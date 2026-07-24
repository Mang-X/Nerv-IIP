<script setup lang="ts">
import { ChevronRightIcon } from '@lucide/vue'
import type { NvMetricAction } from '../metric'

/**
 * Internal — the `alert` bottom-zone footer: a short note plus a call-to-action
 * that routes to the already-filtered list. Renders a link when `action.href`
 * is set, else a button that emits `action`. The card-level tone tint lives on
 * the shell in NvMetricCard. Not a public component.
 */
defineProps<{
  footStart?: string
  action?: NvMetricAction
}>()
const emit = defineEmits<{ (e: 'action'): void }>()
</script>

<template>
  <div
    v-if="footStart || action"
    class="mt-3 flex items-center justify-between gap-3 border-t border-border/60 pt-2.5 text-xs text-muted-foreground"
  >
    <span class="line-clamp-2 min-w-0">{{ footStart }}</span>
    <a
      v-if="action?.href"
      :href="action.href"
      class="nv-metric-action inline-flex shrink-0 items-center gap-0.5 font-semibold text-brand-strong"
    >
      {{ action.label }}<ChevronRightIcon class="size-3.5" aria-hidden="true" />
    </a>
    <button
      v-else-if="action"
      type="button"
      class="nv-metric-action inline-flex shrink-0 items-center gap-0.5 font-semibold text-brand-strong"
      @click="emit('action')"
    >
      {{ action.label }}<ChevronRightIcon class="size-3.5" aria-hidden="true" />
    </button>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-metric-action:hover {
    text-decoration: underline;
    text-underline-offset: 3px;
  }
  .nv-metric-action:focus-visible {
    outline: 2px solid var(--nv-brand);
    outline-offset: 2px;
    border-radius: 4px;
  }
}
</style>
