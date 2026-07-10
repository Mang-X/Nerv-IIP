<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { ArrowDownIcon, ArrowUpIcon, MinusIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import CardPro from '../card/CardPro.vue'

export interface MetricComparisonSide {
  label: string
  value: number
}

/**
 * Pro — plan-vs-actual / target-vs-current comparison card. Auto-computes the
 * delta, attainment ratio and a clamped progress bar; tone follows `betterWhen`
 * (success when the actual beats baseline in the desired direction, destructive
 * when it lags). Numbers render tabular-nums. Composes CardPro, never edits原版.
 */
const props = withDefaults(
  defineProps<{
    label: string
    baseline: MetricComparisonSide
    actual: MetricComparisonSide
    unit?: string
    betterWhen?: 'higher' | 'lower'
    class?: HTMLAttributes['class']
  }>(),
  { betterWhen: 'higher' },
)

const delta = computed(() => props.actual.value - props.baseline.value)

/** Attainment ratio (actual / baseline), guarded against divide-by-zero. */
const attainment = computed(() => {
  const base = props.baseline.value
  if (base === 0) return props.actual.value === 0 ? 100 : null
  return (props.actual.value / base) * 100
})

/** Whether the actual is favourable vs baseline given the desired direction. */
const isFavorable = computed(() => {
  if (delta.value === 0) return null
  return props.betterWhen === 'higher' ? delta.value > 0 : delta.value < 0
})

const deltaTone = computed(() =>
  isFavorable.value === null
    ? 'text-muted-foreground'
    : isFavorable.value
      ? 'text-success-strong'
      : 'text-destructive-strong',
)

const deltaIcon = computed(() =>
  delta.value === 0 ? MinusIcon : delta.value > 0 ? ArrowUpIcon : ArrowDownIcon,
)

/** Bar fill, clamped to 0–100% of the attainment ratio. */
const barPct = computed(() => {
  if (attainment.value === null) return 0
  return Math.max(0, Math.min(100, attainment.value))
})

const barTone = computed(() => (isFavorable.value === false ? 'bg-destructive' : 'bg-success'))

const numberFmt = (v: number) =>
  Number.isInteger(v)
    ? v.toLocaleString('en-US')
    : v.toLocaleString('en-US', { maximumFractionDigits: 2 })

const deltaLabel = computed(() => {
  const sign = delta.value > 0 ? '+' : ''
  return `${sign}${numberFmt(delta.value)}`
})

const attainmentLabel = computed(() =>
  attainment.value === null ? '—' : `${numberFmt(Math.round(attainment.value))}%`,
)
</script>

<template>
  <CardPro :class="cn('p-5', props.class)" data-slot="metric-comparison-pro">
    <div class="flex items-start justify-between gap-3">
      <p class="truncate text-sm text-muted-foreground">{{ label }}</p>
      <span
        :class="cn('inline-flex items-center gap-1 text-xs font-medium tabular-nums', deltaTone)"
      >
        <component :is="deltaIcon" class="size-3.5" aria-hidden="true" />
        {{ deltaLabel }}<template v-if="unit"> {{ unit }}</template>
      </span>
    </div>

    <div class="mt-4 flex items-end justify-between gap-4">
      <div class="min-w-0">
        <p class="truncate text-xs text-muted-foreground">{{ baseline.label }}</p>
        <p class="mt-0.5 text-lg font-medium tabular-nums text-muted-foreground">
          {{ numberFmt(baseline.value) }}<span v-if="unit" class="ml-0.5 text-xs">{{ unit }}</span>
        </p>
      </div>
      <div class="min-w-0 text-right">
        <p class="truncate text-xs text-muted-foreground">{{ actual.label }}</p>
        <p class="mt-0.5 text-2xl font-semibold tabular-nums tracking-tight">
          {{ numberFmt(actual.value)
          }}<span v-if="unit" class="ml-0.5 text-sm font-normal text-muted-foreground">{{
            unit
          }}</span>
        </p>
      </div>
    </div>

    <div class="mt-4">
      <div class="flex items-center justify-between text-xs">
        <span class="text-muted-foreground">达成率</span>
        <span :class="cn('font-medium tabular-nums', deltaTone)">{{ attainmentLabel }}</span>
      </div>
      <div class="mt-1.5 h-1.5 w-full overflow-hidden rounded-full bg-muted">
        <div
          :class="cn('ds-mc-bar h-full rounded-full', barTone)"
          :style="{ width: `${barPct}%` }"
        />
      </div>
    </div>
  </CardPro>
</template>

<style scoped>
@layer nv-components {
  .ds-mc-bar {
    transition: width 0.4s var(--nv-ease-out-quart, ease-out);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-mc-bar {
      transition: none;
    }
  }
}
</style>
