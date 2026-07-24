<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { cn } from '../../../../lib/utils'
import { metricItemKey, metricToneFill, type NvMetricSegment } from '../metric'
import { useMetricTooltip } from '../useMetricTooltip'
import NvMetricTipPart from './NvMetricTipPart.vue'

/**
 * Internal — the `breakdown` bottom-zone: a segmented bar + counted legend that
 * splits the headline total by status. Hovering a segment or its legend row dims
 * the rest (linked highlight) and shows a share tooltip. Not a public component.
 */
const props = withDefaults(defineProps<{ segments?: NvMetricSegment[] }>(), { segments: () => [] })

const total = computed(() =>
  Math.max(
    1,
    props.segments.reduce((s, seg) => s + seg.value, 0),
  ),
)

/**
 * Interaction identity is the slice's resolved KEY, not its array index — if
 * the segments reorder/filter mid-hover (live data), an index would silently
 * re-point the linked highlight at a different business item. The index
 * fallback is only valid under the documented order-stable contract.
 */
const hovered = ref<string | null>(null)
const dimmed = (seg: NvMetricSegment, i: number) =>
  hovered.value !== null && hovered.value !== metricItemKey(seg, i)

const tip = useMetricTooltip()
function showTip(e: MouseEvent, seg: NvMetricSegment, i: number) {
  hovered.value = metricItemKey(seg, i)
  const pct = ((seg.value / total.value) * 100).toFixed(1)
  tip.move(e, {
    rows: [
      {
        label: seg.label,
        value: `${seg.value} · ${pct}%`,
        swatchClass: metricToneFill[seg.tone ?? 'neutral'],
      },
    ],
  })
}
function clear() {
  hovered.value = null
  tip.hide()
}
// Live data can filter the hovered slice away without ever firing mouseleave on
// the unmounted node — a stale key would dim every remaining slice and keep the
// removed item's tooltip on screen. Watch the resolved-key PROJECTION (as an
// array, never join-serialised: a key containing the separator could collide
// two different memberships into the same string and swallow the change) — the
// mapping getter reads every element, so in-place splice/sort fires it, while
// a `() => props.segments` reference getter never would (probe-verified).
watch(
  () => props.segments.map(metricItemKey),
  () => {
    if (
      hovered.value !== null &&
      !props.segments.some((s, i) => metricItemKey(s, i) === hovered.value)
    )
      clear()
  },
)
</script>

<template>
  <div class="mt-4 flex h-1.5 gap-0.5">
    <span
      v-for="(seg, i) in segments"
      :key="metricItemKey(seg, i)"
      :class="
        cn(
          'nv-metric-slice block rounded-sm first:rounded-l-full last:rounded-r-full',
          metricToneFill[seg.tone ?? 'neutral'],
          dimmed(seg, i) && 'nv-metric-dim',
        )
      "
      :style="{ flex: seg.value }"
      @mousemove="(e) => showTip(e, seg, i)"
      @mouseleave="clear"
    />
  </div>
  <ul class="mt-3 flex flex-wrap gap-x-3.5 gap-y-1.5">
    <li
      v-for="(seg, i) in segments"
      :key="metricItemKey(seg, i)"
      :class="
        cn(
          'nv-metric-slice inline-flex items-center gap-1.5 text-xs text-muted-foreground',
          dimmed(seg, i) && 'nv-metric-dim',
        )
      "
      @mousemove="(e) => showTip(e, seg, i)"
      @mouseleave="clear"
    >
      <span :class="cn('size-2 flex-none rounded-sm', metricToneFill[seg.tone ?? 'neutral'])" />
      {{ seg.label }}
      <b class="font-semibold text-foreground tabular-nums">{{ seg.value }}</b>
    </li>
  </ul>
  <NvMetricTipPart :tip="tip" />
</template>

<style scoped>
@layer nv-components {
  /* segment ↔ legend linked highlight: pointing at either dims the other slices */
  .nv-metric-slice {
    transition: opacity var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  .nv-metric-dim {
    opacity: 0.4;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-metric-slice {
      transition: none;
    }
  }
}
</style>
