<script setup lang="ts">
import type { StyleValue } from 'vue'
import type { BadgeVariants } from '../badge'
import type { FileUploadRow, FileUploadVariant } from './types'
import { computed } from 'vue'
import { XIcon } from '@lucide/vue'
import { AnimatePresence, motion } from 'motion-v'
import { cn } from '../../../lib/utils'
import { Badge } from '../badge'
import { Button } from '../button'
import { Progress } from '../progress'
import FileUploadRowActions from './FileUploadRowActions.vue'
import FileUploadRowPreview from './FileUploadRowPreview.vue'
import { fileUploadMotion } from './motion'
import { formatFileSize, rowKind } from './useFileUpload'

const props = withDefaults(defineProps<{
  row: FileUploadRow
  variant?: FileUploadVariant
  rowStyle?: StyleValue
}>(), {
  variant: 'default',
})

const MotionDiv = motion.div
const MotionSpan = motion.span

const emits = defineEmits<{
  pause: [id: string]
  resume: [id: string]
  retry: [id: string]
  remove: [id: string]
}>()

const kind = computed(() => rowKind(props.row))
const badgeVariant = computed<BadgeVariants['variant']>(() => {
  switch (props.row.status) {
    case 'completed':
      return 'success'
    case 'failed':
    case 'rejected':
      return 'destructive'
    case 'paused':
      return 'warning'
    case 'queued':
      return 'secondary'
    case 'uploading':
      return 'default'
  }
})
const showProgress = computed(() =>
  props.row.status === 'uploading'
  || props.row.status === 'paused'
  || props.row.status === 'completed',
)
const statusLabel = computed(() => {
  switch (props.row.status) {
    case 'queued':
      return '待上传'
    case 'uploading':
      return '上传中'
    case 'paused':
      return '已暂停'
    case 'completed':
      return '已完成'
    case 'failed':
      return '失败'
    case 'rejected':
      return '已拒绝'
  }
})
const rowAnimate = computed(() => ({ borderColor: 'var(--border)' }))
const progressClass = computed(() => cn(
  props.row.status === 'completed' && '[&_[data-slot=progress-indicator]]:bg-success',
  props.row.status === 'paused' && '[&_[data-slot=progress-indicator]]:bg-warning',
  props.row.status === 'uploading' && '[&_[data-slot=progress-indicator]]:bg-primary',
))
const iconAnimate = computed(() => {
  switch (props.row.status) {
    case 'uploading':
      return { scale: [1, 1.06, 1], opacity: [0.78, 1, 0.86] }
    case 'completed':
      return { scale: [0.96, 1.08, 1], opacity: 1 }
    case 'failed':
    case 'rejected':
      return { x: [0, -2, 2, 0], opacity: 1 }
    case 'paused':
      return { scale: 0.98, opacity: 0.72 }
    case 'queued':
      return { scale: 1, opacity: 1, x: 0 }
  }
})
const iconTransition = computed(() => props.row.status === 'uploading'
  ? { ...fileUploadMotion.fastInvokeLong, repeat: Number.POSITIVE_INFINITY, repeatType: 'mirror' as const }
  : fileUploadMotion.fastInvoke)
