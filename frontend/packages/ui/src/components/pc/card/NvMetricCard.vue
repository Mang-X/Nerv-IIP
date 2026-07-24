<script setup lang="ts">
import type { Component, HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { MinusIcon, TrendingDownIcon, TrendingUpIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'
import NvAreaChart from '../chart/NvAreaChart.vue'
import NvCard from './NvCard.vue'
import {
  metricToneText,
  metricToneTint,
  type NvMetricAction,
  type NvMetricDelta,
  type NvMetricFacet,
  type NvMetricSegment,
  type NvMetricStatus,
  type NvMetricTone,
  type NvMetricVariant,
  resolveDeltaTone,
} from './metric'
import MetricAlert from './parts/MetricAlert.vue'
import MetricBars from './parts/MetricBars.vue'
import MetricBreakdown from './parts/MetricBreakdown.vue'
import MetricFacets from './parts/MetricFacets.vue'
import MetricSparkline from './parts/MetricSparkline.vue'
import MetricTarget from './parts/MetricTarget.vue'

/**
 * Pro — the workhorse KPI card. A `variant` decides the structured bottom-zone
 * (icon / sparkline / target progress / status breakdown / mini bars / alert /
 * dimension facets) so the space below the headline carries actionable data —
 * a trend, a gap-to-target, a state split — never filler prose. This shell owns
 * the shared header (label / value / delta chip / status) and the container
 * degrade; each bottom-zone is a co-located internal part under `./parts/`, so
 * changing one variant's geometry or interaction can't ripple into the others.
 */
const props = withDefaults(
  defineProps<{
    /** Which structured bottom-zone to render. */
    variant?: NvMetricVariant
    label: string
    value: string | number
    unit?: string
    /** Accent tone: the icon chip (`icon`), and the emphasis of an `alert` card. */
    tone?: NvMetricTone
    /** Leading icon component for `variant="icon"`. */
    icon?: Component
    /** Top-right change chip (icon / sparkline / bars / facets / default). */
    trend?: NvMetricDelta

    /** Series for `sparkline` / `bars`. */
    series?: number[]
    /** Per-point labels for the viz tooltip (sparkline / bars). */
    seriesLabels?: string[]
    /** Unit suffix shown in the viz tooltip. */
    seriesUnit?: string
    /** `bars`: index of the emphasised (e.g. 今日) bar. */
    currentIndex?: number
    /** `bars`: per-bar tone override, aligned to `series`. */
    barTones?: NvMetricTone[]

    /** `target`: fill percent 0–100 (consumer-computed, stays predictable). */
    progress?: number
    /** `target`: tick position 0–100; defaults to 100 (the goal at bar end). */
    targetMarker?: number
    /** `target`: label shown top-right, e.g. `目标 15,000 件`. */
    targetLabel?: string
    /** `target`/`sparkline`/`bars`: structured footer slots (no free text elsewhere). */
    footStart?: string
    footEnd?: string
    /** `target`: fill tone; defaults to success at ≥100%, else brand. */
    progressTone?: Extract<NvMetricTone, 'brand' | 'success' | 'warning' | 'danger'>

    /** `breakdown`: slices of the headline total. */
    segments?: NvMetricSegment[]

    /** `alert`: status pill top-right. */
    status?: NvMetricStatus
    /** `alert`: footer call-to-action. */
    action?: NvMetricAction

    /** `facets`: dimension chips. */
    facets?: NvMetricFacet[]

    /**
     * @deprecated 自由描述文本会被填成无意义内容；改用结构化变体
     * （`sparkline`/`target`/`breakdown`/`facets`…）。将在下一 major 移除。
     */
    hint?: string
    class?: HTMLAttributes['class']
  }>(),
  { variant: 'default', tone: 'brand' },
)

const emit = defineEmits<{
  (e: 'action'): void
  (e: 'facet', facet: NvMetricFacet): void
}>()

// --- delta chip -------------------------------------------------------------
const deltaTone = computed<NvMetricTone>(() =>
  props.trend ? resolveDeltaTone(props.trend) : 'neutral',
)
const deltaIcon = computed(() => {
  const dir = props.trend?.direction
  if (dir === 'up') return TrendingUpIcon
  if (dir === 'down') return TrendingDownIcon
  return MinusIcon
})

// --- alert shell + default-variant legacy sparkline -------------------------
const alertShell = computed(() => {
  if (props.variant !== 'alert') return ''
  if (props.tone === 'danger') return 'border-destructive/30 bg-destructive/[0.04]'
  if (props.tone === 'warning') return 'border-warning/35 bg-warning/[0.05]'
  return ''
})
const valueToneClass = computed(() =>
  props.variant === 'alert' && (props.tone === 'danger' || props.tone === 'warning')
    ? metricToneText[props.tone]
    : '',
)
const defaultChartData = computed(() =>
  (props.series ?? []).map((v, i) => ({ label: props.seriesLabels?.[i] ?? String(i), value: v })),
)
</script>

<template>
  <NvCard
    :class="cn('nv-metric overflow-hidden p-5', alertShell, props.class)"
    :data-variant="variant"
  >
    <!-- icon variant: horizontal, tone chip leads -->
    <div v-if="variant === 'icon'" class="flex items-center gap-3.5">
      <span
        v-if="icon"
        :class="
          cn(
            'nv-metric-iconbox size-11 flex-none place-items-center rounded-[10px]',
            metricToneTint[tone],
          )
        "
      >
        <component :is="icon" class="size-[21px]" aria-hidden="true" />
      </span>
      <div class="flex min-w-0 flex-1 flex-col gap-0.5">
        <p class="truncate text-sm text-muted-foreground">{{ label }}</p>
        <p class="truncate text-[22px] font-semibold leading-tight tabular-nums tracking-tight">
          {{ value
          }}<span v-if="unit" class="ml-0.5 text-sm font-medium text-muted-foreground">{{
            unit
          }}</span>
        </p>
      </div>
      <span
        v-if="trend"
        :class="
          cn(
            'nv-metric-chip ml-auto shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums',
            metricToneTint[deltaTone],
          )
        "
      >
        <component :is="deltaIcon" class="size-3" aria-hidden="true" />{{ trend.value }}
      </span>
    </div>

    <!-- all other variants: vertical, shared header + a bottom-zone part -->
    <template v-else>
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0 flex-1">
          <p class="truncate text-sm text-muted-foreground">{{ label }}</p>
          <p
            :class="
              cn(
                'mt-1.5 truncate text-2xl font-semibold tabular-nums tracking-tight',
                valueToneClass,
              )
            "
          >
            {{ value
            }}<span v-if="unit" class="ml-0.5 text-sm font-medium text-muted-foreground">{{
              unit
            }}</span>
          </p>
        </div>

        <span
          v-if="variant === 'alert' && status"
          :class="
            cn(
              'shrink-0 rounded-full px-2 py-0.5 text-xs font-semibold',
              metricToneTint[status.tone],
            )
          "
        >
          {{ status.label }}
        </span>
        <span
          v-else-if="variant === 'target' && targetLabel"
          class="shrink-0 text-xs text-muted-foreground tabular-nums"
        >
          {{ targetLabel }}
        </span>
        <span
          v-else-if="trend"
          :class="
            cn(
              'nv-metric-chip shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums',
              metricToneTint[deltaTone],
            )
          "
        >
          <component :is="deltaIcon" class="size-3" aria-hidden="true" />{{ trend.value }}
        </span>
      </div>

      <MetricSparkline
        v-if="variant === 'sparkline'"
        :label="label"
        :series="series"
        :series-labels="seriesLabels"
        :series-unit="seriesUnit"
        :foot-start="footStart"
        :foot-end="footEnd"
      />
      <MetricTarget
        v-else-if="variant === 'target'"
        :label="label"
        :value="value"
        :unit="unit"
        :target-label="targetLabel"
        :progress="progress"
        :target-marker="targetMarker"
        :progress-tone="progressTone"
        :foot-start="footStart"
        :foot-end="footEnd"
      />
      <MetricBreakdown v-else-if="variant === 'breakdown'" :segments="segments" />
      <MetricBars
        v-else-if="variant === 'bars'"
        :label="label"
        :series="series"
        :series-labels="seriesLabels"
        :series-unit="seriesUnit"
        :current-index="currentIndex"
        :bar-tones="barTones"
        :foot-start="footStart"
        :foot-end="footEnd"
      />
      <MetricFacets
        v-else-if="variant === 'facets'"
        :facets="facets"
        @facet="(f) => emit('facet', f)"
      />
      <MetricAlert
        v-else-if="variant === 'alert'"
        :foot-start="footStart"
        :action="action"
        @action="emit('action')"
      />

      <!-- default (back-compat: optional sparkline + deprecated hint) -->
      <template v-else>
        <NvAreaChart
          v-if="defaultChartData.length > 1"
          minimal
          :data="defaultChartData"
          :height="46"
          class="mt-4"
        />
        <p v-if="hint" class="mt-3 text-xs text-muted-foreground">{{ hint }}</p>
      </template>
    </template>
  </NvCard>
</template>

<style scoped>
@layer nv-components {
  /* A KPI card can't know how wide its grid cell is, so it degrades on its own
     container. Priority is label > value > delta chip: when the cell gets too
     tight the *supplementary* chip yields first, then the icon — the identity
     (label) and the number must never be the thing that disappears. */
  .nv-metric {
    container-type: inline-size;
  }
  /* `display` for these two is declared here rather than via a Tailwind utility
     on purpose: utilities land in a layer that outranks nv-components, so a
     `display: none` written here could never win against an `inline-flex`/`grid`
     class. Owning the property outright keeps the container queries below
     effective — they're later in the same layer, so they simply win. */
  .nv-metric-chip {
    display: inline-flex;
  }
  .nv-metric-iconbox {
    display: grid;
  }
  @container (max-width: 208px) {
    .nv-metric-chip {
      display: none;
    }
  }
  @container (max-width: 152px) {
    .nv-metric-iconbox {
      display: none;
    }
  }
}
</style>
