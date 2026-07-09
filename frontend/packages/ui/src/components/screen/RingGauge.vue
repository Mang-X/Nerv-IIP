<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — thin radial progress ring (SVG stroke-dasharray). A faint track, a
 * cyan glowing arc that sweeps from 12 o'clock, and a center value + label. The
 * arc length is derived from `value` (0–100); the glow is a restrained
 * drop-shadow. Built on the independent `--nv-scr-*` tokens.
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
    /** Center value font-size in px — bump for hero rings so大环大字一致. */
    valueSize?: number
  }>(),
  {
    suffix: '%',
    size: 140,
    valueSize: 28,
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
  <div class="nv-scr-rg" :style="{ width: `${size}px`, height: `${size}px` }">
    <svg
      class="nv-scr-rg-svg"
      :viewBox="`0 0 ${VB} ${VB}`"
      role="img"
      :aria-label="`${label} ${shown}${suffix}`"
    >
      <circle
        class="nv-scr-rg-track"
        :cx="VB / 2"
        :cy="VB / 2"
        :r="R"
        fill="none"
        :stroke-width="STROKE"
      />
      <circle
        v-if="pct > 0"
        class="nv-scr-rg-arc"
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
    <div class="nv-scr-rg-c">
      <div class="nv-scr-rg-v" :style="{ fontSize: `${valueSize}px` }">
        {{ shown
        }}<small :style="{ fontSize: `${Math.round(valueSize / 2)}px` }">{{ suffix }}</small>
      </div>
      <div class="nv-scr-rg-l">{{ label }}</div>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-rg {
    position: relative;
    display: inline-grid;
    place-items: center;
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-rg-svg {
    width: 100%;
    height: 100%;
    overflow: visible;
  }
  .nv-scr-rg-track {
    stroke: var(--nv-scr-line-2);
  }
  .nv-scr-rg-arc {
    stroke: var(--nv-scr-cyan);
    filter: drop-shadow(0 0 4px var(--nv-scr-cyan-dim));
    transition: stroke-dasharray 0.6s var(--nv-scr-ease-emphasized);
  }
  .nv-scr-rg-c {
    position: absolute;
    inset: 0;
    display: grid;
    place-content: center;
    text-align: center;
  }
  .nv-scr-rg-v {
    font-size: 28px;
    font-weight: 700;
    color: #fff;
    line-height: 1;
    text-shadow: var(--nv-scr-value-glow);
  }
  .nv-scr-rg-v small {
    font-size: 14px;
    font-weight: 600;
    margin-left: 1px;
  }
  .nv-scr-rg-l {
    margin-top: 6px;
    font-size: 12px;
    color: var(--nv-scr-muted);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-rg-arc {
      transition: none;
    }
  }
}
</style>
