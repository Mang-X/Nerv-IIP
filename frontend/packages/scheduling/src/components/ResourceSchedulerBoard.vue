<script setup lang="ts">
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import type { EngineCommand, TaskDragPayload, TimeScale } from '../engine/engine'
import type { ScheduleModel } from '../model/types'
import SchedulingCanvas from './SchedulingCanvas.vue'

// 资源排产板:一资源一泳道,左轴维度可切换(设备 / 班组 / 产线 / 工作中心…)。
const props = defineProps<{
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
}>()

const dimensions = computed(() => props.model?.groupDimensions ?? [])
const groupBy = ref<string>(dimensions.value[0]?.key ?? 'workCenter')
watch(dimensions, (list) => {
  if (list.length && !list.some((d) => d.key === groupBy.value)) groupBy.value = list[0].key
})

const laneCount = computed(() => {
  const ops = (props.model?.tasks ?? []).filter((t) => t.type === 'operation')
  const lanes = new Set(ops.map((t) => t.dimensions?.[groupBy.value]?.id ?? t.resourceId ?? '未分配'))
  return lanes.size
})

const canvas = ref<InstanceType<typeof SchedulingCanvas>>()
function command(cmd: EngineCommand) {
  canvas.value?.command(cmd)
}
defineExpose({ command })
</script>

<template>
  <div class="flex h-full flex-col">
    <div
      v-if="dimensions.length > 1"
      class="flex items-center gap-2 border-b border-border/50 px-4 py-2"
    >
      <span class="text-xs font-medium text-muted-foreground">分组维度</span>
      <Select v-model="groupBy">
        <SelectTrigger class="h-7 w-28 border-border/70 text-xs" aria-label="分组维度"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem v-for="d in dimensions" :key="d.key" :value="d.key">{{ d.label }}</SelectItem>
        </SelectContent>
      </Select>
      <span class="text-xs text-muted-foreground">·  共 {{ laneCount }} 条泳道</span>
    </div>
    <div class="min-h-0 flex-1">
      <SchedulingCanvas
        ref="canvas"
        view="resource"
        :model="model"
        :scale="scale"
        :read-only="readOnly"
        :loading="loading"
        :group-by="groupBy"
        :engine-kind="engineKind"
        @task-select="$emit('taskSelect', $event)"
        @task-drag-end="$emit('taskDragEnd', $event)"
        @conflict-click="$emit('conflictClick', $event)"
      />
    </div>
  </div>
</template>
