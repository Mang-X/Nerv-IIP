<script setup lang="ts">
import { Skeleton } from '@nerv-iip/ui'
import { CalendarClockIcon } from '@lucide/vue'
import { computed, ref, toRef } from 'vue'
import type { EngineCommand, TaskDragPayload, TimeScale } from '../engine/engine'
import type { ScheduleModel } from '../model/types'
import { useEngine } from './useEngine'
import '../styles/scheduling.css'

// 内部工作组件:GanttChart(view=order)与 ResourceSchedulerBoard(view=resource)的共享内核。
const props = withDefaults(
  defineProps<{
    view: 'order' | 'resource'
    model?: ScheduleModel
    scale?: TimeScale
    readOnly?: boolean
    loading?: boolean
    groupBy?: string
    engineKind?: 'auto' | 'dhtmlx'
  }>(),
  { scale: 'auto', readOnly: false, loading: false, engineKind: 'auto' },
)

const emit = defineEmits<{
  taskSelect: [taskId: string]
  taskDragEnd: [payload: TaskDragPayload]
  conflictClick: [taskId: string]
  lockedDragAttempt: [taskId: string]
}>()

const container = ref<HTMLElement>()
const isEmpty = computed(() => props.model != null && props.model.tasks.length === 0)

const { engine, engineName } = useEngine({
  container,
  model: toRef(props, 'model'),
  view: props.view,
  scale: toRef(props, 'scale'),
  readOnly: toRef(props, 'readOnly'),
  groupBy: toRef(props, 'groupBy'),
  engineKind: props.engineKind,
  on: {
    taskSelected: (p) => emit('taskSelect', p.taskId),
    taskDragEnd: (p) => emit('taskDragEnd', p),
    conflictClicked: (p) => emit('conflictClick', p.taskId),
    lockedDragAttempt: (p) => emit('lockedDragAttempt', p.taskId),
  },
})

/** 供父组件(工作台)下发命令,如缩放/定位/选中。 */
function command(cmd: EngineCommand) {
  engine.value?.applyCommand(cmd)
}
defineExpose({ command, engineName })
</script>

<template>
  <div class="relative h-full w-full">
    <div
      v-if="loading"
      data-testid="gantt-skeleton"
      class="flex h-full w-full flex-col gap-2 p-4"
    >
      <Skeleton v-for="i in 6" :key="i" class="h-7 w-full" />
    </div>
    <template v-else>
      <div ref="container" :data-view="view" :data-engine="engineName" class="h-full w-full" />
      <!-- 无可用引擎时的优雅占位:容器仍在 DOM 中,引擎一旦可用即可挂载。 -->
      <div
        v-if="isEmpty"
        data-testid="gantt-empty"
        role="status"
        aria-live="polite"
        class="absolute inset-0 flex flex-col items-center justify-center gap-3 rounded-md border border-dashed bg-card text-center text-muted-foreground"
      >
        <CalendarClockIcon class="h-8 w-8" aria-hidden="true" />
        <div class="space-y-1">
          <p class="text-sm font-medium">暂无排程任务</p>
          <p class="text-xs">调整筛选条件或生成排程后再查看甘特图。</p>
        </div>
      </div>
      <div
        v-else-if="engineName === 'unavailable'"
        data-testid="engine-unavailable"
        role="status"
        aria-live="polite"
        class="absolute inset-0 flex flex-col items-center justify-center gap-3 rounded-md border border-dashed bg-card text-center text-muted-foreground"
      >
        <CalendarClockIcon class="h-8 w-8" aria-hidden="true" />
        <div class="space-y-1">
          <p class="text-sm font-medium">排程引擎未加载</p>
          <p class="text-xs">DHTMLX 引擎在生产部署时手动分发;开发环境请配置本地 vendor。</p>
        </div>
      </div>
    </template>
  </div>
</template>
