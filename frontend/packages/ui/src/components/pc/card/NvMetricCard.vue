<script setup lang="ts">
import type { Component, HTMLAttributes } from 'vue'
import { computed, ref } from 'vue'
import { ChevronRightIcon, MinusIcon, TrendingDownIcon, TrendingUpIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'
import NvAreaChart from '../chart/NvAreaChart.vue'
import NvCard from './NvCard.vue'
import {
  metricToneFill,
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
import { useMetricTooltip } from './useMetricTooltip'

/**
 * Pro — the workhorse KPI card. A `variant` decides the structured bottom-zone
 * (icon / sparkline / target progress / status breakdown / mini bars / alert /
 * dimension facets) so the space below the headline carries actionable data —
 * a trend, a gap-to-target, a state split — never filler prose. Tone classes
 * mirror NvStatusBadge; numbers render tabular-nums; inline vizzes get a
 * cursor-following tooltip (the sparkline reuses NvAreaChart's native crosshair).
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

const tip = useMetricTooltip()

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

// --- sparkline (reuses NvAreaChart engine) ----------------------------------
const chartData = computed(() =>
  (props.series ?? []).map((v, i) => ({ label: props.seriesLabels?.[i] ?? String(i), value: v })),
)

// --- mini bars --------------------------------------------------------------
const barMax = computed(() => Math.max(1, ...(props.series ?? [])))
function barHeight(v: number) {
  return `${Math.max(6, Math.round((v / barMax.value) * 100))}%`
}
function barTone(i: number): NvMetricTone {
  if (props.barTones?.[i]) return props.barTones[i]
  return i === props.currentIndex ? 'brand' : 'neutral'
}
// Literal class strings on both sides — Tailwind only emits classes it can see
// verbatim in source, so a tone+opacity pair must never be built by concatenation
// (`bg-${tone}/70` silently produces an unstyled, invisible bar).
const BAR_EMPHASIS: Record<NvMetricTone, string> = {
  brand: 'bg-brand',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-destructive',
  neutral: 'bg-brand/30',
}
const BAR_QUIET: Record<NvMetricTone, string> = {
  brand: 'bg-brand/70',
  success: 'bg-success/70',
  warning: 'bg-warning/70',
  danger: 'bg-destructive/70',
  neutral: 'bg-brand/30',
}
function barClass(i: number) {
  // non-current bars sit at a lighter weight so the emphasised bar reads first
  return (i === props.currentIndex ? BAR_EMPHASIS : BAR_QUIET)[barTone(i)]
}
/** Text equivalent of the bar series — the viz itself is pointer-only. */
const barsAriaLabel = computed(() => {
  const unit = props.seriesUnit ?? ''
  const points = (props.series ?? []).map(
    (v, i) => `${props.seriesLabels?.[i] ?? i + 1}: ${v}${unit}`,
  )
  return `${props.label}，${points.length} 期：${points.join('；')}`
})

// --- target progress --------------------------------------------------------
const progressPct = computed(() => Math.max(0, Math.min(100, props.progress ?? 0)))
const progressFill = computed(
  () => metricToneFill[props.progressTone ?? (progressPct.value >= 100 ? 'success' : 'brand')],
)
const markerPct = computed(() => Math.max(0, Math.min(100, props.targetMarker ?? 100)))

// --- breakdown --------------------------------------------------------------
const segTotal = computed(() =>
  Math.max(
    1,
    (props.segments ?? []).reduce((s, seg) => s + seg.value, 0),
  ),
)

// --- alert shell ------------------------------------------------------------
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

// --- viz tooltip builders ---------------------------------------------------
function showPointTip(e: MouseEvent, i: number) {
  const raw = props.series?.[i]
  if (raw == null) return
  tip.move(e, {
    title: props.seriesLabels?.[i],
    rows: [{ label: props.label, value: `${raw}${props.seriesUnit ?? ''}` }],
  })
}
/** Hovered slice — drives the segment ↔ legend linked highlight. */
const hoveredSeg = ref<number | null>(null)
function showSegmentTip(e: MouseEvent, seg: NvMetricSegment, i: number) {
  hoveredSeg.value = i
  const pct = ((seg.value / segTotal.value) * 100).toFixed(1)
  tip.move(e, {
    rows: [
      {
        label: seg.label,
        value: `${seg.value} · ${pct}%`,
        swatchClass: metricToneFill[seg.tone ?? 'neutral'],
      },
    ],
  })
}
function clearSegment() {
  hoveredSeg.value = null
  tip.hide()
}
/** Dim every slice but the pointed-at one (either bar or legend row). */
function segDimmed(i: number) {
  return hoveredSeg.value !== null && hoveredSeg.value !== i
}
function showTargetTip(e: MouseEvent) {
  const rows = [{ label: props.label, value: `${props.value}${props.unit ?? ''}` }]
  if (props.targetLabel)
    rows.push({ label: '目标', value: props.targetLabel.replace(/^目标\s*/, '') })
  rows.push({ label: '达成', value: `${progressPct.value.toFixed(1)}%` })
  tip.move(e, { rows })
}
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
          cn('grid size-11 flex-none place-items-center rounded-[10px]', metricToneTint[tone])
        "
      >
        <component :is="icon" class="size-[21px]" aria-hidden="true" />
      </span>
      <div class="flex min-w-0 flex-col gap-0.5">
        <p class="truncate text-sm text-muted-foreground">{{ label }}</p>
        <p class="text-[22px] font-semibold leading-tight tabular-nums tracking-tight">
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
            'ml-auto inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums',
            metricToneTint[deltaTone],
          )
        "
      >
        <component :is="deltaIcon" class="size-3" aria-hidden="true" />{{ trend.value }}
      </span>
    </div>

    <!-- all other variants: vertical, shared header -->
    <template v-else>
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0">
          <p class="truncate text-sm text-muted-foreground">{{ label }}</p>
          <p
            :class="cn('mt-1.5 text-2xl font-semibold tabular-nums tracking-tight', valueToneClass)"
          >
            {{ value
            }}<span v-if="unit" class="ml-0.5 text-sm font-medium text-muted-foreground">{{
              unit
            }}</span>
          </p>
        </div>

        <span
          v-if="variant === 'alert' && status"
          :class="cn('rounded-full px-2 py-0.5 text-xs font-semibold', metricToneTint[status.tone])"
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
              'inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums',
              metricToneTint[deltaTone],
            )
          "
        >
          <component :is="deltaIcon" class="size-3" aria-hidden="true" />{{ trend.value }}
        </span>
      </div>

      <!-- sparkline -->
      <template v-if="variant === 'sparkline'">
        <NvAreaChart
          v-if="chartData.length > 1"
          minimal
          crosshair
          :data="chartData"
          :height="46"
          :value-suffix="seriesUnit ?? ''"
          class="mt-4"
        />
        <div
          v-if="footStart || footEnd"
          class="mt-2 flex justify-between text-xs text-muted-foreground tabular-nums"
        >
          <span>{{ footStart }}</span
          ><span>{{ footEnd }}</span>
        </div>
      </template>

      <!-- target progress -->
      <template v-else-if="variant === 'target'">
        <div
          class="nv-metric-bar relative mt-4 h-1.5 rounded-full bg-muted"
          role="progressbar"
          :aria-valuenow="progressPct"
          aria-valuemin="0"
          aria-valuemax="100"
          :aria-label="label"
          :aria-valuetext="`${value}${unit ?? ''}${targetLabel ? ` / ${targetLabel}` : ''}，达成 ${progressPct.toFixed(1)}%`"
          @mousemove="showTargetTip"
          @mouseleave="tip.hide"
        >
          <div
            :class="cn('h-full rounded-full', progressFill)"
            :style="{ width: `${progressPct}%` }"
          />
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
      </template>

      <!-- breakdown -->
      <template v-else-if="variant === 'breakdown'">
        <div class="mt-4 flex h-1.5 gap-0.5">
          <span
            v-for="(seg, i) in segments"
            :key="i"
            :class="
              cn(
                'nv-metric-slice block rounded-sm first:rounded-l-full last:rounded-r-full',
                metricToneFill[seg.tone ?? 'neutral'],
                segDimmed(i) && 'nv-metric-dim',
              )
            "
            :style="{ flex: seg.value }"
            @mousemove="(e) => showSegmentTip(e, seg, i)"
            @mouseleave="clearSegment"
          />
        </div>
        <ul class="mt-3 flex flex-wrap gap-x-3.5 gap-y-1.5">
          <li
            v-for="(seg, i) in segments"
            :key="i"
            :class="
              cn(
                'nv-metric-slice inline-flex items-center gap-1.5 text-xs text-muted-foreground',
                segDimmed(i) && 'nv-metric-dim',
              )
            "
            @mousemove="(e) => showSegmentTip(e, seg, i)"
            @mouseleave="clearSegment"
          >
            <span
              :class="cn('size-2 flex-none rounded-sm', metricToneFill[seg.tone ?? 'neutral'])"
            />
            {{ seg.label }}
            <b class="font-semibold text-foreground tabular-nums">{{ seg.value }}</b>
          </li>
        </ul>
      </template>

      <!-- mini bars -->
      <template v-else-if="variant === 'bars'">
        <div
          class="nv-metric-bars mt-4 flex h-[46px] items-end gap-1"
          role="img"
          :aria-label="barsAriaLabel"
        >
          <span
            v-for="(v, i) in series"
            :key="i"
            :class="cn('min-h-1 flex-1 rounded-t-sm', barClass(i))"
            :style="{ height: barHeight(v) }"
            aria-hidden="true"
            @mousemove="(e) => showPointTip(e, i)"
            @mouseleave="tip.hide"
          />
        </div>
        <div
          v-if="footStart || footEnd"
          class="mt-1.5 flex justify-between text-[11px] text-muted-foreground tabular-nums"
        >
          <span>{{ footStart }}</span
          ><span>{{ footEnd }}</span>
        </div>
      </template>

      <!-- facets -->
      <template v-else-if="variant === 'facets'">
        <div class="mt-4 flex flex-wrap gap-1.5">
          <button
            v-for="(f, i) in facets"
            :key="i"
            type="button"
            :class="
              cn(
                'inline-flex items-baseline gap-1.5 rounded-md px-2 py-1 text-xs transition-colors',
                f.tone && f.tone !== 'neutral'
                  ? metricToneTint[f.tone]
                  : 'bg-muted text-muted-foreground hover:bg-muted/70',
              )
            "
            @click="emit('facet', f)"
          >
            {{ f.label }}
            <b
              class="font-semibold tabular-nums"
              :class="f.tone && f.tone !== 'neutral' ? '' : 'text-foreground'"
              >{{ f.value }}</b
            >
          </button>
        </div>
      </template>

      <!-- alert -->
      <template v-else-if="variant === 'alert'">
        <div
          v-if="footStart || action"
          class="mt-3 flex items-center justify-between gap-3 border-t border-border/60 pt-2.5 text-xs text-muted-foreground"
        >
          <span class="min-w-0 truncate">{{ footStart }}</span>
          <a
            v-if="action?.href"
            :href="action.href"
            class="nv-metric-action inline-flex shrink-0 items-center gap-0.5 font-semibold text-brand-strong"
          >
            {{ action.label }}<ChevronRightIcon class="size-3.5" aria-hidden="true" />
          </a>
          <button
            v-else-if="action"
            type="button"
            class="nv-metric-action inline-flex shrink-0 items-center gap-0.5 font-semibold text-brand-strong"
            @click="emit('action')"
          >
            {{ action.label }}<ChevronRightIcon class="size-3.5" aria-hidden="true" />
          </button>
        </div>
      </template>

      <!-- default (back-compat: optional sparkline + deprecated hint) -->
      <template v-else>
        <NvAreaChart
          v-if="chartData.length > 1"
          minimal
          :data="chartData"
          :height="46"
          class="mt-4"
        />
        <p v-if="hint" class="mt-3 text-xs text-muted-foreground">{{ hint }}</p>
      </template>
    </template>

    <!-- shared cursor-following tooltip for the hand-drawn vizzes -->
    <Teleport to="body">
      <div
        v-if="tip.data.value"
        :ref="tip.setEl"
        class="nv-metric-tip pointer-events-none fixed z-50 min-w-32 rounded-lg p-2.5 text-xs"
        :style="{ left: `${tip.pos.value.left}px`, top: `${tip.pos.value.top}px` }"
      >
        <div
          v-if="tip.data.value.title"
          class="mb-1 text-[11px] text-muted-foreground tabular-nums"
        >
          {{ tip.data.value.title }}
        </div>
        <div
          v-for="(row, i) in tip.data.value.rows"
          :key="i"
          class="flex items-baseline justify-between gap-4 tabular-nums"
        >
          <span class="inline-flex items-center text-muted-foreground">
            <span
              v-if="row.swatchClass"
              :class="cn('mr-1.5 size-2 flex-none rounded-sm', row.swatchClass)"
            />
            {{ row.label }}
          </span>
          <b class="font-semibold text-foreground">{{ row.value }}</b>
        </div>
      </div>
    </Teleport>
  </NvCard>
