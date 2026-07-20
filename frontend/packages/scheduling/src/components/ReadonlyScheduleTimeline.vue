<script setup lang="ts">
import { LockIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed } from 'vue'
import type { TimeScale } from '../engine/engine'
import type { ScheduleModel, ScheduleTask } from '../model/types'

const props = withDefaults(
  defineProps<{
    model: ScheduleModel
    view: 'order' | 'resource'
    scale?: TimeScale
    groupBy?: string
  }>(),
  { scale: 'auto', groupBy: 'workCenter' },
)

const emit = defineEmits<{
  taskSelect: [taskId: string]
  conflictClick: [taskId: string]
}>()

const EIGHT_HOURS = 8 * 60 * 60 * 1000
const ONE_DAY = 24 * 60 * 60 * 1000
const LABEL_WIDTH = 224

interface PositionedTask {
  task: ScheduleTask
  row: number
  left: number
  width: number
}

interface TimelineLane {
  id: string
  label: string
  tasks: PositionedTask[]
  rowCount: number
}

const operationTasks = computed(() =>
  props.model.tasks
    .filter((task) => task.type === 'operation')
    .filter(
      (task) =>
        Number.isFinite(Date.parse(task.startUtc)) && Number.isFinite(Date.parse(task.endUtc)),
    )
    .filter((task) => Date.parse(task.endUtc) > Date.parse(task.startUtc)),
)

const resolvedScale = computed<'shift' | 'day'>(() => {
  if (props.scale === 'hour') return 'shift'
  if (props.scale !== 'auto') return 'day'
  const duration = Date.parse(props.model.horizon.endUtc) - Date.parse(props.model.horizon.startUtc)
  return duration > 0 && duration <= 2 * ONE_DAY ? 'shift' : 'day'
})

const stepMs = computed(() => (resolvedScale.value === 'shift' ? EIGHT_HOURS : ONE_DAY))

function floorToLocalBoundary(timestamp: number) {
  const value = new Date(timestamp)
  if (resolvedScale.value === 'day') {
    value.setHours(0, 0, 0, 0)
  } else {
    value.setHours(Math.floor(value.getHours() / 8) * 8, 0, 0, 0)
  }
  return value.getTime()
}

function nextLocalBoundary(timestamp: number) {
  const value = new Date(timestamp)
  if (resolvedScale.value === 'day') value.setDate(value.getDate() + 1)
  else value.setHours(value.getHours() + 8)
  return value.getTime()
}

function ceilToLocalBoundary(timestamp: number) {
  const floor = floorToLocalBoundary(timestamp)
  return floor === timestamp ? floor : nextLocalBoundary(floor)
}

const range = computed(() => {
  const starts = operationTasks.value.map((task) => Date.parse(task.startUtc))
  const ends = operationTasks.value.map((task) => Date.parse(task.endUtc))
  const rawStart = Date.parse(props.model.horizon.startUtc)
  const rawEnd = Date.parse(props.model.horizon.endUtc)
  const fallbackStart = starts.length > 0 ? Math.min(...starts) : Date.now()
  const fallbackEnd = ends.length > 0 ? Math.max(...ends) : fallbackStart + stepMs.value
  const startCandidates = [...starts, fallbackStart]
  const endCandidates = [...ends, fallbackEnd]
  if (Number.isFinite(rawStart)) startCandidates.push(rawStart)
  if (Number.isFinite(rawEnd)) endCandidates.push(rawEnd)
  const axisStart = floorToLocalBoundary(Math.min(...startCandidates))
  const axisEnd = Math.max(
    nextLocalBoundary(axisStart),
    ceilToLocalBoundary(Math.max(...endCandidates)),
  )
  return { start: axisStart, end: axisEnd, duration: axisEnd - axisStart }
})

const ticks = computed(() => {
  const result: Array<{ key: number; label: string; left: number; width: number }> = []
  for (let time = range.value.start; time < range.value.end; time = nextLocalBoundary(time)) {
    const next = nextLocalBoundary(time)
    result.push({
      key: time,
      label: tickLabel(time),
      left: ((time - range.value.start) / range.value.duration) * 100,
      width: ((next - time) / range.value.duration) * 100,
    })
  }
  return result
})

const timelineWidth = computed(() => {
  const unitWidth = resolvedScale.value === 'shift' ? 168 : 144
  return Math.max(720, ticks.value.length * unitWidth)
})

const resourceNames = computed(
  () => new Map(props.model.resources.map((resource) => [resource.id, resource.text])),
)

const laneLabel = (task: ScheduleTask, laneId: string) => {
  if (props.view === 'order') return task.orderId || '未关联工单'
  return (
    task.dimensions?.[props.groupBy]?.label ??
    resourceNames.value.get(laneId) ??
    task.workCenterId ??
    task.resourceId ??
    '未分配资源'
  )
}

