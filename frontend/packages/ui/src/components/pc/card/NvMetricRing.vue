<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref } from 'vue'
import { cn } from '../../../lib/utils'
import NvCard from './NvCard.vue'
import { metricToneFill, metricToneStroke, type NvMetricSegment } from './metric'

/**
 * Pro — the circular form of a composition: one coloured arc per slice, each
 * with its legend row, and the number that matters most held in the middle.
 * Pointing at an arc or its legend row lights that slice, dims the rest and
 * swaps the centre to that slice's own reading, so the ring answers "how much
 * of the whole is this?" without a tooltip covering the card it belongs to.
 *
 * Use it for parts of a whole (工单状态 / 齐套 / 库位). A rate whose factors
 * *multiply* rather than sum — OEE = A×P×Q — is not a composition and must not
 * be drawn as one; give those `NvMetricCard variant="facets"` or their own rows.
 */
const props = withDefaults(
  defineProps<{
    label: string
    /**
     * Centre readout — the figure that matters most, usually the total. Kept
     * unit-free: the ring's inner clearance is only ~58px, so a 4–5 digit total
     * plus a unit would truncate. Units live in the caption and the legend rows.
     */
    value: string | number
    /** Small caption under the centre figure, e.g. `总计 · 单` / `总库位`. */
    centerCaption?: string
    /** Slices of the whole; each gets an arc, a legend row and a share. */
    segments?: NvMetricSegment[]
    class?: HTMLAttributes['class']
  }>(),
  { segments: () => [] },
)

const R = 36
const CIRC = 2 * Math.PI * R
/** Gap after each drawn slice (shrinks adaptively when slices are many). */
const GAP = 2.5
/** Minimum drawn arc for a non-zero slice, so the 1-in-1000 anomaly still reads. */
const MIN_ARC = 3

/** Only finite, positive values contribute to the geometry; the rest are 0. */
function safeValue(v: number) {
  return Number.isFinite(v) && v > 0 ? v : 0
}
/** Denominator = the actual sum of positive values (never a forced `max(1,…)`). */
const positiveTotal = computed(() => props.segments.reduce((s, seg) => s + safeValue(seg.value), 0))

/**
 * Arc geometry — drawn lengths + gaps CONSERVE the full circumference and never
 * overlap, regardless of slice order, and the share denominator is the true sum
 * of positive values (fractional / negative / non-finite inputs don't distort
 * it). A naive `span - GAP` breaks two ways — a sub-gap slice drops to zero, and
 * a floored tiny slice gets overdrawn when it isn't last (offset advanced by the
 * true span). Instead: sanitise inputs, fail closed on zero total, shrink the
 * gap so the per-slice MIN_ARC floor keeps fitting as the count rises, give each
 * non-zero slice its floor + a value-weighted share of the remainder, and
 * advance the offset by each slice's OWN drawn arc + gap.
 *
 * Capacity: MIN_ARC is guaranteed while `n × MIN_ARC ≤ circumference` (~75
 * slices); beyond that every non-zero slice still draws a proportional, non-zero
 * arc (never vanishes) but below MIN_ARC — aggregate a tiny tail category for a
 * readable ring at that density.
 */
const arcs = computed(() => {
  const values = props.segments.map((seg) => safeValue(seg.value))
  const denom = values.reduce((a, b) => a + b, 0)
  const nonZero = values.reduce((n, v) => n + (v > 0 ? 1 : 0), 0)
  const track = (seg: NvMetricSegment) => ({
    seg,
    dasharray: `0 ${CIRC}`,
    dashoffset: 0,
    stroke: metricToneStroke[seg.tone ?? 'neutral'],
  })
  // fail closed: no positive data → draw only the muted track
  if (denom <= 0 || nonZero === 0) return props.segments.map(track)

  // adaptive gap: reserve the per-slice floors first, split what's left as gaps,
  // capped at GAP — so crowded rings shrink the gap instead of eating the floor
  const gap = Math.min(GAP, Math.max(0, (CIRC - nonZero * MIN_ARC) / nonZero))
  const arcBudget = Math.max(0, CIRC - nonZero * gap)
  const floorTotal = nonZero * MIN_ARC
  const floorFits = arcBudget >= floorTotal
  const extra = Math.max(0, arcBudget - floorTotal)

  let offset = 0
  return props.segments.map((seg, i) => {
    const v = values[i]
    let length = 0
    if (v > 0) {
      const frac = v / denom
      length = floorFits ? MIN_ARC + extra * frac : arcBudget * frac
    }
    const arc = {
      seg,
      dasharray: `${length} ${CIRC - length}`,
      dashoffset: -offset,
      stroke: metricToneStroke[seg.tone ?? 'neutral'],
    }
    offset += length + (v > 0 ? gap : 0)
    return arc
  })
})

const hovered = ref<number | null>(null)
const dimmed = (i: number) => hovered.value !== null && hovered.value !== i

function share(seg: NvMetricSegment) {
  const denom = positiveTotal.value
  return denom > 0 ? ((safeValue(seg.value) / denom) * 100).toFixed(1) : '0.0'
}

