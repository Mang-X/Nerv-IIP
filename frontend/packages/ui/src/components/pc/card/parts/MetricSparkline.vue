<script setup lang="ts">
import { computed } from 'vue'
import NvAreaChart from '../../chart/NvAreaChart.vue'

/**
 * Internal — the `sparkline` bottom-zone: an in-card trend that reuses the
 * NvAreaChart engine (native crosshair) plus a `role="img"` text equivalent so
 * keyboard / screen-reader users get the series the crosshair only reveals on
 * hover. Not a public component.
 */
const props = withDefaults(
  defineProps<{
    label: string
    series?: number[]
    seriesLabels?: string[]
    seriesUnit?: string
    footStart?: string
    footEnd?: string
  }>(),
  { series: () => [] },
)

const chartData = computed(() =>
  props.series.map((v, i) => ({ label: props.seriesLabels?.[i] ?? String(i), value: v })),
)
const ariaLabel = computed(() => {
  const unit = props.seriesUnit ?? ''
  const points = props.series.map((v, i) => `${props.seriesLabels?.[i] ?? i + 1}: ${v}${unit}`)
  return `${props.label} 趋势，${points.length} 期：${points.join('；')}`
})
</script>

<template>
  <div v-if="chartData.length > 1" role="img" :aria-label="ariaLabel" class="mt-4">
    <NvAreaChart
      minimal
      crosshair
      :data="chartData"
      :height="46"
      :value-suffix="seriesUnit ?? ''"
    />
  </div>
  <div
    v-if="footStart || footEnd"
    class="mt-2 flex justify-between text-xs text-muted-foreground tabular-nums"
  >
    <span>{{ footStart }}</span
    ><span>{{ footEnd }}</span>
  </div>
</template>
