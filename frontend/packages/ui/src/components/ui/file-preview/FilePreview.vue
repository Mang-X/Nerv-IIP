<script setup lang="ts">
import type { FilePreviewEmits, FilePreviewProps } from './filePreviewKind'
import { AlertCircleIcon, ExternalLinkIcon } from 'lucide-vue-next'
import { AnimatePresence, motion, MotionConfig } from 'motion-v'
import { computed, defineAsyncComponent, ref, watch } from 'vue'

import { cn } from '../../../lib/utils'
import { Button } from '../button'
import { Skeleton } from '../skeleton'
import ImagePreview from './ImagePreview.vue'
import UnsupportedPreview from './UnsupportedPreview.vue'
import {
  filePreviewMotion,
  formatFilePreviewSize,
  getFilePreviewKind,
  getFilePreviewKindMeta,
} from './filePreviewKind'

const OfficePreview = defineAsyncComponent(() => import('./OfficePreview.vue'))
const PdfPreview = defineAsyncComponent(() => import('./PdfPreview.vue'))
const props = withDefaults(defineProps<FilePreviewProps>(), {
  contentType: '',
  height: 520,
  loading: false,
  error: null,
  showHeader: true,
})

const emit = defineEmits<FilePreviewEmits>()

const MotionDiv = motion.div
const previewKind = computed(() => getFilePreviewKind(props.fileName, props.contentType))
const kindMeta = computed(() => getFilePreviewKindMeta(previewKind.value))
const formattedSize = computed(() => formatFilePreviewSize(props.sizeBytes))
const hasSource = computed(() => Boolean(props.src))
const internalError = ref('')
const displayedError = computed(() => props.error || internalError.value)
const heightStyle = computed(() => ({
  height: typeof props.height === 'number' ? `${props.height}px` : props.height,
}))
const subtitle = computed(() => {
  const pieces = [kindMeta.value.label, formattedSize.value].filter(Boolean)
  return pieces.join(' · ')
})
const contentKey = computed(() => `${previewKind.value}:${props.src ?? 'empty'}:${props.loading}:${props.error ?? ''}`)

function openSource(src = props.src) {
  if (src) {
    emit('openSource', src)
  }
}

function onChildError(message: string) {
  internalError.value = message
  emit('error', message)
}

watch(
  () => [props.src, previewKind.value] as const,
  () => {
    internalError.value = ''
  },
)

function onChildReady(kind = previewKind.value) {
  if (kind !== 'unsupported') {
    emit('ready', kind)
  }
}
</script>

<template>
  <MotionConfig reduced-motion="user">
    <section
      data-slot="file-preview"
      :class="cn('overflow-hidden rounded-lg border border-border bg-card text-card-foreground shadow-xs', props.class)"
      :style="heightStyle"
    >
      <div class="grid h-full min-h-0 grid-rows-[auto_minmax(0,1fr)]">
        <header
          v-if="showHeader"
          data-slot="file-preview-header"
          class="flex min-h-12 items-center justify-between gap-3 border-b border-border/70 bg-card px-3"
        >
          <div class="flex min-w-0 items-center gap-2.5">
            <div :class="cn('flex size-8 shrink-0 items-center justify-center rounded-md border', kindMeta.iconContainerClass)">
              <component :is="kindMeta.icon" :class="cn('size-4', kindMeta.iconClass)" aria-hidden="true" />
            </div>
            <div class="min-w-0">
              <div class="truncate text-[13px] font-medium leading-[18px]">{{ fileName }}</div>
              <div class="truncate text-xs leading-4 text-muted-foreground">{{ subtitle }}</div>
            </div>
          </div>
          <Button
            v-if="hasSource"
            variant="ghost"
            size="icon-sm"
            :aria-label="`Open ${fileName}`"
            @click="openSource()"
          >
            <ExternalLinkIcon aria-hidden="true" />
          </Button>
        </header>

        <div data-slot="file-preview-body" class="min-h-0 overflow-hidden bg-background">
          <AnimatePresence mode="wait">
            <MotionDiv
              :key="contentKey"
              class="h-full min-h-0"
              :initial="{ opacity: 0, y: 4, scale: 0.995 }"
              :animate="{ opacity: 1, y: 0, scale: 1 }"
              :exit="{ opacity: 0, y: 2, scale: 0.995 }"
              :transition="filePreviewMotion.fastInvoke"
            >
              <div v-if="loading" class="grid h-full min-h-64 grid-rows-[1fr_auto] gap-4 bg-muted/20 p-4">
                <Skeleton class="h-full min-h-48 rounded-md" />
                <Skeleton class="h-8 w-40 rounded-md" />
              </div>

              <div
                v-else-if="displayedError"
                data-slot="file-preview-error"
                class="flex h-full min-h-64 flex-col items-center justify-center gap-3 bg-muted/20 p-6 text-center"
              >
                <AlertCircleIcon class="size-6 text-destructive" aria-hidden="true" />
                <div class="max-w-sm">
                  <div class="text-sm font-semibold leading-5">预览失败</div>
                  <div class="mt-1 text-sm leading-5 text-muted-foreground">{{ displayedError }}</div>
                </div>
              </div>

              <UnsupportedPreview
                v-else-if="!src"
                :file-name="fileName"
                reason="empty"
              />

              <UnsupportedPreview
                v-else-if="previewKind === 'unsupported'"
                :file-name="fileName"
                :src="src"
                reason="unsupported"
                @open-source="openSource"
              />

              <PdfPreview
                v-else-if="previewKind === 'pdf'"
                :src="src"
                @ready="onChildReady('pdf')"
                @error="onChildError"
              />
              <ImagePreview
                v-else-if="previewKind === 'image'"
                :src="src"
                :file-name="fileName"
                @ready="onChildReady('image')"
                @error="onChildError"
              />
              <OfficePreview
                v-else-if="previewKind === 'office-docx' || previewKind === 'office-xlsx' || previewKind === 'office-pptx'"
                :src="src"
                :kind="previewKind"
                @ready="onChildReady(previewKind)"
                @error="onChildError"
              />
            </MotionDiv>
          </AnimatePresence>
        </div>
      </div>
    </section>
  </MotionConfig>
</template>
