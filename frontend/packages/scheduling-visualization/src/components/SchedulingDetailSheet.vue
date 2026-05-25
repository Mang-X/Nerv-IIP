<script setup lang="ts">
import { Badge, Button } from '@nerv-iip/ui'
import { AlertTriangle, PanelRight, X } from 'lucide-vue-next'
import { computed } from 'vue'

import type { GanttFixture, GanttTask } from '../model/gantt'
import type { ScheduleFixture } from '../model/schedule'
import type { SchedulingDetailView, SchedulingWorkspaceSelection } from './types'

interface Props {
  ganttFixture: GanttFixture
  scheduleFixture: ScheduleFixture
  selection?: SchedulingWorkspaceSelection
}

interface Emits {
  clear: []
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()

function findTask(tasks: GanttTask[], taskId: string): GanttTask | undefined {
  for (const task of tasks) {
    if (task.id === taskId) {
      return task
    }

    const child = findTask(task.children ?? [], taskId)
    if (child) {
      return child
    }
  }

  return undefined
}

function dateLabel(value: string) {
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'UTC',
  }).format(new Date(value))
}

function buildTaskDetail(taskId: string): SchedulingDetailView | undefined {
  const task = findTask(props.ganttFixture.tasks, taskId)
  if (!task) {
    return undefined
  }

  const conflict = props.ganttFixture.conflicts.find((item) => task.conflictIds?.includes(item.id))
  return {
    eyebrow: 'Gantt task',
    title: task.name,
    status: task.status,
    description: task.assignee ? `Owner: ${task.assignee}` : undefined,
    fields: [
      { label: 'Code', value: task.code },
      { label: 'Start', value: dateLabel(task.start) },
      { label: 'End', value: dateLabel(task.end) },
      { label: 'Progress', value: `${task.progress}%` },
    ],
    conflictTitle: conflict?.title,
    conflictDescription: conflict?.description,
    conflictResolutionHint: conflict?.resolutionHint,
  }
}

function buildScheduleDetail(selection: SchedulingWorkspaceSelection): SchedulingDetailView | undefined {
  if (selection.selection.kind === 'resource') {
    const resource = props.scheduleFixture.resources.find((item) => item.id === selection.selection.id)
    if (!resource) {
      return undefined
    }

    return {
      eyebrow: 'Schedule resource',
      title: resource.name,
      status: resource.kind,
      description: resource.calendarLabel,
      fields: [
        { label: 'Work center', value: resource.workCenterCode },
        { label: 'Capacity', value: `${resource.capacityPerShift} min per shift` },
      ],
    }
  }

  if (selection.selection.kind === 'operation') {
    const operation = props.scheduleFixture.operations.find((item) => item.id === selection.selection.id)
    if (!operation) {
      return undefined
    }

    const resource = props.scheduleFixture.resources.find((item) => item.id === operation.resourceId)
    const conflict = props.scheduleFixture.conflicts.find((item) => operation.conflictIds?.includes(item.id))
    return {
      eyebrow: 'Schedule operation',
      title: operation.name,
      status: operation.status,
      description: operation.workOrderCode,
      fields: [
        { label: 'Operation', value: operation.operationCode },
        { label: 'SKU', value: operation.skuCode },
        { label: 'Resource', value: resource?.name ?? operation.resourceId },
        { label: 'Start', value: dateLabel(operation.start) },
        { label: 'End', value: dateLabel(operation.end) },
        { label: 'Load', value: `${operation.loadPercent}%` },
      ],
      conflictTitle: conflict?.title,
      conflictDescription: conflict?.description,
      conflictResolutionHint: conflict?.resolutionHint,
    }
  }

  const conflict = props.scheduleFixture.conflicts.find((item) => item.id === selection.selection.id)
  if (!conflict) {
    return undefined
  }

  return {
    eyebrow: 'Schedule conflict',
    title: conflict.title,
    status: conflict.severity,
    description: conflict.description,
    fields: [
      { label: 'Target', value: conflict.targetId },
      { label: 'Target kind', value: conflict.targetKind },
    ],
    conflictResolutionHint: conflict.resolutionHint,
  }
}

const detail = computed<SchedulingDetailView | undefined>(() => {
  if (!props.selection) {
    return undefined
  }

  if (props.selection.source === 'gantt') {
    if (props.selection.selection.kind === 'task') {
      return buildTaskDetail(props.selection.selection.id)
    }

    const conflict = props.ganttFixture.conflicts.find((item) => item.id === props.selection?.selection.id)
    if (!conflict) {
      return undefined
    }

    return {
      eyebrow: 'Gantt conflict',
      title: conflict.title,
      status: conflict.severity,
      description: conflict.description,
      fields: [
        { label: 'Task', value: conflict.taskId },
      ],
      conflictResolutionHint: conflict.resolutionHint,
    }
  }

  return buildScheduleDetail(props.selection)
})
</script>