const lanes = computed<TimelineLane[]>(() => {
  const groups = new Map<string, ScheduleTask[]>()
  for (const task of operationTasks.value) {
    const laneId =
      props.view === 'order'
        ? task.orderId || '__unlinked__'
        : (task.dimensions?.[props.groupBy]?.id ?? task.resourceId ?? '__unassigned__')
    const tasks = groups.get(laneId) ?? []
    tasks.push(task)
    groups.set(laneId, tasks)
  }

  return [...groups.entries()].map(([id, tasks]) => {
    const rowEnds: number[] = []
    const positioned = [...tasks]
      .sort((left, right) => Date.parse(left.startUtc) - Date.parse(right.startUtc))
      .map((task) => {
        const start = Date.parse(task.startUtc)
        const end = Date.parse(task.endUtc)
        let row = rowEnds.findIndex((rowEnd) => rowEnd <= start)
        if (row === -1) {
          row = rowEnds.length
          rowEnds.push(end)
        } else {
          rowEnds[row] = end
        }
        return {
          task,
          row,
          left: ((start - range.value.start) / range.value.duration) * 100,
          width: ((end - start) / range.value.duration) * 100,
        }
      })

    return {
      id,
      label: laneLabel(tasks[0]!, id),
      tasks: positioned,
      rowCount: Math.max(1, rowEnds.length),
    }
  })
})

const groupLabel = computed(() => {
  if (props.view === 'order') return '工单 / 工序'
  return (
    props.model.groupDimensions?.find((dimension) => dimension.key === props.groupBy)?.label ??
    '资源'
  )
})

function tickLabel(timestamp: number) {
  const value = new Date(timestamp)
  if (resolvedScale.value === 'shift') {
    const start = value.toLocaleTimeString('zh-CN', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    })
    const end = new Date(timestamp + EIGHT_HOURS).toLocaleTimeString('zh-CN', {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    })
    return `${value.toLocaleDateString('zh-CN', { month: 'numeric', day: 'numeric' })} ${start}–${end}`
  }
  return value.toLocaleDateString('zh-CN', { month: 'numeric', day: 'numeric', weekday: 'short' })
}

function taskTime(task: ScheduleTask) {
  const options: Intl.DateTimeFormatOptions = {
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }
  return `${new Date(task.startUtc).toLocaleString('zh-CN', options)}–${new Date(task.endUtc).toLocaleString('zh-CN', options)}`
}

function taskLabel(task: ScheduleTask) {
  if (task.text.trim()) return task.text
  const sequence = task.operationSequence > 0 ? `第 ${task.operationSequence} 道` : '工序'
  return `${task.orderId || '未关联工单'} · ${sequence}`
}

function selectTask(task: ScheduleTask) {
  emit('taskSelect', task.id)
  if (task.hasConflict) emit('conflictClick', task.id)
}
</script>

