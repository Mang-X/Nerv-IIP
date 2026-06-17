<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * DashboardNavbar — top bar of a dashboard panel (Nuxt UI Pro style):
 * `#leading` (sidebar toggle / icon), a title, and `#right` actions. Sticky,
 * with a subtle glass background.
 */
const props = defineProps<{ title?: string; class?: HTMLAttributes['class'] }>()
</script>

<template>
  <div
    data-slot="dashboard-navbar"
    :class="
      cn(
        'sticky top-0 z-10 flex h-14 shrink-0 items-center gap-3 border-b border-border bg-background/80 px-4 backdrop-blur-md',
        props.class,
      )
    "
  >
    <slot name="leading" />
    <div class="flex min-w-0 items-center gap-2">
      <slot name="title">
        <h1 v-if="title" class="truncate text-sm font-semibold text-foreground">{{ title }}</h1>
      </slot>
    </div>
    <div v-if="$slots.right" class="ms-auto flex items-center gap-1.5">
      <slot name="right" />
    </div>
  </div>
</template>
