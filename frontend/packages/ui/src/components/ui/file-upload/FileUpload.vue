<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type {
  FileUploadCompletedFile,
  FileUploadCompleteSessionRequest,
  FileUploadCreateSessionRequest,
  FileUploadExpose,
  FileUploadRejectedFile,
  FileUploadRow,
  FileUploadSession,
  FileUploadTransport,
} from './types'
import { useVirtualList } from '@vueuse/core'
import { computed, shallowRef, useTemplateRef } from 'vue'
import { UploadCloudIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import FileUploadRowItem from './FileUploadRowItem.vue'
import { uploadWithNativeFileStorageTransport } from './nativeTransport'
import { isSlotOccupyingRow, useFileUpload } from './useFileUpload'

const props = withDefaults(defineProps<{
  purpose: string
  ownerService: string
  ownerType: string
  ownerId: string
  organizationId: string
  environmentId: string
  acceptedContentTypes?: string[]
  maxFileSizeBytes?: number
  maxFiles?: number
  autoUpload?: boolean
  virtualizeThreshold?: number
  virtualRowHeight?: number
  virtualListHeight?: number
  disabled?: boolean
  class?: HTMLAttributes['class']
  createUploadSession: (request: FileUploadCreateSessionRequest) => Promise<FileUploadSession>
  completeUploadSession: (
    uploadSessionId: string,
    request: FileUploadCompleteSessionRequest,
  ) => Promise<{ fileId: string }>
  transport?: FileUploadTransport
}>(), {
  acceptedContentTypes: () => [],
  maxFiles: 5,
  autoUpload: true,
  virtualizeThreshold: 40,
  virtualRowHeight: 92,
  virtualListHeight: 384,
  transport: uploadWithNativeFileStorageTransport,
})

const emits = defineEmits<{
  completed: [files: FileUploadCompletedFile[]]
  rejected: [files: FileUploadRejectedFile[]]
  failed: [row: FileUploadRow]
}>()

const fileInput = useTemplateRef<HTMLInputElement>('fileInput')
const isDragging = shallowRef(false)

const {
  rows,
  addFiles,
  uploadQueued,
  removeRow,
  pauseRow,
  resumeRow,
  retryRow,
  pauseAll,
  resumeAll,
  retryFailed,
  clear,
} = useFileUpload({
  purpose: props.purpose,
  ownerService: props.ownerService,
  ownerType: props.ownerType,
  ownerId: props.ownerId,
  organizationId: props.organizationId,
  environmentId: props.environmentId,
  acceptedContentTypes: props.acceptedContentTypes,
  maxFileSizeBytes: props.maxFileSizeBytes,
  maxFiles: props.maxFiles,
  autoUpload: props.autoUpload,
  createUploadSession: props.createUploadSession,
  completeUploadSession: props.completeUploadSession,
  transport: props.transport,
  onCompleted: files => emits('completed', files),
  onRejected: files => emits('rejected', files),
  onFailed: row => emits('failed', row),
})

const accept = computed(() => props.acceptedContentTypes.join(','))
const occupiedSlotCount = computed(() => rows.filter(isSlotOccupyingRow).length)
const availableSlotCount = computed(() => Math.max(props.maxFiles - occupiedSlotCount.value, 0))
const canAddMore = computed(() => !props.disabled && availableSlotCount.value > 0)
const shouldVirtualizeRows = computed(() => rows.length > props.virtualizeThreshold)
const virtualContainerStyle = computed(() => ({
  height: `${Math.min(rows.length * props.virtualRowHeight, props.virtualListHeight)}px`,
}))
const virtualRowStyle = computed(() => ({
  height: `${props.virtualRowHeight}px`,
}))

const {
  list: virtualRows,
  containerProps: virtualContainerProps,
  wrapperProps: virtualWrapperProps,
} = useVirtualList(computed(() => rows), {
  itemHeight: props.virtualRowHeight,
  overscan: 8,
})

function browse() {
  if (canAddMore.value) {
    fileInput.value?.click()
  }
}

async function handleFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  const files = Array.from(target.files ?? [])
  target.value = ''

  if (files.length > 0) {
    await addFiles(files)
  }
}

