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
const range = defineModel<string | number>('range')

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
    /** 每个数据点的悬停标签（与 actual 等长）；缺省按 24h 均匀推算（2026-07 生产化） */
    hoverLabels?: string[]
    /** 两条序列的图例/悬停名（默认 实际产量/计划产量） */
    actualLabel?: string
    planLabel?: string
    /** 右上时间范围假 tabs 为演示装饰 —— 生产使用传 false 隐藏 */
    tabs?: boolean
    /** 真实时间范围切换（提供即渲染可点 tabs，经 v-model:range 切换；覆盖 tabs 装饰） */
    ranges?: { label: string; value: string | number }[]
    /** 附加对比序列（如质量分层 IQC/IPQC/FQC）：细线 + 图例色点 + 悬停逐条读数。
     *  与 actual 等长；color 建议传字面量色（CSS 变量可能被调用方局部重映射） */
    series?: { label: string; color: string; data: number[] }[]
  }>(),
  {
    title: '产量趋势（件）',
    hoverLabels: undefined,
    actualLabel: '实际产量',
    planLabel: '计划产量',
    tabs: true,
    ranges: undefined,
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
  const peak = Math.max(
    1,
    ...props.actual,
    ...props.plan,
    ...(props.series ?? []).flatMap((s) => s.data),
  )
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
const seriesPaths = computed(() =>
  (props.series ?? []).map((s) => ({ label: s.label, color: s.color, d: linePath(s.data) })),
)
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
    if (props.hoverLabels?.length) {
      label = props.hoverLabels[Math.min(i, props.hoverLabels.length - 1)]
    } else {
      const hr = Math.round((i / Math.max(1, len - 1)) * 24)
      label = `${String(hr).padStart(2, '0')}:00`
    }
    aVal = fmt(props.actual[i] ?? 0)
    pVal = fmt(props.plan[i] ?? 0)
  } else if (props.tooltip) {
    i = Math.max(0, Math.min(len - 1, props.tooltip.x))
    label = props.tooltip.label
    aVal = props.tooltip.actual
    pVal = props.tooltip.plan
  }
  if (i == null) return null
  // 附加序列逐条读数（分层对比的悬停真值）
  const sVals = (props.series ?? []).map((s) => ({
    label: s.label,
    color: s.color,
    val: s.data[i] != null ? fmt(s.data[i]) : '—',
  }))
  const cx = xAt(i, len)
  const cy = yAt(props.actual[i] ?? 0)
  // 信息卡为 HTML overlay（% 锚定）：SVG 用 preserveAspectRatio="none" 非均匀拉伸，
  // rect/text 放 SVG 内会随容器宽高比被拉变形。右半区翻转到标线左侧、纵向钳制在画布内
  // （钳制半径随序列行数增高，多行卡不顶出画布）。
  const flip = cx > (left + right) / 2
  const half = 36 + sVals.length * 9
  const cyClamped = Math.min(Math.max(cy, top + half), bottom - half)
  return { cx, cy, cyClamped, flip, label, aVal, pVal, sVals }
})

const uid = Math.random().toString(36).slice(2, 8)
</script>

<template>
  <ScreenPanel :title="title" class="sb-tc">
    <!-- 图例 + 范围切换独立一行（标题下方）：标题行不再挤，tabs 永远完整可见 -->
    <div v-if="(series?.length ?? 0) > 0 || ranges?.length || tabs" class="sb-tc-bar">
      <em class="sb-tc-key">
        — {{ actualLabel }}　--- {{ planLabel }}
        <span v-for="s in series ?? []" :key="s.label" class="sb-tc-key-s">
          <i :style="{ background: s.color }" aria-hidden="true" />{{ s.label }}
        </span>
      </em>
      <div v-if="ranges?.length" class="sb-tc-tabs">
        <button
          v-for="r in ranges"
          :key="String(r.value)"
          type="button"
          class="sb-tc-tab"
          :class="{ on: r.value === range }"
          @click="range = r.value"
        >
          {{ r.label }}
        </button>
      </div>
      <div v-else-if="tabs" class="sb-tc-tabs">
        <span class="on">今日</span><span>近7天</span><span>近30天</span>
      </div>
    </div>
    <em v-else class="sb-tc-key sb-tc-key-solo">— {{ actualLabel }}　--- {{ planLabel }}</em>

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

        <!-- d 同时走 attribute（兜底）与 style —— Chromium 对 CSS d 做 transition：
             轮询同点数更新时曲线平滑变形（切换范围点数不同则直接切换） -->
        <path class="sb-tc-area" :d="areaPath" :style="{ d: `path('${areaPath}')` }" :fill="`url(#sbTc-${uid})`" />
        <path class="sb-tc-plan" :d="planPath" :style="{ d: `path('${planPath}')` }" fill="none" stroke-width="1.5" stroke-dasharray="5 5" />
        <!-- 附加对比序列：细线（主曲线之下，颜色由调用方定义） -->
        <path
          v-for="s in seriesPaths"
          :key="s.label"
          class="sb-tc-ser"
          :d="s.d"
          :style="{ d: `path('${s.d}')`, stroke: s.color }"
          fill="none"
          stroke-width="1.3"
          vector-effect="non-scaling-stroke"
        />
        <path class="sb-tc-act" :d="actualPath" :style="{ d: `path('${actualPath}')` }" fill="none" stroke-width="2" vector-effect="non-scaling-stroke" />

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
        </g>
      </svg>
      <!-- 悬停点 + 信息卡：HTML overlay —— 文字层脱离 SVG 非均匀缩放坐标系，
           任何容器宽高比 / ScreenScaler 缩放下都不变形 -->
      <template v-if="cross">
        <i
          class="sb-tc-dot"
          :style="{ left: `${(cross.cx / VB_W) * 100}%`, top: `${(cross.cy / VB_H) * 100}%` }"
          aria-hidden="true"
        />
        <div
          class="sb-tc-cardh"
          :class="{ flip: cross.flip }"
          :style="{ left: `${(cross.cx / VB_W) * 100}%`, top: `${(cross.cyClamped / VB_H) * 100}%` }"
        >
          <span class="sb-tc-c-t">{{ cross.label }}</span>
          <span class="sb-tc-c-a">● {{ actualLabel }}<b>{{ cross.aVal }}</b></span>
          <span class="sb-tc-c-p">┄ {{ planLabel }}<b>{{ cross.pVal }}</b></span>
          <span v-for="sv in cross.sVals" :key="sv.label" class="sb-tc-c-s">
            <i :style="{ background: sv.color }" aria-hidden="true" />{{ sv.label }}<b>{{ sv.val }}</b>
          </span>
        </div>
      </template>
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
/* 图例 + 范围切换工具行（标题下方独立一行）：图例左、tabs 右；
   图例可收缩截断（真值在悬停卡），tabs 永远完整 */
