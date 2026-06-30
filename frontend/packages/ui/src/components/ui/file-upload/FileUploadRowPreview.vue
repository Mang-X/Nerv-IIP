<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { FileUploadRow } from './types'
import { computed, onBeforeUnmount, shallowRef, watch } from 'vue'
import { cn } from '../../../lib/utils'
import { rowKind } from './useFileUpload'

const props = defineProps<{
  row: FileUploadRow
  class?: HTMLAttributes['class']
}>()

const previewUrl = shallowRef<string | null>(null)
const kind = computed(() => rowKind(props.row))
const isImage = computed(() =>
  props.row.contentType.startsWith('image/')
  || /\.(apng|avif|gif|jpe?g|png|webp)$/i.test(props.row.fileName),
)

watch(
  () => [props.row.file, isImage.value] as const,
  () => {
    revokePreviewUrl()

    if (isImage.value && typeof URL.createObjectURL === 'function') {
      previewUrl.value = URL.createObjectURL(props.row.file)
    }
  },
  { immediate: true },
)

onBeforeUnmount(revokePreviewUrl)

function revokePreviewUrl() {
  if (previewUrl.value && typeof URL.revokeObjectURL === 'function') {
    URL.revokeObjectURL(previewUrl.value)
  }

  previewUrl.value = null
}
</script>

<template>
  <div
    data-slot="file-upload-preview"
    :data-image="previewUrl ? 'true' : 'false'"
    :class="cn('bg-muted text-muted-foreground flex shrink-0 items-center justify-center overflow-hidden rounded-md', props.class)"
  >
    <img
      v-if="previewUrl"
      data-slot="file-upload-thumbnail"
      :src="previewUrl"
      :alt="row.fileName"
      class="size-full object-cover"
      draggable="false"
    >
    <component
      :is="kind.icon"
      v-else
      class="size-4"
      aria-hidden="true"
    />
  </div>
</template>
