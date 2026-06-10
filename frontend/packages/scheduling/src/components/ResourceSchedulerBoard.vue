<script setup lang="ts">
import { ref } from 'vue'
import type { EngineCommand, TaskDragPayload, TimeScale } from '../engine/engine'
import type { ScheduleModel } from '../model/types'
import SchedulingCanvas from './SchedulingCanvas.vue'

// 资源排产板:工作中心/资源为行的负载视角(工序块按资源时间轴排布 + 利用率)。
defineProps<{
  model?: ScheduleModel
  scale?: TimeScale
  readOnly?: boolean
  loading?: boolean
  engineKind?: 'auto' | 'native' | 'dhtmlx'
}>()

defineEmits<{
  taskSelect: [taskId: string]
  taskDragEnd: [payload: TaskDragPayload]
  conflictClick: [taskId: string]
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
    view="resource"
    :model="model"
    :scale="scale"
    :read-only="readOnly"
    :loading="loading"
    :engine-kind="engineKind"
    @task-select="$emit('taskSelect', $event)"
    @task-drag-end="$emit('taskDragEnd', $event)"
    @conflict-click="$emit('conflictClick', $event)"
  />
</template>
