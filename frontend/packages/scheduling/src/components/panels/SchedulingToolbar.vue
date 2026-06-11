<script setup lang="ts">
import {
  Button,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
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
  <div class="flex flex-wrap items-center gap-1.5 border-b border-border/60 bg-card/80 px-4 py-2.5 backdrop-blur-sm">
    <Select v-model="scaleModel">
      <SelectTrigger class="h-8 w-24 border-border/70" aria-label="时间刻度"><SelectValue /></SelectTrigger>
      <SelectContent>
        <SelectItem value="auto">自适应</SelectItem>
        <SelectItem value="hour">小时</SelectItem>
        <SelectItem value="day">日</SelectItem>
        <SelectItem value="week">周</SelectItem>
        <SelectItem value="month">月</SelectItem>
      </SelectContent>
    </Select>

    <span class="mx-1 h-5 w-px bg-border/60" aria-hidden="true" />

    <div class="flex items-center">
      <Button size="icon" variant="ghost" class="size-8" aria-label="放大" @click="emit('zoomIn')"><ZoomInIcon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" class="size-8" aria-label="缩小" @click="emit('zoomOut')"><ZoomOutIcon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" class="size-8" aria-label="定位到当前" @click="emit('today')"><CalendarClockIcon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" class="size-8" aria-label="适配窗口" @click="emit('fit')"><MaximizeIcon aria-hidden="true" /></Button>
    </div>

    <span class="mx-1 h-5 w-px bg-border/60" aria-hidden="true" />

    <div class="flex items-center">
      <Button size="icon" variant="ghost" class="size-8" aria-label="撤销" :disabled="!canUndo" @click="emit('undo')"><Undo2Icon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" class="size-8" aria-label="重做" :disabled="!canRedo" @click="emit('redo')"><Redo2Icon aria-hidden="true" /></Button>
      <Button size="icon" variant="ghost" class="size-8" :aria-label="readOnly ? '允许编辑' : '锁定为只读'" @click="emit('toggleReadOnly')">
        <LockIcon v-if="readOnly" aria-hidden="true" />
        <LockOpenIcon v-else aria-hidden="true" />
      </Button>
    </div>

    <div class="ml-auto flex items-center gap-2.5">
      <span v-if="dirty" class="flex items-center gap-1.5 text-xs font-medium text-warning">
        <span class="size-1.5 rounded-full bg-warning" aria-hidden="true" />
        有未应用的调整
      </span>
      <Button size="sm" variant="outline" class="border-border/70" :disabled="!dirty || busy" @click="emit('repreview')">
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
