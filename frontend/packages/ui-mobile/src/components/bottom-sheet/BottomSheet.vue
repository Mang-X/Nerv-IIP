<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import {
  DialogRoot,
  DialogPortal,
  DialogOverlay,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from 'reka-ui'
import { cn } from '../../lib/utils'

defineProps<{
  open: boolean
  title?: string
  description?: string
  class?: HTMLAttributes['class']
}>()
const emit = defineEmits<{ 'update:open': [value: boolean] }>()
</script>

<template>
  <DialogRoot :open="open" @update:open="emit('update:open', $event)">
    <DialogPortal>
      <DialogOverlay class="fixed inset-0 z-40 bg-black/50" />
      <DialogContent
        :class="cn(
          'fixed inset-x-0 bottom-0 z-50 flex max-h-[85dvh] flex-col rounded-t-2xl border-t border-border bg-card pb-safe',
          $props.class,
        )"
      >
        <div class="mx-auto mt-2 h-1.5 w-10 shrink-0 rounded-full bg-muted" aria-hidden="true" />
        <DialogTitle v-if="title" class="px-4 py-3 text-base font-semibold text-foreground">
          {{ title }}
        </DialogTitle>
        <!-- 始终渲染描述以消除 reka-ui 缺失描述的控制台告警；无可见描述时用 sr-only 承载标题/占位。 -->
        <DialogDescription :class="description ? 'px-4 pb-2 text-sm text-muted-foreground' : 'sr-only'">
          {{ description ?? title ?? '抽屉内容' }}
        </DialogDescription>
        <div class="min-h-0 flex-1 overflow-y-auto px-4 pb-4">
          <slot />
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>
