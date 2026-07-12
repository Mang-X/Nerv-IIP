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
    data-slot="nv-dropdown-menu-shortcut"
    :class="
      cn(
        'text-muted-foreground group-focus/nv-dropdown-menu-item:text-accent-foreground ml-auto text-xs tracking-widest',
        props.class,
      )
    "
  >
    <slot />
  </span>
</template>