<template>
  <div
    data-testid="readonly-schedule-timeline"
    class="nv-readonly-timeline h-full min-h-80 overflow-auto rounded-md border bg-card"
  >
    <div :style="{ width: `${LABEL_WIDTH + timelineWidth}px` }">
      <div
        class="nv-timeline-grid nv-timeline-header"
        :style="{ gridTemplateColumns: `${LABEL_WIDTH}px ${timelineWidth}px` }"
      >
        <div class="nv-timeline-label nv-timeline-label--header">{{ groupLabel }}</div>
        <div class="nv-timeline-axis" aria-hidden="true">
          <div
            v-for="tick in ticks"
            :key="tick.key"
            class="nv-timeline-tick"
            :style="{ left: `${tick.left}%`, width: `${tick.width}%` }"
          >
            {{ tick.label }}
          </div>
        </div>
      </div>

      <div
        v-for="lane in lanes"
        :key="lane.id"
        :data-resource-lane="view === 'resource' ? lane.id : undefined"
        class="nv-timeline-grid nv-timeline-lane"
        :style="{
          gridTemplateColumns: `${LABEL_WIDTH}px ${timelineWidth}px`,
          minHeight: `${Math.max(72, lane.rowCount * 58 + 12)}px`,
        }"
      >
        <div class="nv-timeline-label">
          <span class="nv-timeline-label__name">{{ lane.label }}</span>
          <span class="nv-timeline-label__count">{{ lane.tasks.length }} 道工序</span>
        </div>
        <div class="nv-timeline-track">
          <span
            v-for="tick in ticks"
            :key="tick.key"
            class="nv-timeline-gridline"
            :style="{ left: `${tick.left}%` }"
            aria-hidden="true"
          />
          <button
            v-for="positioned in lane.tasks"
            :key="positioned.task.id"
            type="button"
            :data-task-id="positioned.task.id"
            :data-conflict="positioned.task.hasConflict || undefined"
            :data-locked="positioned.task.locked || undefined"
            class="nv-timeline-task"
            :class="{
              'nv-timeline-task--conflict': positioned.task.hasConflict,
              'nv-timeline-task--locked': positioned.task.locked,
            }"
            :style="{
              left: `${positioned.left}%`,
              top: `${positioned.row * 58 + 8}px`,
              width: `${positioned.width}%`,
            }"
            :aria-label="`${taskLabel(positioned.task)}，${taskTime(positioned.task)}${positioned.task.hasConflict ? '，冲突' : ''}${positioned.task.locked ? '，锁定' : ''}`"
            @click="selectTask(positioned.task)"
          >
            <span class="nv-timeline-task__title">{{ taskLabel(positioned.task) }}</span>
            <span class="nv-timeline-task__meta">
              <span>{{ taskTime(positioned.task) }}</span>
              <span v-if="positioned.task.hasConflict" class="nv-timeline-task__status">
                <TriangleAlertIcon aria-hidden="true" />冲突
              </span>
              <span v-if="positioned.task.locked" class="nv-timeline-task__status">
                <LockIcon aria-hidden="true" />锁定
              </span>
            </span>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-timeline-grid {
    display: grid;
  }

  .nv-timeline-header {
    position: sticky;
    top: 0;
    z-index: 20;
    min-height: 3rem;
    border-bottom: 1px solid var(--border);
    background: var(--card);
  }

  .nv-timeline-label {
    position: sticky;
    left: 0;
    z-index: 10;
    display: flex;
    min-width: 0;
    flex-direction: column;
    justify-content: center;
    gap: 0.2rem;
    border-right: 1px solid var(--border);
    background: var(--card);
    padding: 0.75rem 1rem;
  }

  .nv-timeline-label--header {
    z-index: 30;
    color: var(--muted-foreground);
    font-size: 0.75rem;
    font-weight: 600;
  }

  .nv-timeline-label__name {
    overflow: hidden;
    color: var(--foreground);
    font-size: 0.875rem;
    font-weight: 600;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .nv-timeline-label__count {
    color: var(--muted-foreground);
    font-size: 0.75rem;
  }

  .nv-timeline-axis,
  .nv-timeline-track {
    position: relative;
    min-width: 0;
  }

  .nv-timeline-tick {
    position: absolute;
    inset-block: 0;
    display: flex;
    align-items: center;
    border-left: 1px solid var(--border);
    color: var(--muted-foreground);
    font-size: 0.72rem;
    font-weight: 500;
    padding-inline: 0.6rem;
    white-space: nowrap;
  }

  .nv-timeline-lane {
    border-bottom: 1px solid color-mix(in oklch, var(--border), transparent 35%);
  }

  .nv-timeline-track {
    background: color-mix(in oklch, var(--muted), transparent 65%);
  }

  .nv-timeline-gridline {
    position: absolute;
    inset-block: 0;
    border-left: 1px dashed color-mix(in oklch, var(--border), transparent 25%);
  }

  .nv-timeline-task {
    position: absolute;
    z-index: 2;
    display: flex;
    height: 3rem;
    min-width: 0;
    flex-direction: column;
    justify-content: center;
    gap: 0.2rem;
    overflow: hidden;
    border: 1px solid color-mix(in oklch, var(--primary), transparent 25%);
    border-radius: var(--radius-md);
    background: color-mix(in oklch, var(--primary), var(--card) 82%);
    color: var(--foreground);
    padding: 0.4rem 0.6rem;
    text-align: left;
    box-shadow: var(--shadow-xs);
  }

  .nv-timeline-task:hover,
  .nv-timeline-task:focus-visible {
    border-color: var(--primary);
    outline: none;
    box-shadow: 0 0 0 2px color-mix(in oklch, var(--ring), transparent 55%);
  }

  .nv-timeline-task--conflict {
    border-width: 2px;
    border-color: var(--destructive);
    background: color-mix(in oklch, var(--destructive), var(--card) 88%);
  }

  .nv-timeline-task--locked {
    border-style: dashed;
  }

  .nv-timeline-task__title {
    overflow: hidden;
    font-size: 0.75rem;
    font-weight: 700;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .nv-timeline-task__meta {
    display: flex;
    min-width: 0;
    align-items: center;
    gap: 0.45rem;
    overflow: hidden;
    color: var(--muted-foreground);
    font-size: 0.65rem;
    white-space: nowrap;
  }

  .nv-timeline-task__status {
    display: inline-flex;
    flex: none;
    align-items: center;
    gap: 0.15rem;
    color: var(--foreground);
    font-weight: 700;
  }

  .nv-timeline-task__status svg {
    width: 0.7rem;
    height: 0.7rem;
  }
}
</style>