</template>

<style scoped>
@layer nv-components {
  /* Same frosted readout surface the chart crosshair tooltips use (--nv-glass-*),
     so a metric's micro-viz and a full chart read as one system. */
  .nv-metric-tip {
    color: var(--popover-foreground);
    background: var(--nv-glass-bg);
    border: 1px solid var(--nv-glass-border);
    box-shadow: var(--nv-glass-shadow);
    backdrop-filter: var(--nv-glass-filter);
    -webkit-backdrop-filter: var(--nv-glass-filter);
    transition: opacity var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  /* hover any bar → dim the rest so the pointed-at column reads first */
  .nv-metric-bars > span {
    transition: opacity var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  .nv-metric-bars:hover > span {
    opacity: 0.4;
  }
  .nv-metric-bars > span:hover {
    opacity: 1;
  }
  .nv-metric-action:hover {
    text-decoration: underline;
    text-underline-offset: 3px;
  }
  .nv-metric-action:focus-visible {
    outline: 2px solid var(--nv-brand);
    outline-offset: 2px;
    border-radius: 4px;
  }
  /* segment ↔ legend linked highlight: pointing at either dims the other slices */
  .nv-metric-slice {
    transition: opacity var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  .nv-metric-dim {
    opacity: 0.4;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-metric-tip,
    .nv-metric-bars > span,
    .nv-metric-slice {
      transition: none;
    }
  }
}
</style>
