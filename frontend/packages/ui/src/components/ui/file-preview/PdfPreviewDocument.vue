<script setup lang="ts">
import { useDocumentState } from '@embedpdf/core/vue'
import { DocumentContent } from '@embedpdf/plugin-document-manager/vue'
import { RenderLayer } from '@embedpdf/plugin-render/vue'
import { Scroller } from '@embedpdf/plugin-scroll/vue'
import { Viewport } from '@embedpdf/plugin-viewport/vue'
import { watch } from 'vue'

import PdfPreviewToolbar from './PdfPreviewToolbar.vue'

const props = defineProps<{
  documentId: string
}>()

const emit = defineEmits<{
  ready: []
  error: [message: string]
}>()

const documentState = useDocumentState(() => props.documentId)

watch(
  () => documentState.value?.status,
  (status) => {
    if (status === 'loaded') {
      emit('ready')
      return
    }

    if (status === 'error') {
      emit('error', documentState.value?.error ?? '无法渲染此 PDF。')
    }
  },
  { immediate: true },
)
</script>

<template>
  <DocumentContent :document-id="documentId" v-slot="{ isLoading, isError, isLoaded, documentState: slotState }">
    <div v-if="isLoaded" class="grid h-full min-h-0 grid-rows-[auto_minmax(0,1fr)]">
      <PdfPreviewToolbar :document-id="documentId" />
      <div class="min-h-0 overflow-hidden bg-slate-100 text-slate-950 [color-scheme:light]">
        <Viewport :document-id="documentId" class="h-full min-h-0 w-full overflow-hidden bg-slate-100">
          <Scroller :document-id="documentId">
            <template #default="{ page }">
              <div
                class="relative mx-auto my-3 bg-white shadow-sm"
                :style="{ width: `${page.width}px`, height: `${page.height}px` }"
              >
                <RenderLayer :document-id="documentId" :page-index="page.pageIndex" />
              </div>
            </template>
          </Scroller>
        </Viewport>
      </div>
    </div>
    <div
      v-else-if="isError"
      class="grid h-full min-h-64 place-items-center bg-muted/20 p-6 text-center text-sm text-destructive"
    >
      {{ slotState.error ?? '无法渲染此 PDF。' }}
    </div>
    <div
      v-else-if="isLoading"
      class="grid h-full min-h-64 place-items-center bg-muted/20 text-sm text-muted-foreground"
    >
      正在加载 PDF 预览
    </div>
  </DocumentContent>
</template>
