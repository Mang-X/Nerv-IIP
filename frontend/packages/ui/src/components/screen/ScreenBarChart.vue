<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

/**
 * Screen — bar chart. 纵向柱状图（离散量的正确形态：小时流量/日产量这类
 * "一段一个数"的数据，柱比面积曲线更诚实）。支持 1–2 条序列并排对比；
 * 柱体语义色**上实下消**渐隐（与数字强调线同源语言，静态不发光）；
 * 悬停整列高亮 + HTML 信息卡逐序列读数（文字层不随 SVG 拉伸变形）。
 * autoplay：信息卡自动逐列巡显（挂墙无人操作也能读到每一格的数），
 * 鼠标交互即暂停、移出恢复；reduced-motion 下不自动巡显。
 */
const props = withDefaults(
  defineProps<{
    /** 1–2 条序列（并排分组柱）；data 等长 */
    series: { label: string; color: string; data: number[] }[]
    /** X 轴下标签（可稀疏，与 data 不必等长 —— 均匀分布） */
    xLabels?: string[]
    /** 悬停标签（与 data 等长；缺省用序号） */
    hoverLabels?: string[]
    /** 信息卡自动巡显（用户悬停时暂停） */
    autoplay?: boolean
    autoplayMs?: number
  }>(),
  { xLabels: () => [], hoverLabels: undefined, autoplay: false, autoplayMs: 2400 },
)

const VB_W = 400
const VB_H = 140
const PAD_T = 8
const PAD_B = 4

const n = computed(() => Math.max(1, ...props.series.map((s) => s.data.length)))
const peak = computed(() => Math.max(1, ...props.series.flatMap((s) => s.data)))

/** 组内柱布局：槽宽 = 画布/n，柱占槽 62%（双序列各半），余为组间距 */
const bars = computed(() => {
  const slot = VB_W / n.value
  const groupW = slot * 0.62
  const barW = groupW / props.series.length
  const out: { x: number; y: number; w: number; h: number; si: number; i: number }[] = []
  props.series.forEach((s, si) => {
    s.data.forEach((v, i) => {
      const h = ((VB_H - PAD_T - PAD_B) * v) / peak.value
      out.push({
        x: slot * i + (slot - groupW) / 2 + barW * si,
        y: VB_H - PAD_B - h,
        w: barW,
        h,
        si,
        i,
      })
    })
  })
  return out
})

const uid = Math.random().toString(36).slice(2, 8)

// —— 悬停：按槽命中，整列高亮 + 信息卡；autoplay 自动巡显（用户悬停优先）——
const svgEl = ref<SVGSVGElement>()
const hover = ref<number | null>(null)
const auto = ref<number | null>(null)
let timer: ReturnType<typeof setInterval> | undefined

function onMove(e: MouseEvent) {
  const svg = svgEl.value
  if (!svg) return
  const r = svg.getBoundingClientRect()
  const i = Math.floor(((e.clientX - r.left) / r.width) * n.value)
  hover.value = Math.max(0, Math.min(n.value - 1, i))
}
onMounted(() => {
  if (!props.autoplay) return
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
  timer = setInterval(() => {
    if (hover.value != null) return // 用户交互中：暂停巡显
    auto.value = ((auto.value ?? -1) + 1) % n.value
  }, props.autoplayMs)
})
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})

const active = computed(() => hover.value ?? auto.value)
const card = computed(() => {
  const i = active.value
  if (i == null) return null
  const label = props.hoverLabels?.[i] ?? `#${i + 1}`
  const vals = props.series.map((s) => ({ label: s.label, color: s.color, val: s.data[i] ?? 0 }))
  const cx = ((i + 0.5) / n.value) * 100
  return { i, label, vals, cx, flip: cx > 55 }
})
</script>

<template>
  <div class="sb-bc">
    <svg
      ref="svgEl"
      class="sb-bc-svg"
      :viewBox="`0 0 ${VB_W} ${VB_H}`"
      preserveAspectRatio="none"
      @mousemove="onMove"
      @mouseleave="hover = null"
    >
      <defs>
        <linearGradient
          v-for="(s, si) in series"
          :id="`sbBc-${uid}-${si}`"
          :key="si"
          x1="0"
          y1="0"
          x2="0"
          y2="1"
        >
          <stop offset="0" :stop-color="s.color" stop-opacity="0.92" />
          <stop offset="1" :stop-color="s.color" stop-opacity="0.16" />
        </linearGradient>
      </defs>
      <g class="sb-bc-grid" stroke-dasharray="3 6">
        <line v-for="gi in 3" :key="gi" :x1="0" :x2="VB_W" :y1="PAD_T + ((VB_H - PAD_T - PAD_B) * gi) / 3" :y2="PAD_T + ((VB_H - PAD_T - PAD_B) * gi) / 3" />
      </g>
      <rect
        v-for="(b, bi) in bars"
        :key="bi"
        class="sb-bc-bar"
        :class="{ dim: active != null && active !== b.i }"
        :x="b.x"
        :y="b.y"
        :width="b.w"
        :height="Math.max(0.5, b.h)"
        rx="1"
        :fill="`url(#sbBc-${uid}-${b.si})`"
      />
    </svg>
    <!-- 悬停信息卡：HTML overlay，逐序列读数 -->
    <div
      v-if="card"
      class="sb-bc-card"
      :class="{ flip: card.flip }"
      :style="{ left: `${card.cx}%` }"
    >
      <span class="sb-bc-c-t">{{ card.label }}</span>
      <span v-for="v in card.vals" :key="v.label" class="sb-bc-c-s">
        <i :style="{ background: v.color }" aria-hidden="true" />{{ v.label }}<b>{{ v.val }}</b>
      </span>
    </div>
    <div v-if="xLabels.length" class="sb-bc-x">
      <span v-for="(x, i) in xLabels" :key="i">{{ x }}</span>
    </div>
  </div>
</template>

<style scoped>
.sb-bc {
  position: relative;
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}
.sb-bc-svg {
  flex: 1;
  min-height: 0;
  width: 100%;
}
.sb-bc-grid line {
  stroke: rgba(255, 255, 255, 0.055);
}
.sb-bc-bar {
  transition:
    height 0.5s var(--sb-ease-emphasized),
    y 0.5s var(--sb-ease-emphasized),
    opacity 0.18s var(--sb-ease);
}
.sb-bc-bar.dim {
  opacity: 0.38;
}
.sb-bc-x {
  display: flex;
  justify-content: space-between;
  margin-top: 5px;
  font-size: 11px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}
.sb-bc-card {
  position: absolute;
  top: 4px;
  z-index: 1;
  transform: translateX(10px);
  min-width: 118px;
  padding: 8px 12px 9px;
  border-radius: 6px;
  background: rgba(9, 13, 22, 0.92);
  border: 1px solid rgba(255, 255, 255, 0.16);
  display: flex;
  flex-direction: column;
  gap: 3px;
  white-space: nowrap;
  pointer-events: none;
}
.sb-bc-card.flip {
  transform: translateX(calc(-100% - 10px));
}
.sb-bc-c-t {
  font-size: 12px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.sb-bc-c-s {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12.5px;
  color: var(--sb-text-2);
}
.sb-bc-c-s i {
  width: 8px;
  height: 8px;
  border-radius: 2px;
  flex: none;
}
.sb-bc-c-s b {
  margin-left: auto;
  padding-left: 10px;
  font-weight: 700;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
@media (prefers-reduced-motion: reduce) {
  .sb-bc-bar {
    transition: none;
  }
}
</style>
