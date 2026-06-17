<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * DashboardGroup — root flex container for a dashboard shell (Nuxt UI Pro
 * style): a full-height row that holds a `DashboardSidebar` plus one or more
 * `DashboardPanel`s side by side.
 */
const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <div data-slot="dashboard-group" :class="cn('flex min-h-dvh w-full overflow-hidden', props.class)">
    <slot />
  </div>
</template>
