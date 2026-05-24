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
import { PauseIcon, PlayIcon, RotateCcwIcon, UploadCloudIcon, XIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import { Badge } from '../badge'
import { Button } from '../button'
import { Progress } from '../progress'
import { getFileKind } from './fileKind'
import { uploadWithNativeFileStorageTransport } from './nativeTransport'
import { useFileUpload } from './useFileUpload'

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

function rowKind(row: FileUploadRow) {
  return getFileKind(row.fileName, row.contentType)
}

function isSlotOccupyingRow(row: FileUploadRow) {
  return row.status !== 'rejected' && row.status !== 'failed'
}

function formatFileSize(bytes: number) {
  if (bytes < 1024) {
    return `${bytes} B`
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`
  }

  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
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
        <div
          v-for="virtualRow in virtualRows"
          :key="virtualRow.data.id"
          data-slot="file-upload-row"
          :style="virtualRowStyle"
          class="border-border bg-card flex items-center gap-3 rounded-lg border p-3 transition-colors"
        >
          <div class="bg-muted flex size-9 shrink-0 items-center justify-center rounded-md">
            <component
              :is="rowKind(virtualRow.data).icon"
              class="text-muted-foreground"
              aria-hidden="true"
            />
          </div>

          <div class="min-w-0 flex-1">
            <div class="flex items-center gap-2">
              <span class="truncate text-sm font-medium">{{ virtualRow.data.fileName }}</span>
              <Badge variant="secondary">
                {{ virtualRow.data.status }}
              </Badge>
            </div>
            <div class="text-muted-foreground mt-1 text-xs">
              {{ rowKind(virtualRow.data).label }} - {{ formatFileSize(virtualRow.data.sizeBytes) }}
            </div>
            <Progress
              v-if="virtualRow.data.status === 'uploading' || virtualRow.data.status === 'paused' || virtualRow.data.status === 'completed'"
              :model-value="virtualRow.data.progress"
              class="mt-2"
            />
            <p v-if="virtualRow.data.error" class="text-destructive mt-2 text-xs">
              {{ virtualRow.data.error }}
            </p>
          </div>

          <Button
            v-if="virtualRow.data.status === 'uploading'"
            type="button"
            variant="ghost"
            size="icon-sm"
            :aria-label="`Pause ${virtualRow.data.fileName}`"
            @click="pauseRow(virtualRow.data.id)"
          >
            <PauseIcon />
          </Button>
          <Button
            v-else-if="virtualRow.data.status === 'paused'"
            type="button"
            variant="ghost"
            size="icon-sm"
            :aria-label="`Resume ${virtualRow.data.fileName}`"
            @click="resumeRow(virtualRow.data.id)"
          >
            <PlayIcon />
          </Button>
          <Button
            v-else-if="virtualRow.data.status === 'failed'"
            type="button"
            variant="ghost"
            size="icon-sm"
            :aria-label="`Retry ${virtualRow.data.fileName}`"
            @click="retryRow(virtualRow.data.id)"
          >
            <RotateCcwIcon />
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            :aria-label="`Remove ${virtualRow.data.fileName}`"
            @click="removeRow(virtualRow.data.id)"
          >
            <XIcon />
            <span class="sr-only">Remove {{ virtualRow.data.fileName }}</span>
          </Button>
        </div>
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
      <div
        v-for="row in rows"
        :key="row.id"
        data-slot="file-upload-row"
        class="border-border bg-card flex items-center gap-3 rounded-lg border p-3 transition-colors"
      >
        <div class="bg-muted flex size-9 shrink-0 items-center justify-center rounded-md">
          <component
            :is="rowKind(row).icon"
            class="text-muted-foreground"
            aria-hidden="true"
          />
        </div>

        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="truncate text-sm font-medium">{{ row.fileName }}</span>
            <Badge variant="secondary">
              {{ row.status }}
            </Badge>
          </div>
          <div class="text-muted-foreground mt-1 text-xs">
            {{ rowKind(row).label }} - {{ formatFileSize(row.sizeBytes) }}
          </div>
          <Progress
            v-if="row.status === 'uploading' || row.status === 'paused' || row.status === 'completed'"
            :model-value="row.progress"
            class="mt-2"
          />
          <p v-if="row.error" class="text-destructive mt-2 text-xs">
            {{ row.error }}
          </p>
        </div>

        <Button
          v-if="row.status === 'uploading'"
          type="button"
          variant="ghost"
          size="icon-sm"
          :aria-label="`Pause ${row.fileName}`"
          @click="pauseRow(row.id)"
        >
          <PauseIcon />
        </Button>
        <Button
          v-else-if="row.status === 'paused'"
          type="button"
          variant="ghost"
          size="icon-sm"
          :aria-label="`Resume ${row.fileName}`"
          @click="resumeRow(row.id)"
        >
          <PlayIcon />
        </Button>
        <Button
          v-else-if="row.status === 'failed'"
          type="button"
          variant="ghost"
          size="icon-sm"
          :aria-label="`Retry ${row.fileName}`"
          @click="retryRow(row.id)"
        >
          <RotateCcwIcon />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          :aria-label="`Remove ${row.fileName}`"
          @click="removeRow(row.id)"
        >
          <XIcon />
          <span class="sr-only">Remove {{ row.fileName }}</span>
        </Button>
      </div>
    </TransitionGroup>
  </div>
</template>
