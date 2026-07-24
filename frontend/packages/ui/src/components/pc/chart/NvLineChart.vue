<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { VisAxis, VisCrosshair, VisLine, VisTooltip, VisXYContainer } from '@unovis/vue'
import { cn, cssColor, escapeHtml } from '../../../lib/utils'

/**
 * Pro — multi-series line chart on unovis (e.g. plan vs. actual). Each series
 * maps to a chart token by default; legend + crosshair tooltip included.
 */
export interface LineSeries {
  key: string
  label: string
  color?: string
}
type Row = Record<string, number | string>

const props = withDefaults(
  defineProps<{
    data: Row[]
    xKey: string
    series: LineSeries[]
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
        `<div class="nv-vis-row"><span class="nv-vis-dot" style="background:${cssColor(colors.value[i])}"></span><span>${escapeHtml(s.label)}</span><b>${escapeHtml(d[s.key])}${escapeHtml(props.valueSuffix)}</b></div>`,
    )
    .join('')
  return `<div class="nv-vis-card"><div class="nv-vis-head">${escapeHtml(d[props.xKey])}</div>${rows}</div>`
}
</script>

<template>
  <div :class="cn('nv-chart', props.class)">
    <div class="mb-3 flex flex-wrap items-center gap-x-4 gap-y-1">
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
      :margin="{ top: 8, right: 12, bottom: 4, left: 8 }"
    >
      <VisLine :x="x" :y="yAccessors" :color="colors" :line-width="2" />
      <VisAxis
        type="x"
        :tick-format="xTickFormat"
        :num-ticks="data.length"
        :grid-line="false"
        :tick-line="false"
        :domain-line="false"
      />
      <VisAxis type="y" :num-ticks="4" :tick-line="false" :domain-line="false" />
      <VisCrosshair :color="colors" :template="tooltipTemplate" />
      <VisTooltip />
    </VisXYContainer>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-chart {
    width: 100%;
    --vis-axis-grid-color: var(--border);
    --vis-axis-tick-label-color: var(--muted-foreground);
    --vis-axis-tick-label-font-size: 11px;
    --vis-axis-font-family: var(--font-sans);
    --vis-crosshair-line-stroke-color: color-mix(in oklch, var(--foreground) 30%, transparent);
    --vis-crosshair-circle-stroke-color: var(--card);
    --vis-tooltip-background-color: var(--nv-glass-bg);
    --vis-tooltip-text-color: var(--popover-foreground);
    --vis-tooltip-border-color: var(--nv-glass-border);
    --vis-tooltip-padding: 0;
    --vis-tooltip-border-radius: 8px;
  }
  .nv-chart :deep([class*='-tooltip']) {
    backdrop-filter: var(--nv-glass-filter);
    -webkit-backdrop-filter: var(--nv-glass-filter);
    box-shadow: var(--nv-glass-shadow);
  }
  .nv-chart :deep(.nv-vis-card) {
    min-width: 9rem;
    padding: 8px 10px;
    font-size: 12px;
  }
  .nv-chart :deep(.nv-vis-head) {
    margin-bottom: 6px;
    font-weight: 600;
  }
  .nv-chart :deep(.nv-vis-row) {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 2px 0;
  }
  .nv-chart :deep(.nv-vis-row b) {
    margin-left: auto;
    font-variant-numeric: tabular-nums;
  }
  .nv-chart :deep(.nv-vis-dot) {
    width: 8px;
    height: 8px;
    border-radius: 9999px;
  }
}
</style>
