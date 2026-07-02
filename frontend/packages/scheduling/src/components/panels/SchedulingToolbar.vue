<script setup lang="ts">
import {
  ButtonPro,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
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

const props = withDefaults(
  defineProps<{
    scale: TimeScale
    readOnly: boolean
    canUndo: boolean
    canRedo: boolean
    dirty: boolean
    busy: boolean
    canRepreview?: boolean
    canRelease?: boolean
  }>(),
  { canRepreview: true, canRelease: true },
)

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
  <div class="flex flex-wrap items-center gap-2 border-b border-border/60 bg-card/80 px-5 py-3 backdrop-blur-sm">
    <SelectPro v-model="scaleModel">
      <SelectProTrigger class="h-8 w-24 border-border/70" aria-label="时间刻度"><SelectProValue /></SelectProTrigger>
      <SelectProContent>
        <SelectProItem value="auto">自适应</SelectProItem>
        <SelectProItem value="hour">小时</SelectProItem>
        <SelectProItem value="day">日</SelectProItem>
        <SelectProItem value="week">周</SelectProItem>
        <SelectProItem value="month">月</SelectProItem>
      </SelectProContent>
    </SelectPro>

    <span class="mx-1 h-5 w-px bg-border/60" aria-hidden="true" />

    <div class="flex items-center gap-0.5">
      <ButtonPro size="icon" variant="ghost" class="size-8" aria-label="放大" @click="emit('zoomIn')"><ZoomInIcon aria-hidden="true" /></ButtonPro>
      <ButtonPro size="icon" variant="ghost" class="size-8" aria-label="缩小" @click="emit('zoomOut')"><ZoomOutIcon aria-hidden="true" /></ButtonPro>
      <ButtonPro size="icon" variant="ghost" class="size-8" aria-label="定位到当前" @click="emit('today')"><CalendarClockIcon aria-hidden="true" /></ButtonPro>
      <ButtonPro size="icon" variant="ghost" class="size-8" aria-label="适配窗口" @click="emit('fit')"><MaximizeIcon aria-hidden="true" /></ButtonPro>
    </div>

    <span class="mx-1 h-5 w-px bg-border/60" aria-hidden="true" />

    <div class="flex items-center gap-0.5">
      <ButtonPro size="icon" variant="ghost" class="size-8" aria-label="撤销" :disabled="!canUndo" @click="emit('undo')"><Undo2Icon aria-hidden="true" /></ButtonPro>
      <ButtonPro size="icon" variant="ghost" class="size-8" aria-label="重做" :disabled="!canRedo" @click="emit('redo')"><Redo2Icon aria-hidden="true" /></ButtonPro>
      <ButtonPro size="icon" variant="ghost" class="size-8" :aria-label="readOnly ? '允许编辑' : '锁定为只读'" @click="emit('toggleReadOnly')">
        <LockIcon v-if="readOnly" aria-hidden="true" />
        <LockOpenIcon v-else aria-hidden="true" />
      </ButtonPro>
    </div>

    <div class="ml-auto flex items-center gap-2.5">
      <span v-if="dirty" class="flex items-center gap-1.5 text-xs font-medium text-warning">
        <span class="size-1.5 rounded-full bg-warning" aria-hidden="true" />
        有未应用的调整
      </span>
      <ButtonPro v-if="canRepreview" size="sm" variant="outline" class="border-border/70" :disabled="!dirty || busy" @click="emit('repreview')">
        <RefreshCwIcon aria-hidden="true" />
        重新排程
      </ButtonPro>
      <ButtonPro v-if="canRelease" size="sm" :disabled="busy" @click="emit('release')">
        <CheckIcon aria-hidden="true" />
        发布计划
      </ButtonPro>
    </div>
  </div>
</template>
