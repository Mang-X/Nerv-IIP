<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import { computed } from 'vue'

/**
 * Screen — liquid-level gauge. A rounded container, a clipped cyan fill whose
 * height tracks `value` (0–100), and two offset wave crests that drift sideways
 * (animateTransform). The percentage reads at center. Built on the independent
 * `--sb-*` tokens. SMIL can't be paused from CSS, so under reduced-motion the
 * drift animations are simply not rendered (the level + waves stay static).
 */
const props = withDefaults(
  defineProps<{
    /** Fill level 0–100. */
    value: number
    /** Caption under the gauge, e.g. 原料罐 #2. */
    label?: string
  }>(),
  {},
)

const reduce = useMediaQuery('(prefers-reduced-motion: reduce)')

const VW = 130
const VH = 150
const PAD = 5
const innerH = VH - PAD * 2

const pct = computed(() => Math.max(0, Math.min(100, props.value)))
/** Top edge (svg y) of the liquid surface. */
const surfaceY = computed(() => PAD + (1 - pct.value / 100) * innerH)
const shown = computed(() =>
  Number.isInteger(props.value) ? String(props.value) : props.value.toFixed(1),
)

/** Crests tiled from one width left across ~4× the width, so a +VW drift loops
 *  seamlessly. */
const wavePath = computed(() => {
  const a = 5 // amplitude
  const w = VW // half-period chunk
  const y = surfaceY.value
  return (
    `M${-VW} ${y}`
    + ` q ${w / 2} ${-a} ${w} 0 t ${w} 0 t ${w} 0 t ${w} 0`
    + ` V${VH} H${-VW} Z`
  )
})

const uid = `wl-${Math.random().toString(36).slice(2, 8)}`
</script>

<template>
  <div class="sb-wl">
    <svg
      class="sb-wl-svg"
      :viewBox="`0 0 ${VW} ${VH}`"
      role="img"
      :aria-label="`${label ?? '水位'} ${shown}%`"
    >
      <defs>
        <linearGradient :id="`${uid}-fill`" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="var(--sb-cyan)" stop-opacity=".55" />
          <stop offset="1" stop-color="var(--sb-cyan)" stop-opacity=".12" />
        </linearGradient>
        <clipPath :id="`${uid}-clip`">
          <rect :x="PAD" :y="PAD" :width="VW - PAD * 2" :height="innerH" :rx="14" />
        </clipPath>
      </defs>

      <!-- container track -->
      <rect
        class="sb-wl-track"
        :x="PAD"
        :y="PAD"
        :width="VW - PAD * 2"
        :height="innerH"
        :rx="14"
        fill="none"
      />

      <g :clip-path="`url(#${uid}-clip)`">
        <!-- back wave (slower, fainter) -->
        <path :d="wavePath" :fill="`url(#${uid}-fill)`">
          <animateTransform
            v-if="!reduce"
            attributeName="transform"
            type="translate"
            from="0 0"
            :to="`${VW} 0`"
            dur="7s"
            repeatCount="indefinite"
          />
        </path>
        <!-- front wave -->
        <path :d="wavePath" fill="var(--sb-cyan)" fill-opacity=".32">
          <animateTransform
            v-if="!reduce"
            attributeName="transform"
            type="translate"
            from="0 0"
            :to="`${VW} 0`"
            dur="4.5s"
            repeatCount="indefinite"
          />
        </path>
      </g>

      <!-- surface highlight line -->
      <line
        class="sb-wl-surface"
        :x1="PAD + 2"
        :x2="VW - PAD - 2"
        :y1="surfaceY"
        :y2="surfaceY"
      />
    </svg>
    <div class="sb-wl-pct">{{ shown }}<small>%</small></div>
    <div v-if="label" class="sb-wl-label">{{ label }}</div>
  </div>
</template>

<style scoped>
.sb-wl {
  position: relative;
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  font-variant-numeric: tabular-nums;
}
.sb-wl-svg {
  width: 130px;
  height: 150px;
  overflow: hidden;
}
.sb-wl-track {
  stroke: var(--sb-line-2);
  stroke-width: 1.5;
}
.sb-wl-surface {
  stroke: var(--sb-cyan);
  stroke-width: 1.5;
  opacity: 0.7;
  filter: drop-shadow(0 0 5px var(--sb-cyan-dim));
}
.sb-wl-pct {
  position: absolute;
  top: 0;
  left: 0;
  width: 130px;
  height: 150px;
  display: grid;
  place-content: center;
  font-size: 30px;
  font-weight: 700;
  color: var(--sb-text);
  text-shadow: 0 1px 8px rgba(0, 0, 0, 0.55);
  pointer-events: none;
}
.sb-wl-pct small {
  font-size: 15px;
  font-weight: 600;
  margin-left: 1px;
}
.sb-wl-label {
  margin-top: 8px;
  font-size: 12px;
  color: var(--sb-muted);
}
</style>
