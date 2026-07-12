<script setup lang="ts">
import type { FilePreviewKind } from './filePreviewKind'
import {
  ChevronLeftIcon,
  ChevronRightIcon,
  RotateCcwIcon,
  ZoomInIcon,
  ZoomOutIcon,
} from 'lucide-vue-next'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'

import {
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '../../pro/select'
import { Button } from '../button'

type OfficeViewer = {
  destroy: () => void
  opts?: { width?: number }
  _opts?: { width?: number }
  pageCount?: number
  currentPage?: number
  slideCount?: number
  slideIndex?: number
  sheetCount?: number
  sheetNames?: string[]
  goToPage?: (index: number) => Promise<void>
  goToSlide?: (index: number) => Promise<void>
  goToSheet?: (index: number) => Promise<void>
  nextPage?: () => Promise<void>
  prevPage?: () => Promise<void>
  nextSlide?: () => Promise<void>
  prevSlide?: () => Promise<void>
  renderCurrentSlide?: () => Promise<void>
  _render?: () => Promise<void>
}

type CanvasOfficeViewer = OfficeViewer & {
  opts?: { width?: number }
  _opts?: { width?: number }
  renderCurrentSlide?: () => Promise<void>
  _render?: () => Promise<void>
}

const props = defineProps<{
  src: string
  kind: Extract<FilePreviewKind, 'office-docx' | 'office-xlsx' | 'office-pptx'>
}>()

const emit = defineEmits<{
  error: [message: string]
  ready: []
}>()

const containerRef = ref<HTMLElement | null>(null)
const canvasStageRef = ref<HTMLElement | null>(null)
const canvasRef = ref<HTMLCanvasElement | null>(null)
const spreadsheetRef = ref<HTMLElement | null>(null)
const viewer = shallowRef<OfficeViewer | null>(null)
const canvasRenderWidth = shallowRef(0)
const zoomScale = shallowRef(1)
const loading = ref(false)
const errorMessage = ref('')
const currentIndex = ref(0)
const total = ref(0)
const sheetNames = ref<string[]>([])
const minimumCanvasRenderWidth = 280
const minimumZoomScale = 0.5
const maximumZoomScale = 2
const zoomScaleStep = 0.1
let loadToken = 0
let canvasResizeObserver: ResizeObserver | null = null
let canvasResizeFrame = 0
let canvasRerenderTimer = 0
let canvasRerenderInFlight = false
let canvasRerenderPending = false
const canvasRerenderDelay = 160

const usesCanvas = computed(() => props.kind !== 'office-xlsx')
const indexLabel = computed(() => {
  if (!total.value) {
    return props.kind === 'office-xlsx' ? 'Sheet' : 'Page'
  }

  if (props.kind === 'office-xlsx') {
    const name = sheetNames.value[currentIndex.value] ?? `Sheet ${currentIndex.value + 1}`
    return `${name} · ${currentIndex.value + 1}/${total.value}`
  }

  return `${currentIndex.value + 1}/${total.value}`
})
const canGoPrevious = computed(() => currentIndex.value > 0 && !loading.value)
const canGoNext = computed(
  () => total.value > 0 && currentIndex.value < total.value - 1 && !loading.value,
)
const zoomPercent = computed(() => `${Math.round(zoomScale.value * 100)}%`)
const canZoomOut = computed(
  () => usesCanvas.value && zoomScale.value > minimumZoomScale && !loading.value,
)
const canZoomIn = computed(
  () => usesCanvas.value && zoomScale.value < maximumZoomScale && !loading.value,
)
const navigationUnit = computed(() => {
  if (props.kind === 'office-pptx') {
    return 'slide'
  }
  if (props.kind === 'office-xlsx') {
    return 'sheet'
  }
  return 'page'
})
const jumpSelectLabel = computed(() => {
  if (props.kind === 'office-xlsx') {
    return '选择工作表'
  }
  if (props.kind === 'office-pptx') {
    return '选择幻灯片'
  }
  return '选择页码'
})
const jumpOptions = computed(() => {
  if (!total.value) {
    return []
  }

  if (props.kind === 'office-xlsx') {
    return Array.from({ length: total.value }, (_, index) => ({
      label: sheetNames.value[index] ?? `工作表 ${index + 1}`,
      value: String(index),
    }))
  }

  const labelSuffix = props.kind === 'office-pptx' ? '张' : '页'
  return Array.from({ length: total.value }, (_, index) => ({
    label: `第 ${index + 1} ${labelSuffix}`,
    value: String(index),
  }))
})

function destroyViewer() {
  clearScheduledCanvasRerender()
  viewer.value?.destroy()
  viewer.value = null
  if (spreadsheetRef.value) {
    spreadsheetRef.value.innerHTML = ''
  }
}

function clearScheduledCanvasRerender() {
  if (canvasRerenderTimer) {
    window.clearTimeout(canvasRerenderTimer)
    canvasRerenderTimer = 0
  }
  canvasRerenderPending = false
}

function measureCanvasRenderWidth() {
  if (!canvasStageRef.value) {
    return 0
  }

  const style = window.getComputedStyle(canvasStageRef.value)
  const horizontalPadding =
    Number.parseFloat(style.paddingLeft) + Number.parseFloat(style.paddingRight)
  const contentWidth =
    canvasStageRef.value.clientWidth - (Number.isFinite(horizontalPadding) ? horizontalPadding : 0)
  return Math.max(minimumCanvasRenderWidth, Math.floor(contentWidth))
}

function getEffectiveCanvasRenderWidth() {
  const baseWidth = canvasRenderWidth.value || measureCanvasRenderWidth()
  return Math.max(minimumCanvasRenderWidth, Math.round(baseWidth * zoomScale.value))
}

function syncCanvasRenderWidth() {
  if (!usesCanvas.value) {
    return
  }

  const nextWidth = measureCanvasRenderWidth()
  if (nextWidth > 0 && Math.abs(nextWidth - canvasRenderWidth.value) >= 2) {
    const hasRenderedWidth = canvasRenderWidth.value > 0
    canvasRenderWidth.value = nextWidth
    if (hasRenderedWidth) {
      scheduleCanvasViewerRerender()
    }
  }
}

function scheduleCanvasViewerRerender() {
  if (
    !usesCanvas.value ||
    !viewer.value ||
    loading.value ||
    errorMessage.value ||
    !canvasRenderWidth.value
  ) {
    return
  }

  canvasRerenderPending = true
  if (canvasRerenderTimer) {
    window.clearTimeout(canvasRerenderTimer)
  }

  canvasRerenderTimer = window.setTimeout(() => {
    canvasRerenderTimer = 0
    void rerenderCanvasViewer()
  }, canvasRerenderDelay)
}

async function rerenderCanvasViewer() {
  if (canvasRerenderInFlight) {
    return
  }

  canvasRerenderInFlight = true
  try {
    while (canvasRerenderPending) {
      canvasRerenderPending = false
      const active = viewer.value
      const token = loadToken
      const width = canvasRenderWidth.value

      if (
        !active ||
        token !== loadToken ||
        !usesCanvas.value ||
        loading.value ||
        errorMessage.value ||
        !width
      ) {
        return
      }

      const renderWidth = getEffectiveCanvasRenderWidth()
      setCanvasViewerRenderWidth(active, renderWidth)

      try {
        await rerenderActiveCanvasViewer(active)
      } catch (err) {
        if (token === loadToken) {
          handleError(err)
        }
      }
    }
  } finally {
    canvasRerenderInFlight = false
  }
}

function setCanvasViewerRenderWidth(active: CanvasOfficeViewer, renderWidth: number) {
  // @silurus/ooxml 0.69.0 exposes navigation publicly, but not Viewer resize.
  // Keep the private width write isolated so future package upgrades have one
  // small compatibility point instead of coupling the render flow throughout.
  active.opts = active.opts ?? {}
  active.opts.width = renderWidth
  active._opts = active._opts ?? {}
  active._opts.width = renderWidth
}

async function rerenderActiveCanvasViewer(active: CanvasOfficeViewer) {
  // No public Viewer rerender API exists in @silurus/ooxml 0.69.0; these private
  // methods are the only path that preserves the current document and text layer
  // while resizing without creating another worker-backed viewer.
  if (props.kind === 'office-pptx') {
    await active.renderCurrentSlide?.()
  } else {
    await active._render?.()
  }
}

function setZoomScale(nextScale: number) {
  const clamped = Math.min(
    maximumZoomScale,
    Math.max(minimumZoomScale, Math.round(nextScale * 10) / 10),
  )
  if (Math.abs(clamped - zoomScale.value) < 0.001) {
    return
  }

  zoomScale.value = clamped
  scheduleCanvasViewerRerender()
}

function zoomOut() {
  setZoomScale(zoomScale.value - zoomScaleStep)
}

function zoomIn() {
  setZoomScale(zoomScale.value + zoomScaleStep)
}

function resetZoom() {
  setZoomScale(1)
}

function scheduleCanvasRenderWidthSync() {
  if (canvasResizeFrame) {
    cancelAnimationFrame(canvasResizeFrame)
  }

  canvasResizeFrame = requestAnimationFrame(() => {
    canvasResizeFrame = 0
    syncCanvasRenderWidth()
  })
}

function installCanvasResizeObserver() {
  canvasResizeObserver?.disconnect()
  canvasResizeObserver = null

  if (!usesCanvas.value || !canvasStageRef.value || typeof ResizeObserver === 'undefined') {
    syncCanvasRenderWidth()
    return
  }

  canvasResizeObserver = new ResizeObserver(scheduleCanvasRenderWidthSync)
  canvasResizeObserver.observe(canvasStageRef.value)
  syncCanvasRenderWidth()
}

function handleError(err: unknown) {
  const message = err instanceof Error ? err.message : '无法渲染此 Office 文档。'
  errorMessage.value = message
  emit('error', message)
}

async function loadViewer() {
  const token = ++loadToken
  loading.value = true
  errorMessage.value = ''
  currentIndex.value = 0
  total.value = 0
  sheetNames.value = []
  zoomScale.value = 1
  destroyViewer()

  await nextTick()

  if (token !== loadToken || !containerRef.value) {
    if (token === loadToken) {
      loading.value = false
    }
    return
  }

  if (usesCanvas.value && !canvasRenderWidth.value) {
    syncCanvasRenderWidth()
  }

  try {
    const renderWidth = usesCanvas.value ? getEffectiveCanvasRenderWidth() : 0

    if (props.kind === 'office-docx') {
      if (!canvasRef.value) {
        throw new Error('Document canvas is unavailable.')
      }
      const { DocxViewer } = await import('@silurus/ooxml/docx')
      const docxViewer = new DocxViewer(canvasRef.value, {
        container: containerRef.value,
        enableTextSelection: true,
        width: renderWidth,
        onPageChange: (index, count) => {
          currentIndex.value = index
          total.value = count
        },
        onError: handleError,
      })
      viewer.value = docxViewer as unknown as OfficeViewer
      await docxViewer.load(props.src)
      total.value = docxViewer.pageCount
    } else if (props.kind === 'office-pptx') {
      if (!canvasRef.value) {
        throw new Error('Presentation canvas is unavailable.')
      }
      const { PptxViewer } = await import('@silurus/ooxml/pptx')
      const pptxViewer = new PptxViewer(canvasRef.value, {
        enableTextSelection: true,
        width: renderWidth,
        onSlideChange: (index, count) => {
          currentIndex.value = index
          total.value = count
        },
        onError: handleError,
      })
      viewer.value = pptxViewer as unknown as OfficeViewer
      await pptxViewer.load(props.src)
      total.value = pptxViewer.slideCount
    } else {
      if (!spreadsheetRef.value) {
        throw new Error('Spreadsheet container is unavailable.')
      }
      const { XlsxViewer } = await import('@silurus/ooxml/xlsx')
      const xlsxViewer = new XlsxViewer(spreadsheetRef.value, {
        selectionColor: 'var(--ring)',
        showZoomSlider: false,
        onReady: (names) => {
          sheetNames.value = names
          total.value = names.length
        },
        onSheetChange: (index, count) => {
          currentIndex.value = index
          total.value = count
        },
        onError: handleError,
      })
      viewer.value = xlsxViewer as unknown as OfficeViewer
      await xlsxViewer.load(props.src)
      sheetNames.value = xlsxViewer.sheetNames
      total.value = xlsxViewer.sheetCount
    }

    if (!errorMessage.value) {
      emit('ready')
    }
  } catch (err) {
    handleError(err)
  } finally {
    if (token === loadToken) {
      loading.value = false
    }
  }
}

async function goPrevious() {
  const active = viewer.value
  if (!active || !canGoPrevious.value) {
    return
  }

  if (props.kind === 'office-docx') {
    await active.prevPage?.()
  } else if (props.kind === 'office-pptx') {
    await active.prevSlide?.()
  } else {
    await active.goToSheet?.(currentIndex.value - 1)
  }
}

async function goNext() {
  const active = viewer.value
  if (!active || !canGoNext.value) {
    return
  }

  if (props.kind === 'office-docx') {
    await active.nextPage?.()
  } else if (props.kind === 'office-pptx') {
    await active.nextSlide?.()
  } else {
    await active.goToSheet?.(currentIndex.value + 1)
  }
}

async function goToIndex(value: unknown) {
  if (typeof value !== 'string' || loading.value) {
    return
  }

  const nextIndex = Number(value)
  if (!Number.isInteger(nextIndex) || nextIndex < 0 || nextIndex >= total.value) {
    return
  }

  const active = viewer.value
  if (!active) {
    return
  }

  try {
    if (props.kind === 'office-docx') {
      if (active.goToPage) {
        await active.goToPage(nextIndex)
      } else {
        await stepToIndex(active, nextIndex)
      }
    } else if (props.kind === 'office-pptx') {
      if (active.goToSlide) {
        await active.goToSlide(nextIndex)
      } else {
        await stepToIndex(active, nextIndex)
      }
    } else {
      await active.goToSheet?.(nextIndex)
    }
  } catch (err) {
    handleError(err)
  }
}

async function stepToIndex(active: OfficeViewer, nextIndex: number) {
  const distance = nextIndex - currentIndex.value

  for (let index = 0; index < Math.abs(distance); index += 1) {
    if (props.kind === 'office-pptx') {
      await (distance > 0 ? active.nextSlide?.() : active.prevSlide?.())
    } else {
      await (distance > 0 ? active.nextPage?.() : active.prevPage?.())
    }
  }
}

watch(() => [props.src, props.kind] as const, loadViewer, { immediate: true, flush: 'post' })

watch(
  () => props.kind,
  async () => {
    await nextTick()
    installCanvasResizeObserver()
  },
)

onMounted(() => {
  installCanvasResizeObserver()
})

onBeforeUnmount(() => {
  loadToken += 1
  if (canvasResizeFrame) {
    cancelAnimationFrame(canvasResizeFrame)
  }
  clearScheduledCanvasRerender()
  canvasResizeObserver?.disconnect()
  destroyViewer()
})
</script>

<template>
  <div data-slot="file-preview-office" class="grid h-full min-h-0 grid-rows-[auto_minmax(0,1fr)]">
    <div
      class="flex items-center justify-between gap-2 border-b border-border/70 bg-muted/35 px-2 py-1.5"
    >
      <div class="flex min-w-0 items-center gap-2">
        <NvSelect
          v-if="jumpOptions.length > 0"
          data-slot="file-preview-office-jump-select"
          :model-value="String(currentIndex)"
          @update:model-value="goToIndex"
        >
          <NvSelectTrigger
            class="h-7 w-32 max-w-[42vw] font-mono text-xs"
            :aria-label="jumpSelectLabel"
            :disabled="loading"
          >
            <NvSelectValue />
          </NvSelectTrigger>
          <NvSelectContent class="max-h-64 min-w-32">
            <NvSelectItem v-for="option in jumpOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <div class="min-w-0 truncate font-mono text-xs text-muted-foreground">
          {{ indexLabel }}
        </div>
      </div>
      <div class="flex items-center gap-2">
        <div v-if="usesCanvas" class="flex items-center gap-1">
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Zoom out document"
            :disabled="!canZoomOut"
            @click="zoomOut"
          >
            <ZoomOutIcon aria-hidden="true" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            class="h-7 min-w-12 px-2 font-mono text-xs"
            aria-label="Reset document zoom"
            :disabled="loading || Math.abs(zoomScale - 1) < 0.001"
            @click="resetZoom"
          >
            {{ zoomPercent }}
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Zoom in document"
            :disabled="!canZoomIn"
            @click="zoomIn"
          >
            <ZoomInIcon aria-hidden="true" />
          </Button>
        </div>
        <div class="flex items-center gap-1">
          <Button
            variant="ghost"
            size="icon-sm"
            :aria-label="`Previous ${navigationUnit}`"
            :disabled="!canGoPrevious"
            @click="goPrevious"
          >
            <ChevronLeftIcon aria-hidden="true" />
          </Button>
          <Button
            variant="ghost"
            size="icon-sm"
            :aria-label="`Next ${navigationUnit}`"
            :disabled="!canGoNext"
            @click="goNext"
          >
            <ChevronRightIcon aria-hidden="true" />
          </Button>
        </div>
      </div>
    </div>

    <div class="relative min-h-0 overflow-auto bg-muted/20 p-4">
      <div
        v-if="loading"
        class="absolute inset-0 z-10 grid place-items-center bg-background/70 text-sm text-muted-foreground"
      >
        正在加载预览
      </div>
      <div
        v-if="errorMessage"
        class="grid h-full min-h-64 place-items-center text-sm text-destructive"
      >
        {{ errorMessage }}
      </div>
      <div
        v-show="!errorMessage"
        ref="containerRef"
        class="mx-auto h-full min-h-64 w-full overflow-auto rounded-md border border-border bg-slate-100 text-slate-950 shadow-sm [color-scheme:light]"
      >
        <div
          v-if="usesCanvas"
          ref="canvasStageRef"
          class="grid min-h-full min-w-full items-start justify-items-center bg-slate-100 p-3"
        >
          <canvas ref="canvasRef" class="block max-w-none bg-white shadow-sm" />
        </div>
        <div
          v-else
          ref="spreadsheetRef"
          data-slot="file-preview-spreadsheet-host"
          class="file-preview-spreadsheet-host h-full min-h-full w-full min-w-full bg-white text-slate-950"
        />
      </div>
    </div>
  </div>
</template>

<style scoped>
/* file-preview is a self-owned subsystem (not原版 shadcn), so its styles belong
   in the library component layer (ADR 0020 §4.1). The `!important` still wins:
   for important declarations layer order is reversed, so a layered important
   out-ranks the embedded viewer's unlayered rules. */
@layer nv-components {
  .file-preview-spreadsheet-host :deep(> div) {
    border: 0 !important;
  }

  .file-preview-spreadsheet-host :deep(> div > div:last-child) {
    display: none !important;
  }
}
</style>