/** The centre follows the pointer: hovering a slice reads that slice instead. */
const centerValue = computed(() => {
  const seg = hovered.value === null ? null : props.segments[hovered.value]
  return seg ? String(seg.value) : String(props.value)
})
/**
 * On hover the caption stays short — just the share. Which slice it is, is
 * already said twice over by the lit arc and the undimmed legend row, and the
 * ring's inner clearance is far too small to hold a label as well.
 */
const centerCaptionText = computed(() => {
  const seg = hovered.value === null ? null : props.segments[hovered.value]
  return seg ? `${share(seg)}%` : props.centerCaption
})
</script>

<template>
  <NvCard :class="cn('nv-ring-card overflow-hidden p-5', props.class)">
    <p class="truncate text-sm text-muted-foreground">{{ label }}</p>

    <div class="nv-ring-layout mt-3 flex gap-[18px]">
      <div class="nv-ring relative flex-none">
        <svg class="size-full -rotate-90" viewBox="0 0 84 84" aria-hidden="true">
          <circle cx="42" cy="42" :r="R" fill="none" stroke="var(--muted)" stroke-width="8" />
          <circle
            v-for="(arc, i) in arcs"
            :key="arc.seg.key ?? i"
            cx="42"
            cy="42"
            :r="R"
            fill="none"
            :stroke="arc.stroke"
            stroke-width="8"
            :stroke-dasharray="arc.dasharray"
            :stroke-dashoffset="arc.dashoffset"
            :class="cn('nv-ring-seg', dimmed(i) && 'nv-ring-dim')"
            @mouseenter="hovered = i"
            @mouseleave="hovered = null"
          />
        </svg>

        <!-- pointer-events-none is load-bearing: this readout covers the whole
             ring, so without it the arcs beneath never receive hover at all. -->
        <span
          class="nv-ring-center pointer-events-none absolute inset-0 grid place-items-center px-2 text-center"
        >
          <span>
            <span class="nv-ring-figure block truncate font-semibold tabular-nums tracking-tight">
              {{ centerValue }}
            </span>
            <span
              v-if="centerCaptionText"
              class="mt-px block truncate text-[10px] leading-tight text-muted-foreground"
            >
              {{ centerCaptionText }}
            </span>
          </span>
        </span>
      </div>

      <ul class="flex min-w-0 flex-1 flex-col justify-center gap-1.5">
        <li
          v-for="(seg, i) in segments"
          :key="seg.key ?? i"
          :class="cn('nv-ring-row flex items-center gap-2 text-xs', dimmed(i) && 'nv-ring-dim')"
          @mouseenter="hovered = i"
          @mouseleave="hovered = null"
        >
          <span :class="cn('size-2 flex-none rounded-sm', metricToneFill[seg.tone ?? 'neutral'])" />
          <span class="min-w-0 flex-1 truncate text-muted-foreground">{{ seg.label }}</span>
          <b class="shrink-0 font-semibold tabular-nums">{{ seg.value }}</b>
          <span class="w-11 shrink-0 text-right tabular-nums text-muted-foreground">
            {{ share(seg) }}%
          </span>
        </li>
      </ul>
    </div>
  </NvCard>
</template>

<style scoped>
@layer nv-components {
  /* Size, layout and display are declared here rather than as Tailwind utilities
     so the container query below can win — utilities sit in a later layer. */
  .nv-ring-card {
    container-type: inline-size;
  }
  .nv-ring-layout {
    align-items: center;
  }
  .nv-ring {
    width: 84px;
    height: 84px;
  }
  /* Bound the readout to the ring's inner clearance (84 − 2×11px hover stroke),
     otherwise `truncate` has no width to work against and a long caption or
     value spills straight over the arc. */
  .nv-ring-center > span {
    max-width: 58px;
  }
  .nv-ring-figure {
    font-size: 17px;
    /* the readout inherits the host's body leading (~1.6), which leaves a band of
       empty line-box under the digits and pushes the caption away — the two lines
       belong together as one reading, so set the leading tight here. */
    line-height: 1.05;
  }

  /* Hover emphasis: the pointed-at arc thickens while the rest recede, matching
     the breakdown bar's linked highlight so both forms of a composition behave
     the same way. stroke-width/opacity are safe to own — no utility sets them. */
  .nv-ring-seg {
    transition:
      stroke-width var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out),
      opacity var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  .nv-ring-seg:hover {
    stroke-width: 11;
  }
  .nv-ring-row {
    transition: opacity var(--nv-duration-fast, 150ms) var(--nv-ease-out-quart, ease-out);
  }
  .nv-ring-dim {
    opacity: 0.35;
  }

  /* Too narrow to seat the ring beside its legend — stack, so the legend rows
     keep the full card width instead of truncating to a single character. */
  @container (max-width: 248px) {
    .nv-ring-layout {
      flex-direction: column;
      align-items: flex-start;
      gap: 14px;
    }
    .nv-ring-layout > ul {
      width: 100%;
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-ring-seg,
    .nv-ring-row {
      transition: none;
    }
  }
}
</style>
