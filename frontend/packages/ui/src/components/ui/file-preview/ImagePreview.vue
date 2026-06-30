<script setup lang="ts">
import { RotateCcwIcon, RotateCwIcon, ZoomInIcon, ZoomOutIcon } from 'lucide-vue-next'
import { motion } from 'motion-v'
import { computed, ref } from 'vue'

import { Button } from '../button'
import { filePreviewMotion } from './filePreviewKind'

const props = defineProps<{
  src: string
  fileName: string
}>()

const emit = defineEmits<{
  ready: []
  error: [message: string]
}>()

const MotionImg = motion.img
const scale = ref(1)
const rotation = ref(0)

const imageStyle = computed(() => ({
  transform: `scale(${scale.value}) rotate(${rotation.value}deg)`,
}))

function zoomIn() {
  scale.value = Math.min(scale.value + 0.1, 3)
}

function zoomOut() {
  scale.value = Math.max(scale.value - 0.1, 0.2)
}

function rotateLeft() {
  rotation.value -= 90
}

function rotateRight() {
  rotation.value += 90
}

function onImageError() {
  emit('error', `无法加载 ${props.fileName}。`)
}
</script>

<template>
  <div data-slot="file-preview-image-view" class="grid h-full min-h-0 grid-rows-[auto_minmax(0,1fr)]">
    <div class="flex items-center gap-1 border-b border-border/70 bg-muted/35 px-2 py-1.5">
      <Button variant="ghost" size="icon-sm" :aria-label="`Zoom out ${fileName}`" @click="zoomOut">
        <ZoomOutIcon aria-hidden="true" />
      </Button>
      <span
        data-slot="file-preview-zoom"
        class="min-w-12 text-center font-mono text-xs text-muted-foreground"
      >
        {{ Math.round(scale * 100) }}%
      </span>
      <Button variant="ghost" size="icon-sm" :aria-label="`Zoom in ${fileName}`" @click="zoomIn">
        <ZoomInIcon aria-hidden="true" />
      </Button>
      <div class="ml-1 h-5 w-px bg-border" aria-hidden="true" />
      <Button variant="ghost" size="icon-sm" :aria-label="`Rotate left ${fileName}`" @click="rotateLeft">
        <RotateCcwIcon aria-hidden="true" />
      </Button>
      <Button variant="ghost" size="icon-sm" :aria-label="`Rotate right ${fileName}`" @click="rotateRight">
        <RotateCwIcon aria-hidden="true" />
      </Button>
    </div>

    <div class="flex min-h-0 items-center justify-center overflow-auto bg-muted/20 p-4">
      <MotionImg
        data-testid="file-preview-image"
        :src="src"
        :alt="fileName"
        class="max-h-full max-w-full select-none rounded-md border border-border bg-background object-contain shadow-sm"
        :style="imageStyle"
        :initial="{ opacity: 0, scale: 0.98 }"
        :animate="{ opacity: 1, scale: 1 }"
        :transition="filePreviewMotion.fastInvoke"
        draggable="false"
        @load="emit('ready')"
        @error="onImageError"
      />
    </div>
  </div>
</template>
