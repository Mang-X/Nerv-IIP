<script setup lang="ts">
import {
  Button,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
} from '@nerv-iip/ui'
import {
  CalendarClockIcon,
  CheckIcon,
  LockIcon,
  LockOpenIcon,
  MaximizeIcon,
  RefreshCwIcon,
  Redo2Icon,
  Undo2Icon,
  ZoomInIcon,
  ZoomOutIcon,
} from 'lucide-vue-next'
import { computed } from 'vue'
import type { TimeScale } from '../../engine/engine'

const props = defineProps<{
  scale: TimeScale
  readOnly: boolean
  canUndo: boolean
  canRedo: boolean
  dirty: boolean
  busy: boolean
}>()

const emit = defineEmits<{
  scaleChange: [scale: TimeScale]
  zoomIn: []
  zoomOut: []
  today: []
  fit: []
  undo: []
  redo: []
  repreview: []
  release: []
  toggleReadOnly: []
}>()

const scaleModel = computed({
  get: () => props.scale,
  set: (v) => emit('scaleChange', v as TimeScale),
})
</script>

<template>
  <div class="flex flex-wrap items-center gap-2 border-b border-border bg-card px-3 py-2">
    <Select v-model="scaleModel">
      <SelectTrigger class="h-8 w-28" aria-label="时间刻度"><SelectValue /></SelectTrigger>
      <SelectContent>
        <SelectItem value="auto">自适应</SelectItem>
        <SelectItem value="hour">小时</SelectItem>
        <SelectItem value="day">日</SelectItem>
        <SelectItem value="week">周</SelectItem>
        <SelectItem value="month">月</SelectItem>
      </SelectContent>
    </Select>

    <div class="flex items-center gap-1">
      <Button size="icon" variant="ghost" aria-label="放大" @click="emit('zoomIn')"><ZoomInIcon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" aria-label="缩小" @click="emit('zoomOut')"><ZoomOutIcon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" aria-label="定位到当前" @click="emit('today')"><CalendarClockIcon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" aria-label="适配窗口" @click="emit('fit')"><MaximizeIcon aria-hidden="true" /></Button>
    </div>

    <div class="flex items-center gap-1">
      <Button size="icon" variant="ghost" aria-label="撤销" :disabled="!canUndo" @click="emit('undo')"><Undo2Icon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" aria-label="重做" :disabled="!canRedo" @click="emit('redo')"><Redo2Icon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" :aria-label="readOnly ? '允许编辑' : '锁定为只读'" @click="emit('toggleReadOnly')">
        <LockIcon v-if="readOnly" aria-hidden="true" />
        <LockOpenIcon v-else aria-hidden="true" />
      </Button>
    </div>

    <div class="ml-auto flex items-center gap-2">
      <StatusBadge v-if="dirty" tone="warning" label="有未应用的调整" />
      <Button size="sm" variant="outline" :disabled="!dirty || busy" @click="emit('repreview')">
        <RefreshCwIcon aria-hidden="true" />
        重新排程
      </Button>
      <Button size="sm" :disabled="busy" @click="emit('release')">
        <CheckIcon aria-hidden="true" />
        发布计划
      </Button>
    </div>
  </div>
</template>
