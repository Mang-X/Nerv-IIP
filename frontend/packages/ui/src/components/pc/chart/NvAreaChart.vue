<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { VisArea, VisAxis, VisCrosshair, VisLine, VisTooltip, VisXYContainer } from '@unovis/vue'
import { cn, escapeHtml } from '../../../lib/utils'

/**
 * Pro — real area chart built on unovis (the engine shadcn-vue's chart is based
 * on). Brand-driven: stroke/fill read `--nv-brand`, so the chart re-tints with the
 * runtime accent. Axes, grid, crosshair and tooltip are mapped to design tokens.
 */
interface ChartPoint {
  label: string
  value: number
}

const props = withDefaults(
  defineProps<{
    data: ChartPoint[]
    height?: number
    valueSuffix?: string
    /** Sparkline mode: no axes, tight margins — for in-card trends. */
    minimal?: boolean
    /** Keep the hover crosshair + tooltip even in `minimal` mode (in-card trend scrubbing). */
    crosshair?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { height: 240, valueSuffix: '', minimal: false, crosshair: false },
)

const x = (_d: ChartPoint, i: number) => i
const y = (d: ChartPoint) => d.value
const xTickFormat = (i: number) => props.data[i]?.label ?? ''
const tooltipTemplate = (d: ChartPoint) =>
  `<div class="nv-vis-tt"><span>${escapeHtml(d.label)}</span><b>${escapeHtml(d.value)}${escapeHtml(props.valueSuffix)}</b></div>`
const chartMargin = computed(() =>
  props.minimal
    ? { top: 4, right: 2, bottom: 2, left: 2 }
    : { top: 10, right: 12, bottom: 4, left: 8 },
)
</script>

<template>
  <div :class="cn('nv-chart w-full', props.class)">
    <VisXYContainer :data="data" :height="height" :margin="chartMargin">
      <VisArea :x="x" :y="y" color="var(--nv-brand)" :opacity="0.13" />
      <VisLine :x="x" :y="y" color="var(--nv-brand)" :line-width="2" />
      <VisAxis
        v-if="!minimal"
        type="x"
        :tick-format="xTickFormat"
        :num-ticks="data.length"
        :grid-line="false"
        :tick-line="false"
        :domain-line="false"
      />
      <VisAxis v-if="!minimal" type="y" :num-ticks="4" :tick-line="false" :domain-line="false" />
      <template v-if="!minimal || crosshair">
        <VisCrosshair color="var(--nv-brand)" :template="tooltipTemplate" />
        <VisTooltip />
      </template>
    </VisXYContainer>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-chart {
    --vis-axis-grid-color: var(--border);
    --vis-axis-tick-label-color: var(--muted-foreground);
    --vis-axis-tick-label-font-size: 11px;
    --vis-axis-font-family: var(--font-sans);
    --vis-crosshair-line-stroke-color: color-mix(in oklch, var(--nv-brand) 50%, transparent);
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
  .nv-chart :deep(.nv-vis-tt) {
    display: flex;
    align-items: center;
    gap: 14px;
    padding: 7px 11px;
    font-size: 12px;
  }
  .nv-chart :deep(.nv-vis-tt b) {
    font-weight: 600;
    font-variant-numeric: tabular-nums;
  }
}
</style>