<template>
  <aside class="scheduling-detail" data-test="scheduling-detail-sheet">
    <div class="scheduling-detail__header">
      <div class="scheduling-detail__title-block">
        <PanelRight class="scheduling-detail__panel-icon" aria-hidden="true" />
        <div>
          <p class="scheduling-detail__eyebrow">Selection</p>
          <h3 class="scheduling-detail__title">
            Detail
          </h3>
        </div>
      </div>
      <Button
        v-if="detail"
        variant="ghost"
        size="icon-sm"
        type="button"
        aria-label="Clear selection"
        @click="emit('clear')"
      >
        <X />
      </Button>
    </div>

    <div v-if="detail" class="scheduling-detail__content">
      <div class="scheduling-detail__headline">
        <p class="scheduling-detail__eyebrow">{{ detail.eyebrow }}</p>
        <h4 class="scheduling-detail__name">{{ detail.title }}</h4>
        <Badge v-if="detail.status" variant="secondary">{{ detail.status }}</Badge>
      </div>

      <p v-if="detail.description" class="scheduling-detail__description">
        {{ detail.description }}
      </p>

      <dl class="scheduling-detail__fields">
        <div v-for="field in detail.fields" :key="field.label" class="scheduling-detail__field">
          <dt>{{ field.label }}</dt>
          <dd>{{ field.value }}</dd>
        </div>
      </dl>

      <div v-if="detail.conflictTitle || detail.conflictResolutionHint" class="scheduling-detail__conflict">
        <div class="scheduling-detail__conflict-title">
          <AlertTriangle aria-hidden="true" />
          <span>{{ detail.conflictTitle ?? 'Resolution hint' }}</span>
        </div>
        <p v-if="detail.conflictDescription">{{ detail.conflictDescription }}</p>
        <p v-if="detail.conflictResolutionHint">{{ detail.conflictResolutionHint }}</p>
      </div>
    </div>

    <div v-else class="scheduling-detail__empty">
      <PanelRight aria-hidden="true" />
      <p>Select a task, resource, or operation to inspect its simulated scheduling facts.</p>
    </div>
  </aside>
</template>

<style scoped>
.scheduling-detail {
  display: flex;
  flex-direction: column;
  min-width: 260px;
  min-height: 100%;
  border: 1px solid hsl(var(--border, 214 32% 91%));
  border-radius: 8px;
  background: hsl(var(--background, 0 0% 100%));
}

.scheduling-detail__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 12px 14px;
  border-bottom: 1px solid hsl(var(--border, 214 32% 91%));
}

.scheduling-detail__title-block {
  display: flex;
  align-items: center;
  gap: 9px;
}

.scheduling-detail__panel-icon {
  width: 18px;
  height: 18px;
  color: #2563eb;
}

.scheduling-detail__eyebrow {
  margin: 0;
  color: #64748b;
  font-size: 12px;
  font-weight: 700;
}

.scheduling-detail__title,
.scheduling-detail__name {
  margin: 0;
  color: #0f172a;
}

.scheduling-detail__title {
  font-size: 15px;
}

.scheduling-detail__content {
  display: grid;
  gap: 14px;
  padding: 14px;
}

.scheduling-detail__headline {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.scheduling-detail__headline .scheduling-detail__eyebrow {
  flex-basis: 100%;
}

.scheduling-detail__name {
  max-width: 100%;
  overflow-wrap: anywhere;
  font-size: 17px;
}

.scheduling-detail__description {
  margin: 0;
  color: #475569;
  font-size: 13px;
}

.scheduling-detail__fields {
  display: grid;
  gap: 8px;
  margin: 0;
}

.scheduling-detail__field {
  display: grid;
  gap: 2px;
  padding: 8px 0;
  border-bottom: 1px solid rgba(226, 232, 240, 0.9);
}

.scheduling-detail__field dt {
  color: #64748b;
  font-size: 11px;
  font-weight: 700;
}

.scheduling-detail__field dd {
  margin: 0;
  color: #0f172a;
  font-size: 13px;
  font-weight: 650;
}

.scheduling-detail__conflict {
  display: grid;
  gap: 6px;
  padding: 10px;
  border: 1px solid rgba(249, 115, 22, 0.35);
  border-radius: 8px;
  background: #fff7ed;
  color: #7c2d12;
}

.scheduling-detail__conflict-title {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  font-weight: 750;
}

.scheduling-detail__conflict-title svg {
  width: 15px;
  height: 15px;
}

.scheduling-detail__conflict p {
  margin: 0;
  font-size: 12px;
}

.scheduling-detail__empty {
  display: grid;
  place-items: center;
  gap: 10px;
  min-height: 220px;
  padding: 18px;
  color: #64748b;
  text-align: center;
}

.scheduling-detail__empty svg {
  width: 24px;
  height: 24px;
}

.scheduling-detail__empty p {
  max-width: 220px;
  margin: 0;
  font-size: 13px;
}
</style>

