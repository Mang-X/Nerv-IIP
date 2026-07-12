<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — minimal sparkline. A crisp, hairline cyan polyline (non-scaling stroke
 * so it stays 1.5px however the cell stretches) with a whisper of glow and a
 * bright dot on the latest point — precise, not blurry. Optional area fill.
 * Built on the independent `--nv-scr-*` tokens.
 */
const props = withDefaults(
  defineProps<{
    /** Series to plot (need at least two points). */
    data?: number[]
    /** Fill the area under the line with a fading gradient. */
    area?: boolean
    /** Line color (any CSS color) — 按参数类型着色（2026-07 生产走查）；默认数据青。 */
    color?: string
  }>(),
  {
    area: false,
    data: () => [8, 11, 9, 14, 12, 17, 15, 21, 19, 24, 22, 28],
    color: undefined,
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
    class="nv-scr-sl"
    :viewBox="`0 0 ${W} ${H}`"
    preserveAspectRatio="none"
    aria-hidden="true"
    :style="color ? { '--nv-scr-sl-color': color } : undefined"
  >
    <defs>
      <linearGradient :id="`${uid}-fill`" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" :stop-color="color ?? 'var(--nv-scr-cyan)'" stop-opacity=".16" />
        <stop offset="1" :stop-color="color ?? 'var(--nv-scr-cyan)'" stop-opacity="0" />
      </linearGradient>
    </defs>
    <!-- d 同时走 attribute（兜底）与 style（Chromium 可对 CSS d 做 transition，
         点数恒定 → 数据更新时折线平滑变形而非跳变） -->
    <path
      v-if="area"
      class="nv-scr-sl-area"
      :d="geom.area"
      :style="{ d: `path('${geom.area}')` }"
      :fill="`url(#${uid}-fill)`"
    />
    <path
      class="nv-scr-sl-line"
      :d="geom.line"
      :style="{ d: `path('${geom.line}')` }"
      fill="none"
      stroke="var(--nv-scr-sl-color, var(--nv-scr-cyan))"
      stroke-width="1.5"
      stroke-linecap="round"
      stroke-linejoin="round"
      vector-effect="non-scaling-stroke"
    />
  </svg>
  <svg
    v-else
    class="nv-scr-sl"
    :viewBox="`0 0 ${W} ${H}`"
    preserveAspectRatio="none"
    aria-hidden="true"
  >
    <line
      :x1="0"
      :y1="H / 2"
      :x2="W"
      :y2="H / 2"
      stroke="var(--nv-scr-faint)"
      stroke-width="1"
      stroke-dasharray="3 4"
      vector-effect="non-scaling-stroke"
    />
  </svg>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-sl {
    display: block;
    width: 100%;
    height: 100%;
    overflow: visible;
  }
  /* crisp hairline with just a whisper of glow — no heavy blur */
  .nv-scr-sl-line {
    filter: drop-shadow(0 0 2.5px var(--nv-scr-cyan-dim));
  }
  /* 数据增长动效：新数据到达时折线/面积平滑变形（emphasized 减速，无回弹） */
  .nv-scr-sl-line,
  .nv-scr-sl-area {
    transition: d 0.6s var(--nv-scr-ease-emphasized);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-sl-line,
    .nv-scr-sl-area {
      transition: none;
    }
  }
}
</style>
