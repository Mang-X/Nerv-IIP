<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../lib/utils'

/**
 * PageGrid — responsive grid of equal cards (Nuxt UI style). `cols` sets the
 * large-screen column count; it always collapses to 1 col on mobile, 2 on sm.
 */
const props = withDefaults(
  defineProps<{ cols?: 2 | 3 | 4; class?: HTMLAttributes['class'] }>(),
  { cols: 3 },
)

const colsClass = computed(
  () =>
    ({
      2: 'sm:grid-cols-2',
      3: 'sm:grid-cols-2 lg:grid-cols-3',
      4: 'sm:grid-cols-2 lg:grid-cols-4',
    })[props.cols],
)
</script>

<template>
  <div data-slot="page-grid" :class="cn('grid grid-cols-1 gap-4', colsClass, props.class)">
    <slot />
  </div>
</template>
