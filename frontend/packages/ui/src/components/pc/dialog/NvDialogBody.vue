<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { ScrollAreaRoot, ScrollAreaViewport } from 'reka-ui'
import { cn } from '../../../lib/utils'
import { ScrollBar } from '../../ui/scroll-area'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <ScrollAreaRoot
    data-slot="nv-dialog-body"
    type="hover"
    :class="cn('relative flex min-h-0 flex-1 flex-col overflow-hidden', props.class)"
  >
    <ScrollAreaViewport
      data-slot="nv-dialog-body-viewport"
      aria-label="对话框正文"
      class="min-h-0 w-full flex-1 overscroll-contain px-6 py-4 scroll-py-4 outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring/50"
    >
      <slot />
    </ScrollAreaViewport>
    <ScrollBar class="[&_[data-slot=scroll-area-thumb]]:bg-muted-foreground/35" />
  </ScrollAreaRoot>
</template>