.sb-tc-bar {
  display: flex;
  align-items: center;
  gap: 14px;
  margin: -4px 0 9px;
}
.sb-tc-key {
  flex: 1;
  font-size: 12px;
  color: var(--sb-muted);
  font-style: normal;
  font-weight: 400;
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.sb-tc-key-solo {
  display: block;
  margin: -4px 0 9px;
}
/* tabs 永不收缩折行（图例才是可收缩方） */
.sb-tc-tabs {
  display: flex;
  flex: none;
  border: 1px solid var(--sb-line-2);
  border-radius: 6px;
  overflow: hidden;
  font-size: 12px;
}
.sb-tc-tabs span,
.sb-tc-tab {
  padding: 5px 14px;
  color: var(--sb-muted);
  white-space: nowrap;
}
/* 真实可切换 tab（button 语义） */
.sb-tc-tab {
  appearance: none;
  border: 0;
  background: transparent;
  font: inherit;
  cursor: pointer;
  transition: color 0.18s var(--sb-ease);
}
.sb-tc-tab:hover:not(.on) {
  color: var(--sb-text-2);
}
.sb-tc-tab:focus-visible {
  outline: none;
  box-shadow: inset 0 0 0 2px var(--sb-cyan-dim);
}
.sb-tc-tabs span.on,
.sb-tc-tab.on {
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
/* 附加对比序列：细线不发光（主曲线才是主角），透明度略降层次分明 */
.sb-tc-ser {
  opacity: 0.85;
}
/* 数据增长动效：轮询更新时各路径平滑变形（emphasized 减速，无回弹） */
.sb-tc-act,
.sb-tc-plan,
.sb-tc-area,
.sb-tc-ser {
  transition: d 0.6s var(--sb-ease-emphasized);
}
@media (prefers-reduced-motion: reduce) {
  .sb-tc-act,
  .sb-tc-plan,
  .sb-tc-area,
  .sb-tc-ser,
  .sb-tc-tab {
    transition: none;
  }
}
.sb-tc-rule {
  stroke: var(--sb-cyan);
  opacity: 0.55;
}
/* 悬停点（HTML，圆度不受 SVG 拉伸影响） */
.sb-tc-dot {
  position: absolute;
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #fff;
  box-shadow: 0 0 6px var(--sb-cyan);
  transform: translate(-50%, -50%);
  pointer-events: none;
}
/* 信息卡（HTML overlay）：玻璃暗卡 + 白发丝边；右半区 flip 翻到标线左侧 */
.sb-tc-cardh {
  position: absolute;
  z-index: 1;
  transform: translate(14px, -50%);
  min-width: 150px;
  padding: 9px 14px 10px;
  border-radius: 6px;
  background: rgba(9, 13, 22, 0.9);
  border: 1px solid rgba(255, 255, 255, 0.16);
  display: flex;
  flex-direction: column;
  gap: 4px;
  white-space: nowrap;
  pointer-events: none;
}
.sb-tc-cardh.flip {
  transform: translate(calc(-100% - 14px), -50%);
}
.sb-tc-c-t {
  font-size: 12.5px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.sb-tc-c-a {
  font-size: 13px;
  color: var(--sb-text);
}
.sb-tc-c-p {
  font-size: 13px;
  color: var(--sb-indigo);
}
.sb-tc-c-a b,
.sb-tc-c-p b {
  margin-left: 9px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}
/* 附加序列：悬停行（色短线 + 名 + 值）与图例色点 */
.sb-tc-c-s {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 12.5px;
  color: var(--sb-text-2);
}
.sb-tc-c-s i {
  width: 9px;
  height: 2px;
  border-radius: 1px;
  flex: none;
}
.sb-tc-c-s b {
  margin-left: auto;
  padding-left: 9px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}
.sb-tc-key-s {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  margin-left: 12px;
}
.sb-tc-key-s i {
  width: 9px;
  height: 2px;
  border-radius: 1px;
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
