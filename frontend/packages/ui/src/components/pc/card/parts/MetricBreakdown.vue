<script setup lang="ts">
import { computed, ref } from 'vue'
import { cn } from '../../../../lib/utils'
import { metricToneFill, type NvMetricSegment } from '../metric'
import { useMetricTooltip } from '../useMetricTooltip'
import MetricTip from './MetricTip.vue'

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

const hovered = ref<number | null>(null)
const dimmed = (i: number) => hovered.value !== null && hovered.value !== i

const tip = useMetricTooltip()
function showTip(e: MouseEvent, seg: NvMetricSegment, i: number) {
  hovered.value = i
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
</script>

<template>
  <div class="mt-4 flex h-1.5 gap-0.5">
    <span
      v-for="(seg, i) in segments"
      :key="seg.key ?? i"
      :class="
        cn(
          'nv-metric-slice block rounded-sm first:rounded-l-full last:rounded-r-full',
          metricToneFill[seg.tone ?? 'neutral'],
          dimmed(i) && 'nv-metric-dim',
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
      :key="seg.key ?? i"
      :class="
        cn(
          'nv-metric-slice inline-flex items-center gap-1.5 text-xs text-muted-foreground',
          dimmed(i) && 'nv-metric-dim',
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
  <MetricTip :tip="tip" />
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
