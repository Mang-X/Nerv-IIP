<script setup lang="ts">
import { ExternalLinkIcon, FileQuestionIcon } from 'lucide-vue-next'

import { Button } from '../button'

const props = defineProps<{
  fileName: string
  src?: string
  reason?: 'empty' | 'unsupported'
}>()

const emit = defineEmits<{
  openSource: [src: string]
}>()
</script>

<template>
  <div
    data-slot="file-preview-unsupported"
    class="flex h-full min-h-64 flex-col items-center justify-center gap-4 bg-muted/20 p-6 text-center"
  >
    <div class="flex size-12 items-center justify-center rounded-lg border border-border bg-background shadow-xs">
      <FileQuestionIcon class="size-5 text-muted-foreground" aria-hidden="true" />
    </div>
    <div class="max-w-sm">
      <div class="text-sm font-semibold leading-5">暂不支持预览</div>
      <div class="mt-1 text-sm leading-5 text-muted-foreground">
        <template v-if="props.reason === 'empty'">
          {{ props.fileName }} 还没有可用的预览源。
        </template>
        <template v-else>
          {{ props.fileName }} 可从源文件打开，但当前格式暂不支持内嵌预览。
        </template>
      </div>
    </div>
    <Button
      v-if="src"
      variant="outline"
      size="sm"
      :aria-label="`Open ${fileName}`"
      @click="emit('openSource', src)"
    >
      <ExternalLinkIcon data-icon="inline-start" aria-hidden="true" />
      打开源文件
    </Button>
  </div>
</template>
