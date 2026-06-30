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
  FileUploadVariant,
} from './types'
import { useVirtualList } from '@vueuse/core'
import { computed, shallowRef, useTemplateRef } from 'vue'
import { ImageIcon, PlusIcon, Trash2Icon, UploadCloudIcon, UserIcon } from 'lucide-vue-next'
import { AnimatePresence, motion, MotionConfig } from 'motion-v'
import { cn } from '../../../lib/utils'
import { Button } from '../button'
import FileUploadRowItem from './FileUploadRowItem.vue'
import FileUploadRowPreview from './FileUploadRowPreview.vue'
import { fileUploadMotion } from './motion'
import { uploadWithNativeFileStorageTransport } from './nativeTransport'
import { formatFileSize, isSlotOccupyingRow, useFileUpload } from './useFileUpload'

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
  variant?: FileUploadVariant
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
  variant: 'default',
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
const MotionButton = motion.button
const MotionDiv = motion.div
const virtualRowGap = 8

const {
  rows,
  addFiles,
  uploadQueued,
  removeRow,
  pauseRow,
  resumeRow,
  retryRow,
  hasRows,
  hasQueuedRows,
  pauseAll,
  resumeAll,
  retryFailed,
  clear,
} = useFileUpload({
  purpose: () => props.purpose,
  ownerService: () => props.ownerService,
  ownerType: () => props.ownerType,
  ownerId: () => props.ownerId,
  organizationId: () => props.organizationId,
  environmentId: () => props.environmentId,
  acceptedContentTypes: () => props.acceptedContentTypes,
  maxFileSizeBytes: () => props.maxFileSizeBytes,
  maxFiles: () => props.maxFiles,
  autoUpload: () => props.autoUpload,
  createUploadSession: request => props.createUploadSession(request),
  completeUploadSession: (uploadSessionId, request) => props.completeUploadSession(uploadSessionId, request),
  transport: context => props.transport(context),
  onCompleted: files => emits('completed', files),
  onRejected: files => emits('rejected', files),
  onFailed: row => emits('failed', row),
})

const accept = computed(() => props.acceptedContentTypes.join(','))
const acceptedTypeHint = computed(() => formatAcceptedTypeHint(props.acceptedContentTypes))
const occupiedSlotCount = computed(() => rows.filter(isSlotOccupyingRow).length)
const availableSlotCount = computed(() => Math.max(props.maxFiles - occupiedSlotCount.value, 0))
const canAddMore = computed(() => !props.disabled && availableSlotCount.value > 0)
const totalSizeBytes = computed(() => rows.reduce((sum, row) => sum + row.sizeBytes, 0))
const totalSizeLabel = computed(() => formatFileSize(totalSizeBytes.value))
const maxSizeHint = computed(() => props.maxFileSizeBytes ? `单个文件不超过 ${formatFileSize(props.maxFileSizeBytes)}` : null)
const primaryRow = computed(() => rows.find(isSlotOccupyingRow) ?? rows[rows.length - 1] ?? null)
const isGridVariant = computed(() => props.variant === 'gallery' || props.variant === 'image')
const isDropzoneVariant = computed(() => props.variant !== 'default')
const canBrowse = computed(() => !props.disabled && (availableSlotCount.value > 0 || props.variant === 'avatar'))
const showQueueSummary = computed(() => rows.length > 0 && (props.variant === 'queue' || props.variant === 'gallery' || props.variant === 'table'))
const showCompactRows = computed(() => rows.length > 0 && props.variant === 'compact')
const renderRowsBeforeDropzone = computed(() => props.variant === 'image' && rows.length > 0)
const renderRowsAfterDropzone = computed(() => rows.length > 0 && props.variant !== 'avatar' && props.variant !== 'image' && props.variant !== 'compact')
const shouldVirtualizeRows = computed(() =>
  rows.length > props.virtualizeThreshold
  && !isGridVariant.value
  && props.variant !== 'avatar',
)
const virtualItemHeight = computed(() => props.virtualRowHeight + virtualRowGap)
const virtualContainerStyle = computed(() => ({
  height: `${Math.min(rows.length * virtualItemHeight.value, props.virtualListHeight)}px`,
}))
const virtualRowStyle = computed(() => ({
  height: `${props.virtualRowHeight}px`,
}))
const virtualMotionRowStyle = computed(() => ({
  height: `${virtualItemHeight.value}px`,
  paddingBottom: `${virtualRowGap}px`,
}))
const dropzoneAnimate = computed(() => isDragging.value
  ? { y: -2, scale: 1.01 }
  : { y: 0, scale: 1 })
