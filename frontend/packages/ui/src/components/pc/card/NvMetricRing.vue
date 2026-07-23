<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'
import NvCard from './NvCard.vue'
import { metricToneStroke, type NvMetricFactor, type NvMetricTone } from './metric'
import { useMetricTooltip } from './useMetricTooltip'

/**
 * Pro — a ratio KPI whose constituent factors sit beside the gauge, so a rate
 * (OEE, 齐套率, 库位利用率) shows its result and its causes together: the ring
 * is the outcome, the rows are what to go check. Composes NvCard; the arc reads
 * a tone token so it re-tints with the brand. Hover the ring for the full split.
 */
const props = withDefaults(
  defineProps<{
    label: string
    /** Centre readout, pre-formatted (e.g. `82.4%`). */
    value: string | number
    /** Arc sweep 0–100. */
    percent: number
    tone?: Extract<NvMetricTone, 'brand' | 'success' | 'warning' | 'danger'>
    /** Constituent rows shown to the right of the gauge. */
    factors?: NvMetricFactor[]
    class?: HTMLAttributes['class']
  }>(),
  { tone: 'brand', factors: () => [] },
)

const R = 36
const CIRC = 2 * Math.PI * R
const dash = computed(() => {
  const p = Math.max(0, Math.min(100, props.percent))
  return `${(p / 100) * CIRC} ${CIRC}`
})

const tip = useMetricTooltip()
function showTip(e: MouseEvent) {
  tip.move(e, {
    title: props.label,
    rows: [
      { label: '当前', value: String(props.value) },
      ...props.factors.map((f) => ({ label: f.label, value: String(f.value) })),
    ],
  })
}
</script>

<template>
  <NvCard :class="cn('overflow-hidden p-5', props.class)">
    <div class="flex items-center gap-[18px]">
      <div class="relative size-[84px] flex-none" @mousemove="showTip" @mouseleave="tip.hide">
        <svg class="size-full -rotate-90" viewBox="0 0 84 84">
          <circle cx="42" cy="42" :r="R" fill="none" stroke="var(--muted)" stroke-width="8" />
          <circle
            cx="42"
            cy="42"
            :r="R"
            fill="none"
            :stroke="metricToneStroke[tone]"
            stroke-width="8"
            stroke-linecap="round"
            :stroke-dasharray="dash"
            class="nv-ring-arc"
          />
        </svg>
        <span
          class="absolute inset-0 grid place-items-center text-[17px] font-semibold tabular-nums tracking-tight"
        >
          {{ value }}
        </span>
      </div>

      <div class="flex min-w-0 flex-1 flex-col gap-2">
        <p class="truncate text-sm text-muted-foreground">{{ label }}</p>
        <dl class="flex flex-col gap-1">
          <div
            v-for="(f, i) in factors"
            :key="i"
            class="flex justify-between gap-3 text-xs text-muted-foreground"
          >
            <dt class="truncate">{{ f.label }}</dt>
            <dd class="shrink-0 font-semibold text-foreground tabular-nums">{{ f.value }}</dd>
          </div>
        </dl>
      </div>
    </div>

    <Teleport to="body">
      <div
        v-if="tip.data.value"
        :ref="tip.setEl"
        class="nv-metric-tip pointer-events-none fixed z-50 min-w-32 rounded-lg p-2.5 text-xs"
        :style="{ left: `${tip.pos.value.left}px`, top: `${tip.pos.value.top}px` }"
      >
        <div v-if="tip.data.value.title" class="mb-1 text-[11px] text-muted-foreground">
          {{ tip.data.value.title }}
        </div>
        <div
          v-for="(row, i) in tip.data.value.rows"
          :key="i"
          class="flex items-baseline justify-between gap-4 tabular-nums"
        >
          <span class="text-muted-foreground">{{ row.label }}</span>
          <b class="font-semibold text-foreground">{{ row.value }}</b>
        </div>
      </div>
    </Teleport>
  </NvCard>
</template>

<style scoped>
@layer nv-components {
  .nv-ring-arc {
    transition: stroke-dasharray var(--nv-duration-slow, 320ms) var(--nv-ease-out-quart, ease-out);
  }
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
  @media (prefers-reduced-motion: reduce) {
    .nv-ring-arc,
    .nv-metric-tip {
      transition: none;
    }
  }
}
</style>
