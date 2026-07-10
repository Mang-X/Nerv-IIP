<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — hero KPI block: a big cyan glowing value with an optional unit, a
 * delta line (green when up, red when down) and a faint sparkline + area along
 * the bottom. Data-driven; the sparkline is normalised to its own min/max so any
 * series fills the band. Built on the independent `--nv-scr-*` tokens.
 */
const props = withDefaults(
  defineProps<{
    /** Caption above the value, e.g. 设备综合效率 OEE. */
    label: string
    /** Headline number, e.g. 92.4. */
    value: number | string
    /** Small unit after the value, e.g. %. */
    unit?: string
    /** Change vs. baseline, e.g. 较昨日 +2.7%. Leading +/- drives the color. */
    delta?: string
    /** Series for the bottom sparkline. */
    spark?: number[]
  }>(),
  {
    spark: () => [62, 58, 64, 60, 70, 66, 78, 74, 86, 80, 92],
  },
)

const W = 460
const H = 96

/** Down only when the delta string carries a leading minus. */
const deltaDown = computed(() => /^\s*[-−]/.test(props.delta ?? ''))

const geom = computed(() => {
  const d = props.spark
  if (!d || d.length < 2) return null
  const min = Math.min(...d)
  const max = Math.max(...d)
  const span = max - min || 1
  const stepX = W / (d.length - 1)
  // leave 14px headroom top, 12px floor bottom
  const y = (v: number) => 14 + (1 - (v - min) / span) * (H - 26)
  const pts = d.map((v, i) => `${(i * stepX).toFixed(1)} ${y(v).toFixed(1)}`)
  return {
    line: `M${pts.join(' L')}`,
    area: `M${pts.join(' L')} L${W} ${H} L0 ${H} Z`,
  }
})

const uid = `oeh-${Math.random().toString(36).slice(2, 8)}`
</script>

<template>
  <div class="nv-scr-oeh">
    <div class="nv-scr-oeh-cap">{{ label }}</div>
    <div class="nv-scr-oeh-row">
      <div class="nv-scr-oeh-val">
        {{ value }}<small v-if="unit">{{ unit }}</small>
      </div>
      <div v-if="delta" class="nv-scr-oeh-delta" :class="{ down: deltaDown }">{{ delta }}</div>
    </div>
    <svg
      v-if="geom"
      class="nv-scr-oeh-spark"
      :viewBox="`0 0 ${W} ${H}`"
      preserveAspectRatio="none"
      aria-hidden="true"
    >
      <defs>
        <linearGradient :id="`${uid}-fill`" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="var(--nv-scr-cyan)" stop-opacity=".25" />
          <stop offset="1" stop-color="var(--nv-scr-cyan)" stop-opacity="0" />
        </linearGradient>
      </defs>
      <path :d="geom.area" :fill="`url(#${uid}-fill)`" />
      <path
        class="nv-scr-oeh-line"
        :d="geom.line"
        fill="none"
        stroke="var(--nv-scr-cyan)"
        stroke-width="1.5"
        stroke-linecap="round"
        stroke-linejoin="round"
        vector-effect="non-scaling-stroke"
      />
    </svg>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-oeh {
    color: var(--nv-scr-text);
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-oeh-cap {
    font-size: 16px;
    font-weight: 500;
    color: var(--nv-scr-text-2);
    margin-bottom: 12px;
  }
  .nv-scr-oeh-row {
    display: flex;
    align-items: baseline;
  }
  .nv-scr-oeh-val {
    font-size: 54px;
    font-weight: 700;
    line-height: 1;
    letter-spacing: -0.01em;
    /* white number with only a whisper of glow — accent stays off the big figure */
    color: #fff;
    text-shadow: var(--nv-scr-value-glow);
  }
  .nv-scr-oeh-val small {
    font-size: 25px;
    font-weight: 600;
  }
  .nv-scr-oeh-delta {
    margin-left: 13px;
    font-size: 14px;
    color: var(--nv-scr-green);
  }
  .nv-scr-oeh-delta.down {
    color: var(--nv-scr-red);
  }
  .nv-scr-oeh-spark {
    width: 100%;
    height: 92px;
    margin-top: 8px;
    overflow: visible;
  }
  .nv-scr-oeh-line {
    filter: drop-shadow(0 0 3px var(--nv-scr-cyan-dim));
  }
}
</style>
