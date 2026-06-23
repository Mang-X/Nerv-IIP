<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * PageColumns — a masonry-style multi-column flow (CSS columns) for cards of
 * varying heights (Nuxt UI style). Children avoid breaking across columns.
 */
const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <div
    data-slot="page-columns"
    :class="
      cn('gap-4 sm:columns-2 lg:columns-3 [&>*]:mb-4 [&>*]:break-inside-avoid', props.class)
    "
  >
    <slot />
  </div>
</template>
