<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * DashboardPanel — the main content region of a dashboard shell (Nuxt UI Pro
 * style): a vertical flex column whose `#header` (DashboardNavbar / Toolbar)
 * stays fixed while the default slot scrolls. Multiple panels can sit side by
 * side inside a `DashboardGroup`.
 */
const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <section
    data-slot="dashboard-panel"
    :class="cn('flex min-w-0 flex-1 flex-col bg-background', props.class)"
  >
    <div v-if="$slots.header" class="shrink-0">
      <slot name="header" />
    </div>
    <div class="min-h-0 flex-1 overflow-y-auto">
      <slot />
    </div>
  </section>
</template>
