<script setup lang="ts">
import { computed, ref } from 'vue'
import ScreenPanel from './ScreenPanel.vue'

/**
 * Screen — output trend. A glowing cyan actual line over a soft area fill, a
 * dashed indigo plan series and a faint dashed grid. **Interactive**: hover the
 * plot and a crosshair + info card snaps to the nearest data point, reading off
 * both series; with no hover it rests on the optional `tooltip` pin. All paths
 * are computed from `actual` / `plan`; the y-scale derives from the data (rounded
 * up) so callers pass raw numbers.
 */
const props = withDefaults(
  defineProps<{
    /** Actual output per x-tick — the cyan line. */
    actual?: number[]
    /** Planned output per x-tick — the dashed indigo line. */
    plan?: number[]
    /** Y-axis labels, top → bottom (purely visual; scale comes from data). */
    yLabels?: string[]
    /** X-axis labels under the plot. */
    xLabels?: string[]
    /** Resting crosshair pin when not hovering. `x` is the data index. */
    tooltip?: { x: number, label: string, actual: string, plan: string }
    title?: string
  }>(),
  {
    title: '产量趋势（件）',
    actual: () => [120, 360, 640, 760, 980, 880, 1086, 760, 940, 910, 930, 910],
    plan: () => [140, 420, 700, 900, 1010, 1080, 1150, 1180, 1220, 1260, 1320, 1380],
    yLabels: () => ['1,500', '1,200', '900', '600', '300', '0'],
    xLabels: () => ['00:00', '04:00', '08:00', '12:00', '16:00', '20:00', '24:00'],
    tooltip: () => ({ x: 6, label: '12:00', actual: '1,086', plan: '1,150' }),
  },
)

// Plot geometry inside the 940×300 viewBox (left gutter holds the y-axis labels).
const VB_W = 940
const VB_H = 300
const PAD_L = 46
const PAD_T = 22
const PAD_B = 14

const top = PAD_T
const bottom = VB_H - PAD_B
const left = PAD_L
const right = VB_W

const max = computed(() => {
  const peak = Math.max(1, ...props.actual, ...props.plan)
  const mag = 10 ** Math.floor(Math.log10(peak))
  return Math.ceil(peak / mag) * mag
})

function xAt(i: number, len: number) {
  if (len <= 1) return left
  return left + ((right - left) * i) / (len - 1)
}
function yAt(v: number) {
  return bottom - (bottom - top) * (v / max.value)
}
function linePath(data: number[]) {
  if (!data.length) return ''
  return data.map((v, i) => `${i === 0 ? 'M' : 'L'}${xAt(i, data.length).toFixed(1)} ${yAt(v).toFixed(1)}`).join(' ')
}
function fmt(v: number) {
  return v.toLocaleString('en-US')
}

const actualPath = computed(() => linePath(props.actual))
const planPath = computed(() => linePath(props.plan))
const areaPath = computed(() =>
  props.actual.length
    ? `${actualPath.value} L${right} ${bottom} L${left} ${bottom} Z`
    : '',
)

// Evenly spaced faint grid lines, one fewer than the y-label count.
const gridYs = computed(() => {
  const n = Math.max(2, props.yLabels.length - 1)
  return Array.from({ length: n }, (_, i) => top + ((bottom - top) * i) / (n - 1))
})

// --- hover interaction ---------------------------------------------------
const svgEl = ref<SVGSVGElement>()
const hover = ref<number | null>(null)

function onMove(e: MouseEvent) {
  const svg = svgEl.value
  const len = props.actual.length
  if (!svg || len < 1) return
  const r = svg.getBoundingClientRect()
  const svgX = ((e.clientX - r.left) / r.width) * VB_W
  const i = Math.round(((svgX - left) / (right - left)) * (len - 1))
  hover.value = Math.max(0, Math.min(len - 1, i))
}
function onLeave() {
  hover.value = null
}

const cross = computed(() => {
  const len = props.actual.length
  let i: number | null = null
  let label = ''
  let aVal = ''
  let pVal = ''
  if (hover.value != null) {
    i = hover.value
    const hr = Math.round((i / Math.max(1, len - 1)) * 24)
    label = `${String(hr).padStart(2, '0')}:00`
    aVal = fmt(props.actual[i] ?? 0)
    pVal = fmt(props.plan[i] ?? 0)
  } else if (props.tooltip) {
    i = Math.max(0, Math.min(len - 1, props.tooltip.x))
    label = props.tooltip.label
    aVal = props.tooltip.actual
    pVal = props.tooltip.plan
  }
  if (i == null) return null
  const cx = xAt(i, len)
  const cy = yAt(props.actual[i] ?? 0)
  const cardW = 160
  const cardH = 66
  // Keep the card on-canvas: flip to the left of the rule near the right edge.
  const cardX = cx + 14 + cardW > right ? cx - 14 - cardW : cx + 14
  const cardY = Math.min(Math.max(top, cy - cardH / 2), bottom - cardH)
  return { cx, cy, cardX, cardY, cardW, cardH, label, aVal, pVal }
})

const uid = Math.random().toString(36).slice(2, 8)
</script>

