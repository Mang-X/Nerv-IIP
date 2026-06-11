<script setup lang="ts">
import { Skeleton } from '@nerv-iip/ui'
import { ref, toRef } from 'vue'
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
    engineKind?: 'auto' | 'native' | 'dhtmlx'
  }>(),
  { scale: 'auto', readOnly: false, loading: false, engineKind: 'auto' },
)

const emit = defineEmits<{
  taskSelect: [taskId: string]
  taskDragEnd: [payload: TaskDragPayload]
  conflictClick: [taskId: string]
}>()

const container = ref<HTMLElement>()

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
    <div v-else ref="container" :data-view="view" :data-engine="engineName" class="h-full w-full" />
  </div>
</template>
