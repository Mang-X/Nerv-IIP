<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { Donut } from '@unovis/ts'
import { VisDonut, VisSingleContainer, VisTooltip } from '@unovis/vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — donut composition chart on unovis (e.g. work-order status mix), with a
 * legend that carries value + share and a per-segment hover tooltip. Token-driven
 * colors; segment gaps are transparent so the chart sits on any background.
 */
export interface DonutSlice {
  label: string
  value: number
  color?: string
}

const props = withDefaults(
  defineProps<{
    data: DonutSlice[]
    height?: number
    centralLabel?: string
    centralSubLabel?: string
    class?: HTMLAttributes['class']
  }>(),
  { height: 200 },
)

const palette = [
  'var(--chart-1)',
  'var(--chart-2)',
  'var(--chart-3)',
  'var(--chart-4)',
  'var(--chart-5)',
]
const sliceColor = (d: DonutSlice, i: number) => d.color ?? palette[i % palette.length]
const value = (d: DonutSlice) => d.value
const total = computed(() => props.data.reduce((sum, d) => sum + d.value, 0) || 1)
const legend = computed(() =>
  props.data.map((d, i) => ({
    ...d,
    color: sliceColor(d, i),
    share: Math.round((d.value / total.value) * 100),
  })),
)

// unovis binds each segment to a d3 arc datum: `{ data: DonutSlice, index, ... }`.
const segmentTooltip = (d: { data?: DonutSlice; index?: number } | DonutSlice) => {
  const slice = (d as { data?: DonutSlice }).data ?? (d as DonutSlice)
  if (!slice) return ''
  const i = (d as { index?: number }).index ?? props.data.indexOf(slice)
  const share = Math.round((slice.value / total.value) * 100)
  return `<div class="nv-vis-card"><div class="nv-vis-row"><span class="nv-vis-dot" style="background:${sliceColor(
    slice,
    i,
  )}"></span><span>${slice.label}</span><b>${slice.value} · ${share}%</b></div></div>`
}
const triggers = { [Donut.selectors.segment]: segmentTooltip }
</script>

<template>
  <div :class="cn('nv-donut flex items-center gap-6', props.class)">
    <div class="shrink-0" :style="{ width: `${height}px` }">
      <VisSingleContainer :data="data" :height="height">
        <VisDonut
          :value="value"
          :color="sliceColor"
          :arc-width="26"
          :pad-angle="0.02"
          :corner-radius="3"
          :central-label="centralLabel"
          :central-sub-label="centralSubLabel"
        />
        <VisTooltip :triggers="triggers" />
      </VisSingleContainer>
    </div>
    <ul class="flex min-w-0 flex-1 flex-col gap-2.5">
      <li v-for="item in legend" :key="item.label" class="flex items-center gap-2.5 text-sm">
        <span class="size-2.5 shrink-0 rounded-[3px]" :style="{ background: item.color }" />
        <span class="truncate text-foreground">{{ item.label }}</span>
        <span class="ml-auto tabular-nums text-muted-foreground">{{ item.value }}</span>
        <span class="w-9 text-right text-xs tabular-nums text-muted-foreground"
          >{{ item.share }}%</span
        >
      </li>
    </ul>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-donut {
    --vis-donut-central-label-text-color: var(--foreground);
    --vis-donut-central-sub-label-text-color: var(--muted-foreground);
    --vis-donut-central-label-font-family: var(--font-sans);
    --vis-donut-central-sub-label-font-family: var(--font-sans);
    /* Transparent background ring + gap strokes → works on any surface / theme
     (unovis' own dark-theme isn't wired to our .dark class). */
    --vis-donut-background-color: transparent;
    --vis-tooltip-background-color: color-mix(in oklch, var(--popover) 86%, transparent);
    --vis-tooltip-text-color: var(--popover-foreground);
    --vis-tooltip-border-color: color-mix(in oklch, var(--border) 80%, transparent);
    --vis-tooltip-padding: 0;
    --vis-tooltip-border-radius: 8px;
  }
  .nv-donut :deep([class*='-tooltip']) {
    backdrop-filter: blur(8px) saturate(1.4);
    -webkit-backdrop-filter: blur(8px) saturate(1.4);
    box-shadow: 0 8px 28px -12px color-mix(in oklch, black 50%, transparent);
  }
  .nv-donut :deep(.nv-vis-card) {
    min-width: 8rem;
    padding: 8px 10px;
    font-size: 12px;
  }
  .nv-donut :deep(.nv-vis-row) {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 2px 0;
  }
  .nv-donut :deep(.nv-vis-row b) {
    margin-left: auto;
    font-variant-numeric: tabular-nums;
  }
  .nv-donut :deep(.nv-vis-dot) {
    width: 8px;
    height: 8px;
    border-radius: 9999px;
  }
}
</style>