<template>
  <ScreenPanel :title="title" class="sb-tc">
    <template #title-extra>
      <em class="sb-tc-key">— 实际产量　--- 计划产量</em>
    </template>
    <template #extra>
      <div class="sb-tc-tabs">
        <span class="on">今日</span><span>近7天</span><span>近30天</span>
      </div>
    </template>

    <div class="sb-tc-body">
      <div class="sb-tc-y">
        <span v-for="(y, i) in yLabels" :key="i">{{ y }}</span>
      </div>
      <svg
        ref="svgEl"
        class="sb-tc-svg"
        :viewBox="`0 0 ${VB_W} ${VB_H}`"
        preserveAspectRatio="none"
        @mousemove="onMove"
        @mouseleave="onLeave"
      >
        <defs>
          <linearGradient :id="`sbTc-${uid}`" x1="0" y1="0" x2="0" y2="1">
            <stop class="sb-tc-g0" offset="0" />
            <stop class="sb-tc-g1" offset="1" />
          </linearGradient>
        </defs>

        <g class="sb-tc-grid" stroke-dasharray="3 6">
          <line v-for="(gy, i) in gridYs" :key="i" :x1="left" :y1="gy" :x2="right" :y2="gy" />
        </g>

        <path class="sb-tc-area" :d="areaPath" :fill="`url(#sbTc-${uid})`" />
        <path class="sb-tc-plan" :d="planPath" fill="none" stroke-width="1.5" stroke-dasharray="5 5" />
        <path class="sb-tc-act" :d="actualPath" fill="none" stroke-width="2" vector-effect="non-scaling-stroke" />

        <!-- transparent capture layer so hover fires over empty plot area too -->
        <rect :x="left" :y="top" :width="right - left" :height="bottom - top" fill="transparent" />

        <g v-if="cross" pointer-events="none">
          <line
            class="sb-tc-rule"
            :x1="cross.cx"
            :y1="top"
            :x2="cross.cx"
            :y2="bottom + 2"
            stroke-width="1"
            stroke-dasharray="2 4"
          />
          <circle class="sb-tc-dot" :cx="cross.cx" :cy="cross.cy" r="3.5" />
          <g :transform="`translate(${cross.cardX},${cross.cardY})`">
            <rect class="sb-tc-card" :width="cross.cardW" :height="cross.cardH" rx="6" />
            <text class="sb-tc-c-t" x="14" y="23" font-size="12">{{ cross.label }}</text>
            <text class="sb-tc-c-a" x="14" y="43" font-size="13">● 实际产量　{{ cross.aVal }}</text>
            <text class="sb-tc-c-p" x="14" y="59" font-size="13">┄ 计划产量　{{ cross.pVal }}</text>
          </g>
        </g>
      </svg>
    </div>

    <div class="sb-tc-x">
      <span v-for="(x, i) in xLabels" :key="i">{{ x }}</span>
    </div>
  </ScreenPanel>
</template>

<style scoped>
.sb-tc {
  display: flex;
  flex-direction: column;
  font-variant-numeric: tabular-nums;
}
.sb-tc-key {
  font-size: 12px;
  color: var(--sb-muted);
  font-style: normal;
  font-weight: 400;
  margin-left: 12px;
}
.sb-tc-tabs {
  display: flex;
  border: 1px solid var(--sb-line-2);
  border-radius: 6px;
  overflow: hidden;
  font-size: 12px;
}
.sb-tc-tabs span {
  padding: 5px 14px;
  color: var(--sb-muted);
}
.sb-tc-tabs span.on {
  background: rgba(0, 229, 255, 0.13);
  color: var(--sb-cyan);
}
.sb-tc-body {
  flex: 1;
  position: relative;
  min-height: 0;
}
.sb-tc-y {
  position: absolute;
  left: 0;
  top: 0;
  height: 100%;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  color: var(--sb-faint);
  font-size: 11px;
  padding: 4px 0;
}
.sb-tc-svg {
  width: 100%;
  height: 100%;
  overflow: visible;
  cursor: crosshair;
}
/* SVG paint via CSS props so the --sb-* tokens resolve reliably */
.sb-tc-g0 {
  stop-color: var(--sb-cyan);
  stop-opacity: 0.2;
}
.sb-tc-g1 {
  stop-color: var(--sb-cyan);
  stop-opacity: 0;
}
.sb-tc-grid {
  stroke: rgba(255, 255, 255, 0.045);
}
.sb-tc-plan {
  stroke: var(--sb-indigo);
  opacity: 0.85;
}
.sb-tc-act {
  stroke: var(--sb-cyan);
  filter: drop-shadow(0 0 3px var(--sb-cyan-dim));
}
.sb-tc-rule {
  stroke: var(--sb-cyan);
  opacity: 0.55;
}
.sb-tc-dot {
  fill: #fff;
  filter: drop-shadow(0 0 4px var(--sb-cyan));
}
.sb-tc-card {
  /* glassy dark card — translucent so the plot shows faintly through, white edge */
  fill: rgba(9, 13, 22, 0.9);
  stroke: rgba(255, 255, 255, 0.16);
}
.sb-tc-c-t {
  fill: var(--sb-muted);
}
.sb-tc-c-a {
  fill: var(--sb-text);
}
.sb-tc-c-p {
  fill: var(--sb-indigo);
}
.sb-tc-x {
  display: flex;
  justify-content: space-between;
  color: var(--sb-faint);
  font-size: 11px;
  margin-top: 4px;
  padding-left: 46px;
}
</style>
