<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { MinusIcon, TrendingDownIcon, TrendingUpIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'
import NvAreaChart from '../chart/NvAreaChart.vue'
import NvCard from './NvCard.vue'
import {
  metricItemKey,
  metricToneText,
  metricToneTint,
  type NvMetricStripCell,
  resolveDeltaTone,
} from './metric'

/**
 * Pro — one card holding a row of related metrics, separators standing in for
 * card gaps. Highest-density KPI surface: sits atop a list page pinning the key
 * figures on one line without competing with the table below. Each cell owns a
 * label, a headline value, an optional delta / note sub-line, and an optional
 * mini trend chart.
 */
const props = withDefaults(
  defineProps<{
    cells: NvMetricStripCell[]
    class?: HTMLAttributes['class']
  }>(),
  { cells: () => [] },
)

/**
 * Cells share one row, so a cell without a sub-line ends 4–7px short of its
 * neighbours and the strip's bottom edge reads as ragged. When ANY cell carries
 * a meta line, hold the line's height open in the others — the reserve costs
 * nothing when no cell has one, and stays out of the accessibility tree.
 */
const reservesMeta = computed(() =>
  props.cells.some((cell) => Boolean(cell.meta) || Boolean(cell.delta)),
)

/**
 * Same ragged-edge argument one level down: a cell with a sparkline is ~34px
 * taller than one without. Mixed rows are common (a count has history, a
 * derived ratio doesn't), so reserve the chart band across the row whenever any
 * cell plots one.
 */
const reservesChart = computed(() => props.cells.some((cell) => (cell.series?.length ?? 0) > 1))

const metaIcon = { up: TrendingUpIcon, down: TrendingDownIcon, flat: MinusIcon } as const
function metaToneClass(tone?: string) {
  if (tone === 'up') return metricToneText.success
  if (tone === 'down') return metricToneText.danger
  return 'text-muted-foreground'
}

function chartData(cell: NvMetricStripCell) {
  return (cell.series ?? []).map((value, i) => ({
    label: cell.seriesLabels?.[i] ?? String(i + 1),
    value,
  }))
}

/**
 * The crosshair only surfaces the series on hover, so keyboard and
 * screen-reader users would get a decorative blank. Mirror the points as text
 * the way NvMetricCard's sparkline zone does.
 */
function chartAriaLabel(cell: NvMetricStripCell) {
  const unit = cell.seriesUnit ?? ''
  const points = (cell.series ?? []).map(
    (v, i) => `${cell.seriesLabels?.[i] ?? i + 1}: ${v}${unit}`,
  )
  return `${cell.label} 趋势，${points.length} 期：${points.join('；')}`
}
</script>

<template>
  <NvCard :class="cn('flex flex-col overflow-hidden p-0 sm:flex-row', props.class)">
    <div
      v-for="(cell, i) in cells"
      :key="metricItemKey(cell, i)"
      class="flex flex-1 flex-col gap-1 border-border p-4 [&:not(:first-child)]:border-t sm:p-5 sm:[&:not(:first-child)]:border-l sm:[&:not(:first-child)]:border-t-0"
    >
      <p class="truncate text-sm text-muted-foreground">{{ cell.label }}</p>
      <p
        :class="
          cn(
            'truncate text-xl font-semibold tabular-nums tracking-tight',
            cell.valueTone ? metricToneText[cell.valueTone] : '',
          )
        "
      >
        {{ cell.value
        }}<span v-if="cell.unit" class="ml-0.5 text-xs font-medium text-muted-foreground">{{
          cell.unit
        }}</span>
      </p>

      <span v-if="cell.delta" class="flex min-w-0 items-center gap-1.5 text-xs">
        <span
          :class="
            cn(
              'inline-flex shrink-0 items-center gap-1 rounded-full px-1.5 py-0.5 font-semibold tabular-nums',
              metricToneTint[resolveDeltaTone(cell.delta)],
            )
          "
        >
          <component
            :is="metaIcon[cell.delta.direction ?? 'flat']"
            class="size-3"
            aria-hidden="true"
          />{{ cell.delta.value }}
        </span>
        <span v-if="cell.meta" class="truncate text-muted-foreground">{{ cell.meta }}</span>
      </span>
      <span
        v-else-if="cell.meta"
        :class="
          cn('inline-flex items-center gap-1 text-xs tabular-nums', metaToneClass(cell.metaTone))
        "
      >
        <component
          :is="metaIcon[cell.metaTone as 'up' | 'down' | 'flat']"
          v-if="cell.metaTone && cell.metaTone !== 'neutral'"
          class="size-3"
          aria-hidden="true"
        />{{ cell.meta }}
      </span>
      <span v-else-if="reservesMeta" class="text-xs" aria-hidden="true">&nbsp;</span>

      <div
        v-if="(cell.series?.length ?? 0) > 1"
        role="img"
        :aria-label="chartAriaLabel(cell)"
        class="mt-1.5"
      >
        <NvAreaChart
          minimal
          crosshair
          :data="chartData(cell)"
          :height="34"
          :value-suffix="cell.seriesUnit ?? ''"
        />
      </div>
      <div v-else-if="reservesChart" class="mt-1.5 h-[34px]" aria-hidden="true" />
    </div>
  </NvCard>
</template>
