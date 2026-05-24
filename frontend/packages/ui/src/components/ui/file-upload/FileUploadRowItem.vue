<script setup lang="ts">
import type { StyleValue } from 'vue'
import type { FileUploadRow } from './types'
import { computed } from 'vue'
import { PauseIcon, PlayIcon, RotateCcwIcon, XIcon } from 'lucide-vue-next'
import { Badge } from '../badge'
import { Button } from '../button'
import { Progress } from '../progress'
import { formatFileSize, rowKind } from './useFileUpload'

const props = defineProps<{
  row: FileUploadRow
  rowStyle?: StyleValue
}>()

const emits = defineEmits<{
  pause: [id: string]
  resume: [id: string]
  retry: [id: string]
  remove: [id: string]
}>()

const kind = computed(() => rowKind(props.row))
const showProgress = computed(() =>
  props.row.status === 'uploading'
  || props.row.status === 'paused'
  || props.row.status === 'completed',
)
</script>

<template>
  <div
    data-slot="file-upload-row"
    :style="rowStyle"
    class="border-border bg-card flex items-center gap-3 rounded-lg border p-3 transition-colors"
  >
    <div class="bg-muted flex size-9 shrink-0 items-center justify-center rounded-md">
      <component
        :is="kind.icon"
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
        {{ kind.label }} - {{ formatFileSize(row.sizeBytes) }}
      </div>
      <Progress
        v-if="showProgress"
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
      @click="emits('pause', row.id)"
    >
      <PauseIcon />
    </Button>
    <Button
      v-else-if="row.status === 'paused'"
      type="button"
      variant="ghost"
      size="icon-sm"
      :aria-label="`Resume ${row.fileName}`"
      @click="emits('resume', row.id)"
    >
      <PlayIcon />
    </Button>
    <Button
      v-else-if="row.status === 'failed'"
      type="button"
      variant="ghost"
      size="icon-sm"
      :aria-label="`Retry ${row.fileName}`"
      @click="emits('retry', row.id)"
    >
      <RotateCcwIcon />
    </Button>
    <Button
      type="button"
      variant="ghost"
      size="icon-sm"
      :aria-label="`Remove ${row.fileName}`"
      @click="emits('remove', row.id)"
    >
      <XIcon />
      <span class="sr-only">Remove {{ row.fileName }}</span>
    </Button>
  </div>
</template>