const dropzoneHover = computed(() => canBrowse.value
  ? { y: -1, scale: 1.004 }
  : undefined)
const dropzoneTap = computed(() => canBrowse.value
  ? { y: 0, scale: 0.996 }
  : undefined)
const rootClass = computed(() => cn(
  'flex flex-col gap-3',
  props.variant === 'avatar' && 'items-center',
  props.class,
))
const dropzoneClass = computed(() => cn(
  'border-border bg-background hover:bg-muted/60 focus-visible:border-ring focus-visible:ring-ring/50 data-[dragging=true]:border-primary data-[dragging=true]:bg-primary/5 data-[dragging=true]:ring-primary/20 flex items-center justify-center border border-dashed text-center ring-0 will-change-transform focus-visible:ring-3 focus-visible:outline-none data-[dragging=true]:ring-2 disabled:pointer-events-none disabled:opacity-50',
  props.variant === 'queue' && 'min-h-28 flex-col gap-2 py-5',
  props.variant === 'compact' && 'mx-auto min-h-0 w-full max-w-xl flex-row justify-start gap-3 rounded-lg px-4 py-4 shadow-none',
  props.variant === 'table' && 'min-h-36 flex-col gap-3 rounded-lg px-6 py-7 shadow-none',
  props.variant === 'avatar' && 'size-28 rounded-full p-0 shadow-none',
  props.variant === 'gallery' && 'min-h-64 flex-col gap-3 rounded-lg px-6 py-8 shadow-none',
  props.variant === 'image' && 'min-h-36 flex-col gap-3 rounded-lg border-solid px-6 py-6 shadow-none',
))
const rowsContainerSlot = computed(() => {
  if (isGridVariant.value) {
    return 'file-upload-grid'
  }

  if (props.variant === 'table') {
    return 'file-upload-table'
  }

  return 'file-upload-list'
})
const rowsContainerClass = computed(() => cn(
  props.variant === 'gallery' && 'grid grid-cols-2 gap-3 md:grid-cols-3',
  props.variant === 'image' && 'grid grid-cols-2 gap-2 sm:grid-cols-4',
  props.variant === 'table' && 'border-border bg-card overflow-hidden rounded-lg border',
  props.variant !== 'gallery' && props.variant !== 'image' && props.variant !== 'table' && 'flex flex-col gap-2',
))
const dropzoneTitle = computed(() => {
  switch (props.variant) {
    case 'avatar':
      return '上传头像'
    case 'gallery':
      return '上传图片到画廊'
    case 'image':
      return '选择文件或拖拽到这里'
    case 'compact':
      return '拖拽或点击添加文件'
    case 'table':
      return '拖拽文件到这里，或点击浏览'
    case 'queue':
      return '拖拽文件到这里，或点击浏览'
    case 'default':
      return '选择文件'
  }
})
const dropzoneDescription = computed(() => {
  switch (props.variant) {
    case 'compact':
      return [
        acceptedTypeHint.value ? `支持 ${acceptedTypeHint.value}` : null,
        maxSizeHint.value,
        `最多 ${props.maxFiles} 个文件`,
      ].filter(Boolean).join(' · ')
    case 'gallery':
      return '拖拽图片到这里或点击浏览'
    case 'image':
      return null
    case 'table':
      return null
    case 'avatar':
    case 'queue':
    case 'default':
      return null
  }
})
const dropzoneCta = computed(() => {
  switch (props.variant) {
    case 'compact':
      return '添加文件'
    case 'gallery':
      return '选择图片'
    case 'image':
      return '浏览文件'
    case 'table':
      return null
    case 'avatar':
    case 'queue':
    case 'default':
      return null
  }
})
const dropzoneIcon = computed(() => {
  switch (props.variant) {
    case 'avatar':
      return UserIcon
    case 'gallery':
      return ImageIcon
    case 'compact':
    case 'queue':
    case 'default':
    case 'image':
    case 'table':
      return UploadCloudIcon
  }
})
const restrictionHint = computed(() => {
  const hints = [
    acceptedTypeHint.value ? `支持 ${acceptedTypeHint.value}` : null,
    maxSizeHint.value,
    `最多 ${props.maxFiles} 个文件`,
  ].filter(Boolean)

  return hints.join(' · ')
})
const tableRestrictionHint = computed(() => {
  const maxSize = props.maxFileSizeBytes ? `最大文件大小：${formatFileSize(props.maxFileSizeBytes)}` : null
  return [
    acceptedTypeHint.value ? `支持 ${acceptedTypeHint.value}` : null,
    maxSize,
    `最多文件数：${props.maxFiles}`,
  ].filter(Boolean).join(' · ')
})
const avatarHint = computed(() => {
  if (!primaryRow.value) {
    return restrictionHint.value
  }

  switch (primaryRow.value.status) {
    case 'completed':
      return '头像已上传，可点击更换'
    case 'uploading':
      return '头像上传中'
    case 'paused':
      return '头像上传已暂停'
    case 'failed':
      return '上传失败，请重新选择'
    case 'rejected':
      return primaryRow.value.error ?? '文件不符合要求'
    case 'queued':
      return '头像待上传，可点击更换'
  }
})

