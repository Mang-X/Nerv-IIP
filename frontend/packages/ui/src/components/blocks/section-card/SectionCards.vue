<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

withDefaults(
  defineProps<{
    /** Max columns on the widest breakpoint (2–4). */
    columns?: 2 | 3 | 4
    class?: HTMLAttributes['class']
  }>(),
  { columns: 4 },
)

const colClass: Record<number, string> = {
  2: 'sm:grid-cols-2',
  3: 'sm:grid-cols-2 lg:grid-cols-3',
  4: 'sm:grid-cols-2 lg:grid-cols-4',
}
</script>

<template>
  <div :class="cn('grid grid-cols-1 gap-4', colClass[columns], $props.class)">
    <slot />
  </div>
</template>
