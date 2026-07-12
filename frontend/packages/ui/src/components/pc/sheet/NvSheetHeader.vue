<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <!-- pr-12 clears the absolute close button (top-4 right-4, size-7) so the title
       and description never run under it — mirrors NvDialogHeader's pr reservation. -->
  <div data-slot="nv-sheet-header" :class="cn('gap-0.5 p-4 pr-12 flex flex-col', props.class)">
    <slot />
  </div>
</template>
