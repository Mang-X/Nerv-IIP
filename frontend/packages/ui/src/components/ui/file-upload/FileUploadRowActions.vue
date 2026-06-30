<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { FileUploadRow } from './types'
import { computed } from 'vue'
import { PauseIcon, PlayIcon, RotateCcwIcon, XIcon } from 'lucide-vue-next'
import { AnimatePresence, motion } from 'motion-v'
import { Button } from '../button'
import { fileUploadMotion } from './motion'

const props = withDefaults(defineProps<{
  row: FileUploadRow
  class?: HTMLAttributes['class']
  initialX?: number
}>(), {
  initialX: 6,
})

const emits = defineEmits<{
  pause: [id: string]
  resume: [id: string]
  retry: [id: string]
  remove: [id: string]
}>()

const MotionDiv = motion.div
const MotionSpan = motion.span

const activeAction = computed(() => {
  switch (props.row.status) {
    case 'uploading':
      return { key: 'pause', icon: PauseIcon, label: `暂停 ${props.row.fileName}`, event: 'pause' as const }
    case 'paused':
      return { key: 'resume', icon: PlayIcon, label: `继续 ${props.row.fileName}`, event: 'resume' as const }
    case 'failed':
      return { key: 'retry', icon: RotateCcwIcon, label: `重试 ${props.row.fileName}`, event: 'retry' as const }
    case 'completed':
    case 'queued':
    case 'rejected':
      return null
  }
})

function emitActiveAction() {
  if (!activeAction.value) {
    return
  }

  switch (activeAction.value.event) {
    case 'pause':
      emits('pause', props.row.id)
      break
    case 'resume':
      emits('resume', props.row.id)
      break
    case 'retry':
      emits('retry', props.row.id)
      break
  }
}
</script>

<template>
  <MotionDiv
    data-motion-actions="true"
    :class="props.class"
    :layout="true"
    :initial="{ opacity: 0, x: initialX }"
    :animate="{ opacity: 1, x: 0 }"
    :transition="fileUploadMotion.fastInvoke"
  >
    <AnimatePresence mode="wait">
      <MotionSpan
        v-if="activeAction"
        :key="activeAction.key"
        :initial="{ opacity: 0, scale: 0.92, x: 4 }"
        :animate="{ opacity: 1, scale: 1, x: 0 }"
        :exit="{ opacity: 0, scale: 0.92, x: -4 }"
        :transition="fileUploadMotion.fastInvoke"
      >
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          :aria-label="activeAction.label"
          @click="emitActiveAction"
        >
          <component :is="activeAction.icon" />
        </Button>
      </MotionSpan>
    </AnimatePresence>
    <Button
      type="button"
      variant="ghost"
      size="icon-sm"
      :aria-label="`移除 ${row.fileName}`"
      @click="emits('remove', row.id)"
    >
      <XIcon />
      <span class="sr-only">移除 {{ row.fileName }}</span>
    </Button>
  </MotionDiv>
</template>