const acceptedTypeExtensions: Record<string, string[]> = {
  'application/pdf': ['.pdf'],
  'application/msword': ['.doc'],
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document': ['.docx'],
  'application/vnd.ms-excel': ['.xls'],
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': ['.xlsx'],
  'application/vnd.ms-powerpoint': ['.ppt'],
  'application/vnd.openxmlformats-officedocument.presentationml.presentation': ['.pptx'],
  'application/zip': ['.zip'],
  'application/x-zip-compressed': ['.zip'],
  'application/json': ['.json'],
  'application/xml': ['.xml'],
  'text/plain': ['.txt'],
  'text/csv': ['.csv'],
  'image/png': ['.png'],
  'image/jpeg': ['.jpg', '.jpeg'],
  'image/gif': ['.gif'],
  'image/webp': ['.webp'],
  'image/svg+xml': ['.svg'],
  'audio/mpeg': ['.mp3'],
  'video/mp4': ['.mp4'],
}

const acceptedTypeFamilies: Record<string, string[]> = {
  'image/*': ['.png', '.jpg', '.jpeg', '.svg'],
  'audio/*': ['.mp3', '.wav'],
  'video/*': ['.mp4', '.mov'],
  'text/*': ['.txt', '.csv'],
}

function formatAcceptedTypeHint(acceptedTypes: string[]) {
  const labels = acceptedTypes.flatMap((acceptedType) => {
    const normalized = acceptedType.trim().toLowerCase()

    if (!normalized) {
      return []
    }

    if (normalized.startsWith('.')) {
      return [normalized]
    }

    return acceptedTypeExtensions[normalized]
      ?? acceptedTypeFamilies[normalized]
      ?? [normalized]
  })

  return Array.from(new Set(labels)).join('、')
}

const {
  list: virtualRows,
  containerProps: virtualContainerProps,
  wrapperProps: virtualWrapperProps,
} = useVirtualList(computed(() => rows), {
  itemHeight: virtualItemHeight.value,
  overscan: 8,
})

function browse() {
  if (canBrowse.value) {
    fileInput.value?.click()
  }
}

async function handleFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  const files = Array.from(target.files ?? [])
  target.value = ''

  if (files.length > 0) {
    if (props.variant === 'avatar') {
      clear()
    }

    await addFiles(files)
  }
}

