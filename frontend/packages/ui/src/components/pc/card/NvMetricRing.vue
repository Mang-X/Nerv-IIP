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
    /** Centre readout — the figure that matters most, usually the total. */
    value: string | number
    unit?: string
    /** Small caption under the centre figure, e.g. `总计` / `在制`. */
    centerCaption?: string
    /** Slices of the whole; each gets an arc, a legend row and a share. */
    segments?: NvMetricSegment[]
    class?: HTMLAttributes['class']
  }>(),
  { segments: () => [] },
)

const R = 36
const CIRC = 2 * Math.PI * R
/** Circumference eaten by the gap between adjacent slices. */
const GAP = 2.5

const total = computed(() =>
  Math.max(
    1,
    props.segments.reduce((sum, s) => sum + s.value, 0),
  ),
)

/** Arc geometry per slice: proportional length, cumulative start offset. */
const arcs = computed(() => {
  let offset = 0
  return props.segments.map((seg) => {
    const span = (seg.value / total.value) * CIRC
    const length = Math.max(0, span - GAP)
    const arc = {
      seg,
      dasharray: `${length} ${CIRC - length}`,
      dashoffset: -offset,
      stroke: metricToneStroke[seg.tone ?? 'neutral'],
    }
    offset += span
    return arc
  })
})

const hovered = ref<number | null>(null)
const dimmed = (i: number) => hovered.value !== null && hovered.value !== i

function share(seg: NvMetricSegment) {
  return ((seg.value / total.value) * 100).toFixed(1)
}

/** The centre follows the pointer: hovering a slice reads that slice instead. */
const centerValue = computed(() => {
  const seg = hovered.value === null ? null : props.segments[hovered.value]
  return seg ? String(seg.value) : `${props.value}${props.unit ?? ''}`
})
const centerCaptionText = computed(() => {
  const seg = hovered.value === null ? null : props.segments[hovered.value]
  return seg ? `${seg.label} · ${share(seg)}%` : props.centerCaption
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
            :key="i"
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

        <span class="nv-ring-center absolute inset-0 grid place-items-center px-2 text-center">
          <span>
            <span class="nv-ring-figure block truncate font-semibold tabular-nums tracking-tight">
              {{ centerValue }}
            </span>
            <span
              v-if="centerCaptionText"
              class="mt-0.5 block truncate text-[10px] text-muted-foreground"
            >
              {{ centerCaptionText }}
            </span>
          </span>
        </span>
      </div>

      <ul class="flex min-w-0 flex-1 flex-col justify-center gap-1.5">
        <li
          v-for="(seg, i) in segments"
          :key="i"
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
  .nv-ring-figure {
    font-size: 17px;
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
