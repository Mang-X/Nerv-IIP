<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * PageAside — sticky, independently-scrolling sidebar for use inside a `Page`
 * left/right slot (navigation, table of contents).
 */
const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <div
    data-slot="page-aside"
    :class="
      cn('lg:sticky lg:top-20 lg:max-h-[calc(100dvh-6rem)] lg:overflow-y-auto lg:py-2', props.class)
    "
  >
    <slot />
  </div>
</template>
