<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — minimal sparkline. A glowing cyan polyline normalised to its own
 * min/max, with an optional area gradient beneath. Stretches to its container
 * (preserveAspectRatio none) so it drops into any cell. Built on the independent
 * `--sb-*` tokens.
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
        <stop offset="0" stop-color="var(--sb-cyan)" stop-opacity=".22" />
        <stop offset="1" stop-color="var(--sb-cyan)" stop-opacity="0" />
      </linearGradient>
      <filter :id="`${uid}-glow`" x="-5%" y="-50%" width="110%" height="200%">
        <feGaussianBlur stdDeviation="1.8" result="b" />
        <feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge>
      </filter>
    </defs>
    <path v-if="area" :d="geom.area" :fill="`url(#${uid}-fill)`" />
    <path
      :d="geom.line"
      fill="none"
      stroke="var(--sb-cyan)"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      :filter="`url(#${uid}-glow)`"
    />
  </svg>
  <svg
    v-else
    class="sb-sl"
    :viewBox="`0 0 ${W} ${H}`"
    preserveAspectRatio="none"
    aria-hidden="true"
  >
    <line :x1="0" :y1="H / 2" :x2="W" :y2="H / 2" stroke="var(--sb-faint)" stroke-width="1" stroke-dasharray="3 4" />
  </svg>
</template>

<style scoped>
.sb-sl {
  display: block;
  width: 100%;
  height: 100%;
  overflow: visible;
}
</style>
