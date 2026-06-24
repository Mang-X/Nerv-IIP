<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — minimal sparkline. A crisp, hairline cyan polyline (non-scaling stroke
 * so it stays 1.5px however the cell stretches) with a whisper of glow and a
 * bright dot on the latest point — precise, not blurry. Optional area fill.
 * Built on the independent `--sb-*` tokens.
 */
const props = withDefaults(
  defineProps<{
    /** Series to plot (need at least two points). */
    data: number[]
    /** Fill the area under the line with a fading gradient. */
    area?: boolean
  }>(),
  {
    area: false,
    data: () => [8, 11, 9, 14, 12, 17, 15, 21, 19, 24, 22, 28],
  },
)

const W = 240
const H = 56

const geom = computed(() => {
  const d = props.data
  if (!d || d.length < 2) return null
  const min = Math.min(...d)
  const max = Math.max(...d)
  const span = max - min || 1
  const stepX = W / (d.length - 1)
  const y = (v: number) => 6 + (1 - (v - min) / span) * (H - 12)
  const pts = d.map((v, i) => `${(i * stepX).toFixed(1)} ${y(v).toFixed(1)}`)
  return {
    line: `M${pts.join(' L')}`,
    area: `M${pts.join(' L')} L${W} ${H} L0 ${H} Z`,
    lastX: (d.length - 1) * stepX,
    lastY: y(d[d.length - 1]),
  }
})

const uid = `sl-${Math.random().toString(36).slice(2, 8)}`
</script>

<template>
  <svg
    v-if="geom"
    class="sb-sl"
    :viewBox="`0 0 ${W} ${H}`"
    preserveAspectRatio="none"
    aria-hidden="true"
  >
    <defs>
      <linearGradient :id="`${uid}-fill`" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" stop-color="var(--sb-cyan)" stop-opacity=".16" />
        <stop offset="1" stop-color="var(--sb-cyan)" stop-opacity="0" />
      </linearGradient>
    </defs>
    <path v-if="area" :d="geom.area" :fill="`url(#${uid}-fill)`" />
    <path
      class="sb-sl-line"
      :d="geom.line"
      fill="none"
      stroke="var(--sb-cyan)"
      stroke-width="1.5"
      stroke-linecap="round"
      stroke-linejoin="round"
      vector-effect="non-scaling-stroke"
    />
  </svg>
  <svg
    v-else
    class="sb-sl"
    :viewBox="`0 0 ${W} ${H}`"
    preserveAspectRatio="none"
    aria-hidden="true"
  >
    <line :x1="0" :y1="H / 2" :x2="W" :y2="H / 2" stroke="var(--sb-faint)" stroke-width="1" stroke-dasharray="3 4" vector-effect="non-scaling-stroke" />
  </svg>
</template>

<style scoped>
.sb-sl {
  display: block;
  width: 100%;
  height: 100%;
  overflow: visible;
}
/* crisp hairline with just a whisper of glow — no heavy blur */
.sb-sl-line {
  filter: drop-shadow(0 0 2.5px var(--sb-cyan-dim));
}
</style>
