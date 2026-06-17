<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * DashboardSidebar — fixed-width left rail for a dashboard shell (Nuxt UI Pro
 * style): optional `#header` (brand / switcher) and `#footer` (user / actions)
 * frame a scrollable navigation body.
 */
const props = defineProps<{ class?: HTMLAttributes['class'] }>()
</script>

<template>
  <aside
    data-slot="dashboard-sidebar"
    :class="cn('flex w-64 shrink-0 flex-col border-r border-border bg-card', props.class)"
  >
    <div v-if="$slots.header" class="flex h-14 shrink-0 items-center gap-2 border-b border-border px-3">
      <slot name="header" />
    </div>
    <div class="min-h-0 flex-1 overflow-y-auto p-3">
      <slot />
    </div>
    <div v-if="$slots.footer" class="shrink-0 border-t border-border p-3">
      <slot name="footer" />
    </div>
  </aside>
</template>