const rootClass = computed(() => cn(
  'border-border bg-card relative flex gap-3 border shadow-sm',
  (props.variant === 'default' || props.variant === 'queue') && 'items-center rounded-lg p-3',
  props.variant === 'compact' && 'items-center rounded-md p-2',
  props.variant === 'table' && 'items-center rounded-none border-x-0 border-t-0 p-2 shadow-none last:border-b-0',
  props.variant === 'avatar' && 'items-center rounded-lg p-4 text-center',
  (props.variant === 'gallery' || props.variant === 'image') && 'min-w-0 flex-col items-stretch rounded-lg p-2',
))
const overlayClass = computed(() => cn(
  'pointer-events-none absolute inset-0 border border-transparent',
  props.variant === 'table' ? 'rounded-none' : 'rounded-lg',
))
const previewClass = computed(() => cn(
  (props.variant === 'default' || props.variant === 'queue') && 'size-9',
  props.variant === 'compact' && 'size-8',
  props.variant === 'table' && 'size-10',
  props.variant === 'avatar' && 'size-20 rounded-full',
  props.variant === 'gallery' && 'aspect-[4/3] w-full',
  props.variant === 'image' && 'aspect-video w-full',
))
const contentClass = computed(() => cn(
  'relative min-w-0 flex-1',
  props.variant === 'avatar' && 'text-center',
  (props.variant === 'gallery' || props.variant === 'image') && 'w-full flex-none',
))
const titleRowClass = computed(() => cn(
  'flex gap-2',
  props.variant === 'avatar' ? 'flex-col items-center' : 'items-center',
  (props.variant === 'gallery' || props.variant === 'image') && 'items-start justify-between',
))
const fileNameClass = computed(() => cn(
  'truncate font-medium',
  props.variant === 'compact' || props.variant === 'table' ? 'text-xs' : 'text-sm',
))
const actionsClass = computed(() => cn(
  'relative flex shrink-0 items-center gap-1',
  (props.variant === 'gallery' || props.variant === 'image' || props.variant === 'avatar') && 'justify-end',
))
</script>

