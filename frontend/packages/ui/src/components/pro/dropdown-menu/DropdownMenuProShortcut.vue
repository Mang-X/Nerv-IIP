<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — dropdown menu shortcut hint. Trailing muted keycap text that brightens
 * when its parent item is focused.
 */
const props = defineProps<{
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <span
    data-slot="dropdown-menu-pro-shortcut"
    :class="
      cn(
        'text-muted-foreground group-focus/dropdown-menu-pro-item:text-accent-foreground ml-auto text-xs tracking-widest',
        props.class,
      )
    "
  >
    <slot />
  </span>
</template>