function handleDragEnter() {
  if (canBrowse.value) {
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

  if (!canBrowse.value) {
    return
  }

  const files = Array.from(event.dataTransfer?.files ?? [])

  if (files.length > 0) {
    if (props.variant === 'avatar') {
      clear()
    }

    await addFiles(files)
  }
}

defineExpose<FileUploadExpose>({
  get hasRows() {
    return hasRows.value
  },
  get hasQueuedRows() {
    return hasQueuedRows.value
  },
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
    :data-variant="variant"
    :class="rootClass"
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

    <MotionConfig reduced-motion="user">
      <div
        v-if="renderRowsBeforeDropzone"
        :data-slot="rowsContainerSlot"
        :class="rowsContainerClass"
      >
        <AnimatePresence>
          <MotionDiv
            v-for="row in rows"
            :key="row.id"
            data-motion-row="true"
            :layout="true"
            :initial="{ opacity: 0, y: 10, scale: 0.985, filter: 'blur(3px)' }"
            :animate="{ opacity: 1, y: 0, scale: 1, filter: 'blur(0px)' }"
            :exit="{ opacity: 0, y: -6, scale: 0.99, filter: 'blur(2px)' }"
            :transition="fileUploadMotion.pointToPointShort"
          >
            <FileUploadRowItem
              :row="row"
              :variant="variant"
              @pause="pauseRow"
              @resume="resumeRow"
              @retry="retryRow"
              @remove="removeRow"
            />
          </MotionDiv>
        </AnimatePresence>
      </div>

      <div
        v-if="variant === 'default'"
        data-slot="file-upload-default"
        class="flex flex-col items-start gap-2"
      >
        <Button
          type="button"
          :disabled="!canBrowse"
          data-file-upload-button="true"
          @click="browse"
        >
          <UploadCloudIcon data-icon="inline-start" />
          {{ dropzoneTitle }}
        </Button>
        <span
          v-if="restrictionHint"
          data-slot="file-upload-accept-hint"
          class="text-muted-foreground max-w-full text-wrap text-xs leading-5"
        >
          {{ restrictionHint }}
        </span>
      </div>

      <MotionButton
        v-else-if="isDropzoneVariant"
        data-slot="file-upload-dropzone"
        type="button"
        :disabled="!canBrowse"
        :data-dragging="isDragging ? 'true' : 'false'"
        :initial="false"
        :animate="dropzoneAnimate"
        :while-hover="dropzoneHover"
        :while-tap="dropzoneTap"
        :transition="fileUploadMotion.fastInvoke"
        :class="dropzoneClass"
        @click="browse"
        @dragenter.prevent="handleDragEnter"
        @dragover.prevent="handleDragEnter"
        @dragleave.prevent="handleDragLeave"
        @drop.prevent="handleDrop"
      >
        <template v-if="variant === 'avatar'">
          <FileUploadRowPreview
            v-if="primaryRow"
            :row="primaryRow"
            class="size-full rounded-full"
          />
          <div
            v-else
            class="border-border text-muted-foreground flex size-full items-center justify-center rounded-full border border-dashed"
          >
            <component :is="dropzoneIcon" class="size-5" aria-hidden="true" />
          </div>
        </template>

        <template v-else-if="variant === 'compact'">
          <Button
            as="span"
            size="sm"
            class="pointer-events-none shrink-0"
            data-file-upload-cta="true"
          >
            <PlusIcon class="size-4" aria-hidden="true" />
            {{ dropzoneCta }}
          </Button>
          <span class="text-muted-foreground min-w-0 text-left text-sm">
            {{ dropzoneDescription }}
          </span>
        </template>

        <template v-else>
          <span class="bg-muted text-muted-foreground inline-flex size-14 items-center justify-center rounded-full">
            <component :is="dropzoneIcon" class="size-5" aria-hidden="true" />
          </span>
          <span class="text-sm font-medium">{{ dropzoneTitle }}</span>
          <span
            v-if="dropzoneDescription"
            class="text-muted-foreground text-sm"
          >
            {{ dropzoneDescription }}
          </span>
        </template>

        <span
          v-if="variant !== 'compact' && variant !== 'avatar' && restrictionHint"
          data-slot="file-upload-accept-hint"
          class="text-muted-foreground max-w-full text-wrap text-xs leading-5"
        >
          {{ variant === 'table' ? tableRestrictionHint : restrictionHint }}
        </span>
        <Button
          v-if="dropzoneCta && variant !== 'compact'"
          as="span"
          size="sm"
          class="pointer-events-none"
          data-file-upload-cta="true"
        >
          <UploadCloudIcon class="size-4" aria-hidden="true" />
          {{ dropzoneCta }}
        </Button>
        <span v-if="variant === 'queue'" class="text-muted-foreground text-xs">
          还可上传 {{ availableSlotCount }} 个文件
        </span>
      </MotionButton>

      <div
        v-if="variant === 'avatar'"
        class="text-center"
      >
        <div class="text-sm font-medium">
          {{ primaryRow ? '更换头像' : dropzoneTitle }}
        </div>
        <div class="text-muted-foreground text-xs">
          {{ avatarHint }}
        </div>
        <Button
          v-if="primaryRow"
          type="button"
          variant="ghost"
          size="sm"
          class="mt-2"
          @click.stop="removeRow(primaryRow.id)"
        >
          <Trash2Icon data-icon="inline-start" />
          移除头像
        </Button>
      </div>

      <div
        v-if="showCompactRows"
        data-slot="file-upload-compact-list"
        class="mx-auto flex w-full max-w-xl flex-col gap-1.5"
      >
        <AnimatePresence>
          <MotionDiv
            v-for="row in rows"
            :key="row.id"
            data-motion-row="true"
            :layout="true"
            :initial="{ opacity: 0, y: 6, scale: 0.99, filter: 'blur(2px)' }"
            :animate="{ opacity: 1, y: 0, scale: 1, filter: 'blur(0px)' }"
            :exit="{ opacity: 0, y: -4, scale: 0.99, filter: 'blur(1px)' }"
            :transition="fileUploadMotion.pointToPointShort"
          >
            <FileUploadRowItem
              :row="row"
              variant="compact"
              @pause="pauseRow"
              @resume="resumeRow"
              @retry="retryRow"
              @remove="removeRow"
            />
          </MotionDiv>
        </AnimatePresence>
      </div>

      <div
        v-if="showQueueSummary"
        data-slot="file-upload-summary"
        class="flex flex-wrap items-center justify-between gap-2"
      >
        <div class="min-w-0">
          <div class="text-sm font-medium">
            <template v-if="variant === 'table'">
              文件 ({{ occupiedSlotCount }})
            </template>
            <template v-else>
              {{ variant === 'gallery' ? '画廊' : '文件' }} ({{ occupiedSlotCount }}/{{ maxFiles }})
            </template>
          </div>
          <div v-if="variant !== 'table'" class="text-muted-foreground text-xs">
            总计 {{ totalSizeLabel }}
          </div>
        </div>
        <div class="flex items-center gap-1.5">
          <Button
            v-if="variant !== 'gallery'"
            type="button"
            variant="outline"
            size="sm"
            :disabled="!canAddMore"
            data-slot="file-upload-add-button"
            @click="browse"
          >
            <PlusIcon data-icon="inline-start" />
            添加文件
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            :disabled="!hasRows"
            data-slot="file-upload-clear-button"
            @click="clear"
          >
            <Trash2Icon data-icon="inline-start" />
            {{ variant === 'table' ? '移除全部' : '清空' }}
          </Button>
        </div>
      </div>

      <div
        v-if="shouldVirtualizeRows"
        data-slot="file-upload-virtual-list"
        v-bind="virtualContainerProps"
        :style="virtualContainerStyle"
        class="overflow-y-auto overscroll-contain pr-2"
      >
        <div v-bind="virtualWrapperProps" class="flex flex-col">
          <MotionDiv
            v-for="virtualRow in virtualRows"
            :key="virtualRow.data.id"
            data-motion-row="true"
            :style="virtualMotionRowStyle"
            :layout="true"
            :initial="{ opacity: 0, y: 12, scale: 0.985, filter: 'blur(3px)' }"
            :animate="{ opacity: 1, y: 0, scale: 1, filter: 'blur(0px)' }"
            :exit="{ opacity: 0, y: -6, scale: 0.99, filter: 'blur(2px)' }"
            :transition="fileUploadMotion.pointToPointShort"
          >
            <FileUploadRowItem
              :row="virtualRow.data"
              :variant="variant"
              :row-style="virtualRowStyle"
              @pause="pauseRow"
              @resume="resumeRow"
              @retry="retryRow"
              @remove="removeRow"
            />
          </MotionDiv>
        </div>
      </div>

      <div
        v-else-if="renderRowsAfterDropzone"
        :data-slot="rowsContainerSlot"
        :class="rowsContainerClass"
      >
        <div
          v-if="variant === 'table'"
          data-slot="file-upload-table-header"
          class="bg-muted/40 text-muted-foreground grid grid-cols-[minmax(0,1fr)_7rem_7rem_5rem] gap-3 border-b px-4 py-2.5 text-xs font-medium"
        >
          <span>名称</span>
          <span>类型</span>
          <span>大小</span>
          <span class="text-right">操作</span>
        </div>
        <AnimatePresence>
          <MotionDiv
            v-for="row in rows"
            :key="row.id"
            data-motion-row="true"
            :layout="true"
            :initial="{ opacity: 0, y: 12, scale: 0.985, filter: 'blur(3px)' }"
            :animate="{ opacity: 1, y: 0, scale: 1, filter: 'blur(0px)' }"
            :exit="{ opacity: 0, y: -6, scale: 0.99, filter: 'blur(2px)' }"
            :transition="fileUploadMotion.pointToPointShort"
          >
            <FileUploadRowItem
              :row="row"
              :variant="variant"
              @pause="pauseRow"
              @resume="resumeRow"
              @retry="retryRow"
              @remove="removeRow"
            />
          </MotionDiv>
        </AnimatePresence>
      </div>
    </MotionConfig>
  </div>
</template>
