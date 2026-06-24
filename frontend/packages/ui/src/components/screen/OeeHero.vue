<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — hero KPI block: a big cyan glowing value with an optional unit, a
 * delta line (green when up, red when down) and a faint sparkline + area along
 * the bottom. Data-driven; the sparkline is normalised to its own min/max so any
 * series fills the band. Built on the independent `--sb-*` tokens.
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
  <div class="sb-oeh">
    <div class="sb-oeh-cap">{{ label }}</div>
    <div class="sb-oeh-row">
      <div class="sb-oeh-val">
        {{ value }}<small v-if="unit">{{ unit }}</small>
      </div>
      <div v-if="delta" class="sb-oeh-delta" :class="{ down: deltaDown }">{{ delta }}</div>
    </div>
    <svg v-if="geom" class="sb-oeh-spark" :viewBox="`0 0 ${W} ${H}`" preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <linearGradient :id="`${uid}-fill`" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="var(--sb-cyan)" stop-opacity=".25" />
          <stop offset="1" stop-color="var(--sb-cyan)" stop-opacity="0" />
        </linearGradient>
        <filter :id="`${uid}-glow`" x="-5%" y="-40%" width="110%" height="180%">
          <feGaussianBlur stdDeviation="2.2" result="b" />
          <feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge>
        </filter>
      </defs>
      <path :d="geom.area" :fill="`url(#${uid}-fill)`" />
      <path :d="geom.line" fill="none" stroke="var(--sb-cyan)" stroke-width="2" :filter="`url(#${uid}-glow)`" />
    </svg>
  </div>
</template>

<style scoped>
.sb-oeh {
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.sb-oeh-cap {
  font-size: 16px;
  font-weight: 500;
  color: var(--sb-text-2);
  margin-bottom: 12px;
}
.sb-oeh-row {
  display: flex;
  align-items: baseline;
}
.sb-oeh-val {
  font-size: 54px;
  font-weight: 700;
  line-height: 1;
  letter-spacing: -0.01em;
  color: var(--sb-cyan);
  text-shadow: 0 0 24px rgba(0, 229, 255, 0.45);
}
.sb-oeh-val small {
  font-size: 25px;
  font-weight: 600;
}
.sb-oeh-delta {
  margin-left: 13px;
  font-size: 14px;
  color: var(--sb-green);
}
.sb-oeh-delta.down {
  color: var(--sb-red);
}
.sb-oeh-spark {
  width: 100%;
  height: 92px;
  margin-top: 8px;
  overflow: visible;
}
</style>