<template>
  <div
    v-if="variant === 'gallery' || variant === 'image'"
    data-slot="file-upload-row"
    :data-variant="variant"
    :data-status="row.status"
    :style="rowStyle"
    class="group/file-upload-tile relative overflow-hidden rounded-lg"
  >
    <FileUploadRowPreview
      :row="row"
      :class="cn(variant === 'gallery' ? 'aspect-square w-full' : 'aspect-[4/3] w-full')"
    />
    <div class="pointer-events-none absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/55 to-transparent p-2 opacity-0 transition-opacity duration-150 group-hover/file-upload-tile:opacity-100 group-focus-within/file-upload-tile:opacity-100">
      <div class="truncate text-xs font-medium text-white">
        {{ row.fileName }}
      </div>
    </div>
    <Button
      type="button"
      variant="secondary"
      size="icon-sm"
      :aria-label="`移除 ${row.fileName}`"
      class="absolute top-2 right-2 size-7 rounded-full bg-background/90 opacity-0 shadow-sm backdrop-blur transition-opacity duration-150 group-hover/file-upload-tile:opacity-100 group-focus-within/file-upload-tile:opacity-100"
      @click="emits('remove', row.id)"
    >
      <XIcon />
      <span class="sr-only">移除 {{ row.fileName }}</span>
    </Button>
    <AnimatePresence>
      <MotionDiv
        v-if="showProgress"
        data-motion-progress="true"
        class="absolute inset-x-2 bottom-2"
        :initial="{ opacity: 0, y: 4 }"
        :animate="{ opacity: 1, y: 0 }"
        :exit="{ opacity: 0, y: 4 }"
        :transition="fileUploadMotion.fastInvoke"
      >
        <Progress :model-value="row.progress" :class="cn('h-1 bg-white/25', progressClass)" />
      </MotionDiv>
    </AnimatePresence>
    <span class="sr-only">{{ row.fileName }} {{ statusLabel }}</span>
  </div>

  <div
    v-else-if="variant === 'compact'"
    data-slot="file-upload-row"
    :data-variant="variant"
    :data-status="row.status"
    :style="rowStyle"
    class="border-border bg-card/70 flex min-h-10 items-center gap-2 rounded-md border px-2.5 py-1.5 text-sm"
  >
    <MotionDiv
      data-motion-icon="true"
      :animate="iconAnimate"
      :transition="iconTransition"
      class="shrink-0 will-change-transform"
    >
      <FileUploadRowPreview :row="row" class="size-7" />
    </MotionDiv>
    <div class="min-w-0 flex-1">
      <div class="truncate text-xs font-medium">
        {{ row.fileName }}
      </div>
      <div class="text-muted-foreground truncate text-xs">
        {{ kind.label }} · {{ formatFileSize(row.sizeBytes) }}
      </div>
    </div>
    <Badge :variant="badgeVariant" class="shrink-0">
      {{ statusLabel }}
    </Badge>
    <FileUploadRowActions
      :row="row"
      class="flex shrink-0 items-center gap-1"
      :initial-x="4"
      @pause="emits('pause', $event)"
      @resume="emits('resume', $event)"
      @retry="emits('retry', $event)"
      @remove="emits('remove', $event)"
    />
  </div>

  <div
    v-else-if="variant === 'table'"
    data-slot="file-upload-row"
    :data-variant="variant"
    :data-status="row.status"
    :style="rowStyle"
    class="border-border bg-card grid grid-cols-[minmax(0,1fr)_7rem_7rem_5rem] items-center gap-3 border-b px-4 py-3 text-sm last:border-b-0"
  >
    <div class="flex min-w-0 items-center gap-3">
      <MotionDiv
        data-motion-icon="true"
        :animate="iconAnimate"
        :transition="iconTransition"
        class="shrink-0 will-change-transform"
      >
        <FileUploadRowPreview :row="row" class="size-8" />
      </MotionDiv>
      <div class="min-w-0">
        <div class="truncate font-medium">
          {{ row.fileName }}
        </div>
        <div v-if="row.error" class="text-destructive truncate text-xs">
          {{ row.error }}
        </div>
      </div>
    </div>
    <Badge variant="secondary">
      {{ kind.label }}
    </Badge>
    <span class="text-muted-foreground">{{ formatFileSize(row.sizeBytes) }}</span>
    <FileUploadRowActions
      :row="row"
      class="flex items-center justify-end gap-1"
      @pause="emits('pause', $event)"
      @resume="emits('resume', $event)"
      @retry="emits('retry', $event)"
      @remove="emits('remove', $event)"
    />
  </div>

  <div
    v-else
    data-slot="file-upload-row"
    :data-variant="variant"
    :data-status="row.status"
    :style="rowStyle"
    :class="rootClass"
  >
    <MotionDiv
      aria-hidden="true"
      :class="overlayClass"
      :animate="rowAnimate"
      :transition="fileUploadMotion.fastInvoke"
    />

    <MotionDiv
      data-motion-icon="true"
      :animate="iconAnimate"
      :transition="iconTransition"
      class="relative shrink-0 will-change-transform"
    >
      <FileUploadRowPreview :row="row" :class="previewClass" />
    </MotionDiv>

    <div :class="contentClass">
      <div :class="titleRowClass">
        <span :class="fileNameClass">{{ row.fileName }}</span>
        <AnimatePresence mode="wait">
          <MotionSpan
            :key="row.status"
            data-motion-status="true"
            class="inline-flex"
            :initial="{ opacity: 0, y: 5, scale: 0.96, filter: 'blur(2px)' }"
            :animate="{ opacity: 1, y: 0, scale: 1, filter: 'blur(0px)' }"
            :exit="{ opacity: 0, y: -4, scale: 0.98, filter: 'blur(1px)' }"
            :transition="fileUploadMotion.fastInvoke"
          >
            <Badge :variant="badgeVariant" :data-status="row.status">
              {{ statusLabel }}
            </Badge>
          </MotionSpan>
        </AnimatePresence>
      </div>
      <div class="text-muted-foreground mt-1 text-xs">
        {{ kind.label }} - {{ formatFileSize(row.sizeBytes) }}
      </div>
      <AnimatePresence>
        <MotionDiv
          v-if="showProgress"
          data-motion-progress="true"
          :initial="{ opacity: 0, height: 0, y: -2 }"
          :animate="{ opacity: 1, height: 'auto', y: 0 }"
          :exit="{ opacity: 0, height: 0, y: -2 }"
          :transition="fileUploadMotion.fastInvoke"
        >
          <Progress
            :model-value="row.progress"
            :class="cn('mt-2', progressClass)"
          />
        </MotionDiv>
      </AnimatePresence>
      <p v-if="row.error" class="text-destructive mt-2 text-xs">
        {{ row.error }}
      </p>
    </div>

    <FileUploadRowActions
      :row="row"
      :class="actionsClass"
      @pause="emits('pause', $event)"
      @resume="emits('resume', $event)"
      @retry="emits('retry', $event)"
      @remove="emits('remove', $event)"
    />
  </div>
</template>
