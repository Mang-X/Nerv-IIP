<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — thin radial progress ring (SVG stroke-dasharray). A faint track, a
 * cyan glowing arc that sweeps from 12 o'clock, and a center value + label. The
 * arc length is derived from `value` (0–100); the glow is a restrained
 * drop-shadow. Built on the independent `--sb-*` tokens.
 */
const props = withDefaults(
  defineProps<{
    /** Progress 0–100. */
    value: number
    /** Caption under the value, e.g. 良品率. */
    label: string
    /** Unit after the value, e.g. %. */
    suffix?: string
    /** Outer diameter in px. */
    size?: number
  }>(),
  {
    suffix: '%',
    size: 140,
  },
)

const STROKE = 9
const VB = 120
const R = (VB - STROKE) / 2
const CIRC = 2 * Math.PI * R

const pct = computed(() => Math.max(0, Math.min(100, props.value)))
const dash = computed(() => (pct.value / 100) * CIRC)
/** One number after the point unless the caller passed an integer. */
const shown = computed(() =>
  Number.isInteger(props.value) ? String(props.value) : props.value.toFixed(1),
)

const uid = `rg-${Math.random().toString(36).slice(2, 8)}`
</script>

<template>
  <div class="sb-rg" :style="{ width: `${size}px`, height: `${size}px` }">
    <svg
      class="sb-rg-svg"
      :viewBox="`0 0 ${VB} ${VB}`"
      role="img"
      :aria-label="`${label} ${shown}${suffix}`"
    >
      <circle class="sb-rg-track" :cx="VB / 2" :cy="VB / 2" :r="R" fill="none" :stroke-width="STROKE" />
      <circle
        v-if="pct > 0"
        class="sb-rg-arc"
        :cx="VB / 2"
        :cy="VB / 2"
        :r="R"
        fill="none"
        :stroke-width="STROKE"
        stroke-linecap="round"
        :stroke-dasharray="`${dash} ${CIRC}`"
        :transform="`rotate(-90 ${VB / 2} ${VB / 2})`"
      />
    </svg>
    <div class="sb-rg-c">
      <div class="sb-rg-v">{{ shown }}<small>{{ suffix }}</small></div>
      <div class="sb-rg-l">{{ label }}</div>
    </div>
  </div>
</template>

<style scoped>
.sb-rg {
  position: relative;
  display: inline-grid;
  place-items: center;
  font-variant-numeric: tabular-nums;
}
.sb-rg-svg {
  width: 100%;
  height: 100%;
  overflow: visible;
}
.sb-rg-track {
  stroke: var(--sb-line-2);
}
.sb-rg-arc {
  stroke: var(--sb-cyan);
  filter: drop-shadow(0 0 4px var(--sb-cyan-dim));
  transition: stroke-dasharray 0.6s var(--sb-ease-emphasized);
}
.sb-rg-c {
  position: absolute;
  inset: 0;
  display: grid;
  place-content: center;
  text-align: center;
}
.sb-rg-v {
  font-size: 26px;
  font-weight: 700;
  color: var(--sb-cyan);
  line-height: 1;
  text-shadow: 0 0 16px rgba(0, 229, 255, 0.4);
}
.sb-rg-v small {
  font-size: 14px;
  font-weight: 600;
  margin-left: 1px;
}
.sb-rg-l {
  margin-top: 6px;
  font-size: 12px;
  color: var(--sb-muted);
}
@media (prefers-reduced-motion: reduce) {
  .sb-rg-arc {
    transition: none;
  }
}
</style>
