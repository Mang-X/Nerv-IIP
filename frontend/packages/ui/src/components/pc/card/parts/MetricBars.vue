<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '../../../../lib/utils'
import { metricToneFill, type NvMetricTone } from '../metric'
import { useMetricTooltip } from '../useMetricTooltip'
import MetricTip from './MetricTip.vue'

/**
 * Internal — the `bars` bottom-zone: a compact per-period bar strip with the
 * current period emphasised, per-bar tooltips, and a `role="img"` text
 * equivalent (the bars themselves are pointer-only). Not a public component.
 */
const props = withDefaults(
  defineProps<{
    label: string
    series?: number[]
    seriesLabels?: string[]
    seriesUnit?: string
    currentIndex?: number
    barTones?: NvMetricTone[]
    footStart?: string
    footEnd?: string
  }>(),
  { series: () => [] },
)

const barMax = computed(() => Math.max(1, ...props.series))
function barHeight(v: number) {
  return `${Math.max(6, Math.round((v / barMax.value) * 100))}%`
}
function barTone(i: number): NvMetricTone {
  if (props.barTones?.[i]) return props.barTones[i]
  return i === props.currentIndex ? 'brand' : 'neutral'
}
// Literal class strings on both sides — Tailwind only emits classes it can see
// verbatim in source, so a tone+opacity pair must never be built by concatenation
// (`bg-${tone}/70` silently produces an unstyled, invisible bar).
const BAR_EMPHASIS: Record<NvMetricTone, string> = {
  brand: 'bg-brand',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-destructive',
  neutral: 'bg-brand/30',
}
const BAR_QUIET: Record<NvMetricTone, string> = {
  brand: 'bg-brand/70',
  success: 'bg-success/70',
  warning: 'bg-warning/70',
  danger: 'bg-destructive/70',
  neutral: 'bg-brand/30',
}
function barClass(i: number) {
  return (i === props.currentIndex ? BAR_EMPHASIS : BAR_QUIET)[barTone(i)]
}
const ariaLabel = computed(() => {
  const unit = props.seriesUnit ?? ''
  const points = props.series.map((v, i) => `${props.seriesLabels?.[i] ?? i + 1}: ${v}${unit}`)
  return `${props.label}，${points.length} 期：${points.join('；')}`
})

const tip = useMetricTooltip()
function showTip(e: MouseEvent, i: number) {
  const raw = props.series[i]
  if (raw == null) return
  tip.move(e, {
    title: props.seriesLabels?.[i],
    rows: [{ label: props.label, value: `${raw}${props.seriesUnit ?? ''}` }],
  })
}
</script>

<template>
  <div class="nv-metric-bars mt-4 flex h-[46px] items-end gap-1" role="img" :aria-label="ariaLabel">
    <span
      v-for="(v, i) in series"
      :key="i"
      :class="cn('min-h-1 flex-1 rounded-t-sm', barClass(i))"
      :style="{ height: barHeight(v) }"
      aria-hidden="true"
      @mousemove="(e) => showTip(e, i)"
      @mouseleave="tip.hide"
    />
  </div>
  <div
    v-if="footStart || footEnd"
    class="mt-1.5 flex justify-between text-[11px] text-muted-foreground tabular-nums"
  >
    <span>{{ footStart }}</span
    ><span>{{ footEnd }}</span>
  </div>
  <MetricTip :tip="tip" />
</template>

<style scoped>
@layer nv-components {
  /* hover any bar → dim the rest so the pointed-at column reads first */
  .nv-metric-bars > span {
    transition: opacity var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  .nv-metric-bars:hover > span {
    opacity: 0.4;
  }
  .nv-metric-bars > span:hover {
    opacity: 1;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-metric-bars > span {
      transition: none;
    }
  }
}
</style>
