<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { VisAxis, VisCrosshair, VisGroupedBar, VisTooltip, VisXYContainer } from '@unovis/vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — categorical (optionally grouped) bar chart on unovis. One or more
 * series, rounded bars, token-driven colors. e.g. output by work center.
 */
export interface BarSeries {
  key: string
  label: string
  color?: string
}
type Row = Record<string, number | string>

const props = withDefaults(
  defineProps<{
    data: Row[]
    xKey: string
    series: BarSeries[]
    height?: number
    valueSuffix?: string
    class?: HTMLAttributes['class']
  }>(),
  { height: 260, valueSuffix: '' },
)

const palette = [
  'var(--chart-1)',
  'var(--chart-2)',
  'var(--chart-3)',
  'var(--chart-4)',
  'var(--chart-5)',
]
const colors = computed(() => props.series.map((s, i) => s.color ?? palette[i % palette.length]))

const x = (_d: Row, i: number) => i
const yAccessors = computed(() => props.series.map((s) => (d: Row) => Number(d[s.key])))
const xTickFormat = (i: number) => String(props.data[i]?.[props.xKey] ?? '')

const tooltipTemplate = (d: Row) => {
  const rows = props.series
    .map(
      (s, i) =>
        `<div class="ds-vis-row"><span class="ds-vis-dot" style="background:${colors.value[i]}"></span><span>${s.label}</span><b>${d[s.key]}${props.valueSuffix}</b></div>`,
    )
    .join('')
  return `<div class="ds-vis-card"><div class="ds-vis-head">${d[props.xKey]}</div>${rows}</div>`
}
</script>

<template>
  <div :class="cn('ds-chart', props.class)">
    <div v-if="series.length > 1" class="mb-3 flex flex-wrap items-center gap-x-4 gap-y-1">
      <span
        v-for="(s, i) in series"
        :key="s.key"
        class="inline-flex items-center gap-1.5 text-xs text-muted-foreground"
      >
        <span class="size-2 rounded-full" :style="{ background: colors[i] }" />
        {{ s.label }}
      </span>
    </div>
    <VisXYContainer
      :data="data"
      :height="height"
      :margin="{ top: 8, right: 8, bottom: 4, left: 8 }"
    >
      <VisGroupedBar
        :x="x"
        :y="yAccessors"
        :color="colors"
        :rounded-corners="5"
        :group-padding="0.25"
        :bar-padding="0.1"
      />
      <VisAxis
        type="x"
        :tick-format="xTickFormat"
        :num-ticks="data.length"
        :grid-line="false"
        :tick-line="false"
        :domain-line="false"
      />
      <VisAxis type="y" :num-ticks="4" :tick-line="false" :domain-line="false" />
      <VisCrosshair :x="x" :color="colors" :template="tooltipTemplate" />
      <VisTooltip />
    </VisXYContainer>
  </div>
</template>

<style scoped>
@layer nv-components {
  .ds-chart {
    width: 100%;
    --vis-axis-grid-color: var(--border);
    --vis-axis-tick-label-color: var(--muted-foreground);
    --vis-axis-tick-label-font-size: 11px;
    --vis-axis-font-family: var(--font-sans);
    --vis-crosshair-line-stroke-color: color-mix(in oklch, var(--foreground) 16%, transparent);
    --vis-crosshair-circle-stroke-color: var(--card);
    --vis-tooltip-background-color: color-mix(in oklch, var(--popover) 86%, transparent);
    --vis-tooltip-text-color: var(--popover-foreground);
    --vis-tooltip-border-color: color-mix(in oklch, var(--border) 80%, transparent);
    --vis-tooltip-padding: 0;
    --vis-tooltip-border-radius: 8px;
  }
  .ds-chart :deep([class*='-tooltip']) {
    backdrop-filter: blur(8px) saturate(1.4);
    -webkit-backdrop-filter: blur(8px) saturate(1.4);
    box-shadow: 0 8px 28px -12px color-mix(in oklch, black 50%, transparent);
  }
  .ds-chart :deep(.ds-vis-card) {
    min-width: 9rem;
    padding: 8px 10px;
    font-size: 12px;
  }
  .ds-chart :deep(.ds-vis-head) {
    margin-bottom: 6px;
    font-weight: 600;
  }
  .ds-chart :deep(.ds-vis-row) {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 2px 0;
  }
  .ds-chart :deep(.ds-vis-row b) {
    margin-left: auto;
    font-variant-numeric: tabular-nums;
  }
  .ds-chart :deep(.ds-vis-dot) {
    width: 8px;
    height: 8px;
    border-radius: 9999px;
  }
}
</style>
