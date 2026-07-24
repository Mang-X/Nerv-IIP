<script setup lang="ts">
import { cn } from '../../../../lib/utils'
import { metricToneTint, type NvMetricFacet } from '../metric'

/**
 * Internal — the `facets` bottom-zone: clickable dimension chips that break the
 * headline down (原料 / 半成品 …). Each is a real button with the full
 * default/hover/focus-visible/active set; clicking emits `facet` for the parent
 * to route. Not a public component.
 */
withDefaults(defineProps<{ facets?: NvMetricFacet[] }>(), { facets: () => [] })
const emit = defineEmits<{ (e: 'facet', facet: NvMetricFacet): void }>()
</script>

<template>
  <div class="mt-4 flex flex-wrap gap-1.5">
    <button
      v-for="(f, i) in facets"
      :key="f.key ?? i"
      type="button"
      :class="
        cn(
          'nv-metric-facet inline-flex items-baseline gap-1.5 rounded-md px-2 py-1 text-xs',
          f.tone && f.tone !== 'neutral'
            ? metricToneTint[f.tone]
            : 'bg-muted text-muted-foreground',
        )
      "
      @click="emit('facet', f)"
    >
      {{ f.label }}
      <b
        class="font-semibold tabular-nums"
        :class="f.tone && f.tone !== 'neutral' ? '' : 'text-foreground'"
        >{{ f.value }}</b
      >
    </button>
  </div>
</template>

<style scoped>
@layer nv-components {
  /* facet chips are buttons — every one needs the full default/hover/
     focus-visible/active set (pc/product.md keyboard-reachability gate), toned
     ones included. Hover/active use an inset box-shadow OVERLAY (not
     background-color): the base fill is a Tailwind `bg-*` utility, whose layer
     outranks nv-components, so a background rule here would be ignored — a
     box-shadow tint (no utility touches it) wins and follows the radius.
     `currentColor` carries the chip's own tone, so the overlay reads correctly
     on neutral and on danger/warning alike. */
  .nv-metric-facet {
    transition: box-shadow var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  .nv-metric-facet:hover {
    box-shadow: inset 0 0 0 100px color-mix(in oklch, currentColor 10%, transparent);
  }
  .nv-metric-facet:active {
    box-shadow: inset 0 0 0 100px color-mix(in oklch, currentColor 18%, transparent);
  }
  .nv-metric-facet:focus-visible {
    outline: 2px solid var(--nv-brand);
    outline-offset: 2px;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-metric-facet {
      transition: none;
    }
  }
}
</style>
