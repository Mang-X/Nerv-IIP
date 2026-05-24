<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type {
  FileUploadCompletedFile,
  FileUploadCompleteSessionRequest,
  FileUploadCreateSessionRequest,
  FileUploadRejectedFile,
  FileUploadRow,
  FileUploadSession,
  FileUploadTransport,
} from './types'
import { computed, useTemplateRef } from 'vue'
import { UploadCloudIcon, XIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import { Badge } from '../badge'
import { Button } from '../button'
import { Progress } from '../progress'
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
  transport: uploadWithNativeFileStorageTransport,
})

const emits = defineEmits<{
  completed: [files: FileUploadCompletedFile[]]
  rejected: [files: FileUploadRejectedFile[]]
  failed: [row: FileUploadRow]
}>()

const fileInput = useTemplateRef<HTMLInputElement>('fileInput')

const {
  rows,
  isUploading,
  addFiles,
  removeRow,
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
  createUploadSession: props.createUploadSession,
  completeUploadSession: props.completeUploadSession,
  transport: props.transport,
  onCompleted: files => emits('completed', files),
  onRejected: files => emits('rejected', files),
  onFailed: row => emits('failed', row),
})

const accept = computed(() => props.acceptedContentTypes.join(','))
const canAddMore = computed(() => !props.disabled && rows.length < props.maxFiles && !isUploading.value)

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

function formatFileSize(bytes: number) {
  if (bytes < 1024) {
    return `${bytes} B`
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`
  }

  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}
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
      type="button"
      :disabled="!canAddMore"
      class="border-border bg-background hover:bg-muted/60 focus-visible:border-ring focus-visible:ring-ring/50 flex min-h-28 flex-col items-center justify-center gap-2 rounded-lg border border-dashed px-4 py-5 text-center transition-colors focus-visible:ring-3 focus-visible:outline-none disabled:pointer-events-none disabled:opacity-50"
      @click="browse"
    >
      <UploadCloudIcon class="text-muted-foreground" aria-hidden="true" />
      <span class="text-sm font-medium">Choose files</span>
      <span class="text-muted-foreground text-xs">
        {{ maxFiles - rows.length }} slots available
      </span>
    </button>

    <div v-if="rows.length" class="flex flex-col gap-2">
      <div
        v-for="row in rows"
        :key="row.id"
        class="border-border bg-card flex items-center gap-3 rounded-lg border p-3"
      >
        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="truncate text-sm font-medium">{{ row.fileName }}</span>
            <Badge variant="secondary">
              {{ row.status }}
            </Badge>
          </div>
          <div class="text-muted-foreground mt-1 text-xs">
            {{ formatFileSize(row.sizeBytes) }}
          </div>
          <Progress
            v-if="row.status === 'uploading' || row.status === 'completed'"
            :model-value="row.progress"
            class="mt-2"
          />
          <p v-if="row.error" class="text-destructive mt-2 text-xs">
            {{ row.error }}
          </p>
        </div>
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          :disabled="row.status === 'uploading'"
          @click="removeRow(row.id)"
        >
          <XIcon />
          <span class="sr-only">Remove {{ row.fileName }}</span>
        </Button>
      </div>
    </div>
  </div>
</template>