function handleDragEnter() {
  if (canAddMore.value) {
    isDragging.value = true
  }
}

function handleDragLeave(event: DragEvent) {
  const currentTarget = event.currentTarget as HTMLElement | null
  const relatedTarget = event.relatedTarget as Node | null

  if (!currentTarget || !relatedTarget || !currentTarget.contains(relatedTarget)) {
    isDragging.value = false
  }
}

async function handleDrop(event: DragEvent) {
  isDragging.value = false

  if (!canAddMore.value) {
    return
  }

  const files = Array.from(event.dataTransfer?.files ?? [])

  if (files.length > 0) {
    await addFiles(files)
  }
}

defineExpose<FileUploadExpose>({
  addFiles,
  uploadQueued,
  pauseAll,
  resumeAll,
  retryFailed,
  clear,
  browse,
})
</script>

<template>
  <div
    data-slot="file-upload"
    :class="cn('flex flex-col gap-3', props.class)"
  >
    <input
      ref="fileInput"
      type="file"
      class="sr-only"
      :accept="accept"
      :multiple="maxFiles > 1"
      :disabled="disabled"
      @change="handleFileChange"
    >

    <button
      data-slot="file-upload-dropzone"
      type="button"
      :disabled="!canAddMore"
      :data-dragging="isDragging ? 'true' : 'false'"
      class="border-border bg-background hover:bg-muted/60 focus-visible:border-ring focus-visible:ring-ring/50 data-[dragging=true]:border-primary data-[dragging=true]:bg-accent/60 flex min-h-28 flex-col items-center justify-center gap-2 rounded-lg border border-dashed px-4 py-5 text-center transition-all duration-200 focus-visible:ring-3 focus-visible:outline-none disabled:pointer-events-none disabled:opacity-50 data-[dragging=true]:scale-[1.01]"
      @click="browse"
      @dragenter.prevent="handleDragEnter"
      @dragover.prevent="handleDragEnter"
      @dragleave.prevent="handleDragLeave"
      @drop.prevent="handleDrop"
    >
      <UploadCloudIcon class="text-muted-foreground" aria-hidden="true" />
      <span class="text-sm font-medium">Choose files</span>
      <span class="text-muted-foreground text-xs">
        {{ availableSlotCount }} slots available
      </span>
    </button>

    <div
      v-if="shouldVirtualizeRows"
      data-slot="file-upload-virtual-list"
      v-bind="virtualContainerProps"
      :style="virtualContainerStyle"
      class="overflow-y-auto overscroll-contain pr-2"
    >
      <div v-bind="virtualWrapperProps" class="flex flex-col gap-2">
        <FileUploadRowItem
          v-for="virtualRow in virtualRows"
          :key="virtualRow.data.id"
          :row="virtualRow.data"
          :row-style="virtualRowStyle"
          @pause="pauseRow"
          @resume="resumeRow"
          @retry="retryRow"
          @remove="removeRow"
        />
      </div>
    </div>

    <TransitionGroup
      v-else-if="rows.length"
      tag="div"
      class="flex flex-col gap-2"
      enter-active-class="transition-all duration-200 ease-out"
      enter-from-class="translate-y-1 scale-[0.99] opacity-0"
      leave-active-class="transition-all duration-150 ease-in"
      leave-to-class="translate-y-1 scale-[0.99] opacity-0"
      move-class="transition-transform duration-200"
    >
      <FileUploadRowItem
        v-for="row in rows"
        :key="row.id"
        :row="row"
        @pause="pauseRow"
        @resume="resumeRow"
        @retry="retryRow"
        @remove="removeRow"
      />
    </TransitionGroup>
  </div>
</template>
