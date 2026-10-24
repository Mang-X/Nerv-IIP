<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { DialogClose } from 'reka-ui'
import { cn } from '../../../lib/utils'
import { NvButton } from '../button'
import { inject } from 'vue'
import { dialogScrollableKey } from './layout'

/**
 * scrollable 模式下独立于正文滚动，共用弹窗表面，以分隔线标出操作区。
 * 旧组合保留原有 sticky 行为；show-close-button 继续提供原生 DialogClose。
 */
const props = withDefaults(
  defineProps<{
    class?: HTMLAttributes['class']
    showCloseButton?: boolean
  }>(),
  {
    showCloseButton: false,
  },
)
const scrollable = inject(dialogScrollableKey, undefined)
</script>

<template>
  <div
    data-slot="nv-dialog-footer"
    :class="
      cn(
        'flex shrink-0 flex-col-reverse gap-2 sm:flex-row sm:justify-end',
        scrollable
          ? 'border-t border-border bg-transparent px-6 py-4'
          : 'sticky bottom-0 z-10 rounded-b-xl bg-card py-3',
        props.class,
      )
    "
  >
    <slot />
    <DialogClose v-if="showCloseButton" as-child>
      <NvButton variant="outline">关闭</NvButton>
    </DialogClose>
  </div>
</template>
