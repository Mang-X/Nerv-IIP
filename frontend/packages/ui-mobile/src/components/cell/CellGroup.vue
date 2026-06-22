<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile CellGroup — groups Cells into a rounded card with hairline dividers and
 * an optional section title above. Use `inset` for card-style grouped lists.
 */
withDefaults(
  defineProps<{
    title?: string
    class?: HTMLAttributes['class']
  }>(),
  {},
)
</script>

<template>
  <section data-slot="cell-group" :class="cn('', $props.class)">
    <div v-if="title" class="px-4 pt-4 pb-1.5 text-sm font-medium text-muted-foreground">
      {{ title }}
    </div>
    <div class="overflow-hidden rounded-xl border border-border bg-card">
      <slot />
    </div>
  </section>
</template>
