<script setup lang="ts">
import { createPluginRegistration } from '@embedpdf/core'
import { EmbedPDF } from '@embedpdf/core/vue'
import { usePdfiumEngine } from '@embedpdf/engines/vue'
import { DocumentManagerPluginPackage } from '@embedpdf/plugin-document-manager/vue'
import { RenderPluginPackage } from '@embedpdf/plugin-render/vue'
import { ScrollPluginPackage, ScrollStrategy } from '@embedpdf/plugin-scroll/vue'
import { ViewportPluginPackage } from '@embedpdf/plugin-viewport/vue'
import { ZoomMode, ZoomPluginPackage } from '@embedpdf/plugin-zoom/vue'
import { computed } from 'vue'

import PdfPreviewDocument from './PdfPreviewDocument.vue'

const props = defineProps<{
  src: string
}>()

const emit = defineEmits<{
  ready: []
  error: [message: string]
}>()

const documentId = computed(() => `file-preview-pdf:${props.src}`)
const { engine, isLoading, error: engineError } = usePdfiumEngine({
  fontFallback: null,
})
const plugins = computed(() => [
  createPluginRegistration(DocumentManagerPluginPackage, {
    initialDocuments: [
      {
        documentId: documentId.value,
        url: props.src,
        autoActivate: true,
      },
    ],
  }),
  createPluginRegistration(ViewportPluginPackage),
  createPluginRegistration(ScrollPluginPackage, {
    defaultStrategy: ScrollStrategy.Vertical,
    defaultPageGap: 12,
  }),
  createPluginRegistration(RenderPluginPackage),
  createPluginRegistration(ZoomPluginPackage, {
    defaultZoomLevel: ZoomMode.FitWidth,
    minZoom: 0.25,
    maxZoom: 4,
  }),
])
</script>

<template>
  <div data-slot="file-preview-pdf" class="h-full min-h-0 w-full overflow-hidden">
    <div
      v-if="isLoading || !engine"
      class="grid h-full min-h-64 place-items-center bg-muted/20 text-sm text-muted-foreground"
    >
      正在加载 PDF 预览
    </div>
    <div
      v-else-if="engineError"
      class="grid h-full min-h-64 place-items-center bg-muted/20 p-6 text-center text-sm text-destructive"
    >
      {{ engineError.message }}
    </div>
    <EmbedPDF v-else :key="src" :engine="engine" :plugins="plugins" v-slot="{ activeDocumentId }">
      <PdfPreviewDocument
        v-if="activeDocumentId"
        :document-id="activeDocumentId"
        @ready="emit('ready')"
        @error="emit('error', $event)"
      />
    </EmbedPDF>
  </div>
</template>
