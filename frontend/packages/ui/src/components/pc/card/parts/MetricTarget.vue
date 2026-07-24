<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '../../../../lib/utils'
import { metricToneFill, type NvMetricTone } from '../metric'
import { useMetricTooltip } from '../useMetricTooltip'
import MetricTip from './MetricTip.vue'

/**
 * Internal — the `target` bottom-zone: a progress bar with a goal tick and a
 * structured hover tooltip (actual / target / attainment), plus `progressbar`
 * semantics for keyboard / SR users. Not a public component.
 */
const props = defineProps<{
  label: string
  value: string | number
  unit?: string
  targetLabel?: string
  progress?: number
  targetMarker?: number
  progressTone?: Extract<NvMetricTone, 'brand' | 'success' | 'warning' | 'danger'>
  footStart?: string
  footEnd?: string
}>()

const progressPct = computed(() => Math.max(0, Math.min(100, props.progress ?? 0)))
const progressFill = computed(
  () => metricToneFill[props.progressTone ?? (progressPct.value >= 100 ? 'success' : 'brand')],
)
const markerPct = computed(() => Math.max(0, Math.min(100, props.targetMarker ?? 100)))

const tip = useMetricTooltip()
function showTip(e: MouseEvent) {
  const rows = [{ label: props.label, value: `${props.value}${props.unit ?? ''}` }]
  if (props.targetLabel)
    rows.push({ label: '目标', value: props.targetLabel.replace(/^目标\s*/, '') })
  rows.push({ label: '达成', value: `${progressPct.value.toFixed(1)}%` })
  tip.move(e, { rows })
}
</script>

<template>
  <div
    class="nv-metric-bar relative mt-4 h-1.5 rounded-full bg-muted"
    role="progressbar"
    :aria-valuenow="progressPct"
    aria-valuemin="0"
    aria-valuemax="100"
    :aria-label="label"
    :aria-valuetext="`${value}${unit ?? ''}${targetLabel ? ` / ${targetLabel}` : ''}，达成 ${progressPct.toFixed(1)}%`"
    @mousemove="showTip"
    @mouseleave="tip.hide"
  >
    <div :class="cn('h-full rounded-full', progressFill)" :style="{ width: `${progressPct}%` }" />
    <span
      class="absolute -top-1 bottom-[-4px] w-0.5 rounded-full bg-foreground/55"
      :style="{ left: `calc(${markerPct}% - 1px)` }"
      aria-hidden="true"
    />
  </div>
  <div
    v-if="footStart || footEnd"
    class="mt-2.5 flex justify-between text-xs text-muted-foreground tabular-nums"
  >
    <span>{{ footStart }}</span
    ><span>{{ footEnd }}</span>
  </div>
  <MetricTip :tip="tip" />
</template>
