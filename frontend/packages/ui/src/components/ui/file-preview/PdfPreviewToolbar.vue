<script setup lang="ts">
import { useScroll } from '@embedpdf/plugin-scroll/vue'
import { ZoomMode, useZoom } from '@embedpdf/plugin-zoom/vue'
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  Maximize2Icon,
  ZoomInIcon,
  ZoomOutIcon,
} from '@lucide/vue'
import { computed, onBeforeUnmount, shallowRef } from 'vue'

import {
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '../../pc/select'
import { Button } from '../button'

const props = defineProps<{
  documentId: string
}>()

const { provides: scroll, state: scrollState } = useScroll(() => props.documentId)
const { provides: zoom, state: zoomState } = useZoom(() => props.documentId)

const currentPage = computed(() => scrollState.value.currentPage || 1)
const totalPages = computed(() => scrollState.value.totalPages || 0)
const zoomLabel = computed(() => `${Math.round(zoomState.value.currentZoomLevel * 100)}%`)
const jumping = shallowRef(false)
const canGoPrevious = computed(() => currentPage.value > 1 && !jumping.value)
const canGoNext = computed(
  () => totalPages.value > 0 && currentPage.value < totalPages.value && !jumping.value,
)
const pageOptions = computed(() =>
  Array.from({ length: totalPages.value }, (_, index) => {
    const page = index + 1
    return {
      label: `第 ${page} 页`,
      value: String(page),
    }
  }),
)
let jumpReleaseTimer = 0

async function jumpToPage(value: unknown) {
  if (typeof value !== 'string' || jumping.value) {
    return
  }

  const targetPage = Number(value)
  if (!Number.isInteger(targetPage) || targetPage < 1 || targetPage > totalPages.value) {
    return
  }

  jumping.value = true
  try {
    scroll.value?.scrollToPage({
      pageNumber: targetPage,
      behavior: 'smooth',
    })
  } finally {
    if (jumpReleaseTimer) {
      window.clearTimeout(jumpReleaseTimer)
    }
    jumpReleaseTimer = window.setTimeout(() => {
      jumping.value = false
      jumpReleaseTimer = 0
    }, 187)
  }
}

onBeforeUnmount(() => {
  if (jumpReleaseTimer) {
    window.clearTimeout(jumpReleaseTimer)
  }
})
</script>

<template>
  <div
    data-slot="file-preview-pdf-toolbar"
    class="flex items-center justify-between gap-2 border-b border-border/70 bg-muted/35 px-2 py-1.5"
  >
    <div class="flex min-w-0 items-center gap-1">
      <Button
        variant="ghost"
        size="icon-sm"
        aria-label="Previous page"
        :disabled="!canGoPrevious"
        @click="scroll?.scrollToPreviousPage('smooth')"
      >
        <ChevronLeftIcon aria-hidden="true" />
      </Button>
      <NvSelect
        v-if="pageOptions.length > 0"
        data-slot="file-preview-pdf-page-select"
        :model-value="String(currentPage)"
        @update:model-value="jumpToPage"
      >
        <NvSelectTrigger
          class="h-7 w-24 font-mono text-xs"
          aria-label="选择 PDF 页码"
          :disabled="jumping"
        >
          <NvSelectValue />
        </NvSelectTrigger>
        <NvSelectContent class="max-h-64 min-w-24">
          <NvSelectItem v-for="option in pageOptions" :key="option.value" :value="option.value">
            {{ option.label }}
          </NvSelectItem>
        </NvSelectContent>
      </NvSelect>
      <div v-else class="min-w-16 text-center font-mono text-xs text-muted-foreground">
        {{ currentPage }}/{{ totalPages || '-' }}
      </div>
      <Button
        variant="ghost"
        size="icon-sm"
        aria-label="Next page"
        :disabled="!canGoNext"
        @click="scroll?.scrollToNextPage('smooth')"
      >
        <ChevronRightIcon aria-hidden="true" />
      </Button>
    </div>

    <div class="flex items-center gap-1">
      <Button variant="ghost" size="icon-sm" aria-label="Zoom out PDF" @click="zoom?.zoomOut()">
        <ZoomOutIcon aria-hidden="true" />
      </Button>
      <div
        data-slot="file-preview-pdf-zoom"
        class="min-w-12 text-center font-mono text-xs text-muted-foreground"
      >
        {{ zoomLabel }}
      </div>
      <Button variant="ghost" size="icon-sm" aria-label="Zoom in PDF" @click="zoom?.zoomIn()">
        <ZoomInIcon aria-hidden="true" />
      </Button>
      <div class="mx-1 h-5 w-px bg-border" aria-hidden="true" />
      <Button
        variant="ghost"
        size="icon-sm"
        aria-label="Fit PDF width"
        @click="zoom?.requestZoom(ZoomMode.FitWidth)"
      >
        <Maximize2Icon aria-hidden="true" />
      </Button>
    </div>
  </div>
</template>
