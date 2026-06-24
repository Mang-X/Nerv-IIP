<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import { computed } from 'vue'

/**
 * Screen — liquid-level ball. A glass sphere with a clipped cyan fill whose
 * surface tracks `value` (0–100); two offset wave crests drift sideways
 * (animateTransform), a rim ring + soft top highlight give it the gauge-ball
 * look. The percentage reads at center. Built on the independent `--sb-*` tokens.
 * SMIL can't be paused from CSS, so under reduced-motion the drift isn't rendered
 * (the level + waves stay static).
 */
const props = withDefaults(
  defineProps<{
    /** Fill level 0–100. */
    value: number
    /** Caption under the ball, e.g. 原料罐 #2. */
    label?: string
  }>(),
  {},
)

const reduce = useMediaQuery('(prefers-reduced-motion: reduce)')

const SIZE = 150
const C = SIZE / 2 // center x/y
const R = 64 // ball radius
const TOP = C - R // y of ball top
const BOT = C + R // y of ball bottom

const pct = computed(() => Math.max(0, Math.min(100, props.value)))
/** Liquid surface y inside the circle. */
const surfaceY = computed(() => TOP + (1 - pct.value / 100) * (R * 2))
const shown = computed(() =>
  Number.isInteger(props.value) ? String(props.value) : props.value.toFixed(1),
)

/** Wave crest tiled across ~4R so a +2R drift loops seamlessly. */
const wavePath = computed(() => {
  const a = 5 // amplitude
  const w = R // half-period chunk
  const y = surfaceY.value
  return (
    `M${C - 2 * R} ${y}`
    + ` q ${w / 2} ${-a} ${w} 0 t ${w} 0 t ${w} 0 t ${w} 0 t ${w} 0 t ${w} 0 t ${w} 0 t ${w} 0`
    + ` V${BOT} H${C - 2 * R} Z`
  )
})

const uid = `wl-${Math.random().toString(36).slice(2, 8)}`
</script>

<template>
  <div class="sb-wl">
    <svg
      class="sb-wl-svg"
      :viewBox="`0 0 ${SIZE} ${SIZE}`"
      role="img"
      :aria-label="`${label ?? '水位'} ${shown}%`"
    >
      <defs>
        <linearGradient :id="`${uid}-fill`" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="var(--sb-cyan)" stop-opacity=".5" />
          <stop offset="1" stop-color="var(--sb-cyan)" stop-opacity=".14" />
        </linearGradient>
        <clipPath :id="`${uid}-clip`">
          <circle :cx="C" :cy="C" :r="R" />
        </clipPath>
        <radialGradient :id="`${uid}-shine`" cx="0.36" cy="0.3" r="0.7">
          <stop offset="0" stop-color="#ffffff" stop-opacity=".2" />
          <stop offset="0.55" stop-color="#ffffff" stop-opacity="0" />
        </radialGradient>
      </defs>

      <!-- glass base -->
      <circle :cx="C" :cy="C" :r="R" fill="rgba(255, 255, 255, 0.02)" />

      <g :clip-path="`url(#${uid}-clip)`">
        <!-- back wave (slower, fainter) -->
        <path :d="wavePath" :fill="`url(#${uid}-fill)`">
          <animateTransform
            v-if="!reduce"
            attributeName="transform"
            type="translate"
            from="0 0"
            :to="`${2 * R} 0`"
            dur="7s"
            repeatCount="indefinite"
          />
        </path>
        <!-- front wave -->
        <path :d="wavePath" fill="var(--sb-cyan)" fill-opacity=".3">
          <animateTransform
            v-if="!reduce"
            attributeName="transform"
            type="translate"
            from="0 0"
            :to="`${2 * R} 0`"
            dur="4.5s"
            repeatCount="indefinite"
          />
        </path>
      </g>

      <!-- top glass highlight + rim -->
      <circle :cx="C" :cy="C" :r="R" :fill="`url(#${uid}-shine)`" />
      <circle class="sb-wl-rim" :cx="C" :cy="C" :r="R" fill="none" />
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
  width: 150px;
  height: 150px;
}
.sb-wl-rim {
  stroke: var(--sb-cyan);
  stroke-width: 1.5;
  opacity: 0.55;
  filter: drop-shadow(0 0 6px var(--sb-cyan-dim));
}
.sb-wl-pct {
  position: absolute;
  top: 0;
  left: 0;
  width: 150px;
  height: 150px;
  display: grid;
  place-content: center;
  font-size: 30px;
  font-weight: 700;
  color: var(--sb-text);
  text-shadow: 0 1px 8px rgba(0, 0, 0, 0.6);
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
