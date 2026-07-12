<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { MinusIcon, TrendingDownIcon, TrendingUpIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import NvAreaChart from '../chart/NvAreaChart.vue'
import NvCard from './NvCard.vue'

export type TrendDirection = 'up' | 'down' | 'flat'

/**
 * Pro — metric card with an inline unovis sparkline (minimal mode of
 * NvAreaChart), so in-card trends share the same engine as full charts and
 * re-tint with the runtime brand. Numbers render tabular-nums.
 */
const props = withDefaults(
  defineProps<{
    label: string
    value: string | number
    trend?: { value: string; direction?: TrendDirection }
    hint?: string
    /** Series for the sparkline (>= 2 points). */
    series?: number[]
    class?: HTMLAttributes['class']
  }>(),
  { series: () => [] },
)

const direction = computed<TrendDirection>(() => props.trend?.direction ?? 'flat')
const trendIcon = computed(() =>
  direction.value === 'up'
    ? TrendingUpIcon
    : direction.value === 'down'
      ? TrendingDownIcon
      : MinusIcon,
)
const trendTone = computed(() =>
  direction.value === 'up'
    ? 'text-success-strong'
    : direction.value === 'down'
      ? 'text-destructive-strong'
      : 'text-muted-foreground',
)

const chartData = computed(() => props.series.map((v, i) => ({ label: String(i), value: v })))
</script>

<template>
  <NvCard :class="cn('overflow-hidden p-5', props.class)">
    <div class="flex items-start justify-between gap-3">
      <div class="min-w-0">
        <p class="truncate text-sm text-muted-foreground">{{ label }}</p>
        <p class="mt-1.5 text-2xl font-semibold tabular-nums tracking-tight">{{ value }}</p>
      </div>
      <span
        v-if="trend"
        :class="cn('inline-flex items-center gap-1 text-xs font-medium tabular-nums', trendTone)"
      >
        <component :is="trendIcon" class="size-3.5" aria-hidden="true" />
        {{ trend.value }}
      </span>
    </div>

    <NvAreaChart v-if="chartData.length > 1" minimal :data="chartData" :height="46" class="mt-4" />

    <p v-if="hint" class="mt-3 text-xs text-muted-foreground">{{ hint }}</p>
  </NvCard>
</template>
