<script setup lang="ts">
import { ref } from 'vue'
import type { EngineCommand, TaskDragPayload, TimeScale } from '../engine/engine'
import type { ScheduleModel } from '../model/types'
import SchedulingCanvas from './SchedulingCanvas.vue'

// 工单甘特:工单 → 工序 WBS 视角时间线(依赖链 / 关键路径 / 里程碑 / 进度)。
defineProps<{
  model?: ScheduleModel
  scale?: TimeScale
  readOnly?: boolean
  loading?: boolean
  engineKind?: 'auto' | 'dhtmlx'
}>()

defineEmits<{
  taskSelect: [taskId: string]
  taskDragEnd: [payload: TaskDragPayload]
  conflictClick: [taskId: string]
  lockedDragAttempt: [taskId: string]
}>()

const canvas = ref<InstanceType<typeof SchedulingCanvas>>()
function command(cmd: EngineCommand) {
  canvas.value?.command(cmd)
}
defineExpose({ command })
</script>

<template>
  <SchedulingCanvas
    ref="canvas"
    view="order"
    :model="model"
    :scale="scale"
    :read-only="readOnly"
    :loading="loading"
    :engine-kind="engineKind"
    @task-select="$emit('taskSelect', $event)"
    @task-drag-end="$emit('taskDragEnd', $event)"
    @conflict-click="$emit('conflictClick', $event)"
    @locked-drag-attempt="$emit('lockedDragAttempt', $event)"
  />
</template>
