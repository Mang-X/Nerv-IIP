<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — donut. 环形占比图（状态构成/分布类数据的正确形态）：发丝底环 +
 * 语义色弧段（段间留缝），中心 slot 放主数字；右侧内置图例（色点 + 名 + 值）。
 * 弧段 dashoffset 缓动（轮询更新平滑再分配），静态不发光。
 */
const props = withDefaults(
  defineProps<{
    segments: { label: string; value: number; color: string }[]
    /** 环径（px，SVG 实际按容器缩放） */
    size?: number
    thickness?: number
    /** 内置图例（右侧）；关掉后自行排版 */
    legend?: boolean
  }>(),
  { size: 118, thickness: 11, legend: true },
)

const R = 52
const C = 2 * Math.PI * R
/** 段间缝（弧长 px） */
const GAP = 2.5

const total = computed(() =>
  Math.max(
    1,
    props.segments.reduce((n, s) => n + s.value, 0),
  ),
)

const arcs = computed(() => {
  let acc = 0
  const visible = props.segments.filter((s) => s.value > 0)
  return props.segments.map((s) => {
    const frac = s.value / total.value
    const len = Math.max(0, frac * C - (visible.length > 1 ? GAP : 0))
    const arc = { ...s, len, offset: -acc * C }
    acc += frac
    return arc
  })
})
</script>

<template>
  <div class="nv-scr-dn">
    <div class="nv-scr-dn-ring" :style="{ width: `${size}px`, height: `${size}px` }">
      <svg viewBox="0 0 120 120" class="nv-scr-dn-svg">
        <circle
          class="nv-scr-dn-track"
          cx="60"
          cy="60"
          :r="R"
          fill="none"
          :stroke-width="thickness"
        />
        <circle
          v-for="a in arcs"
          :key="a.label"
          class="nv-scr-dn-arc"
          cx="60"
          cy="60"
          :r="R"
          fill="none"
          :stroke="a.color"
          :stroke-width="thickness"
          :stroke-dasharray="`${a.len} ${C - a.len}`"
          :stroke-dashoffset="a.offset"
          stroke-linecap="butt"
          transform="rotate(-90 60 60)"
        />
      </svg>
      <div class="nv-scr-dn-center"><slot /></div>
    </div>
    <div v-if="legend" class="nv-scr-dn-legend">
      <div v-for="s in segments" :key="s.label" class="nv-scr-dn-item">
        <i :style="{ background: s.color }" aria-hidden="true" />
        <span class="nv-scr-dn-l">{{ s.label }}</span>
        <b class="nv-scr-dn-v">{{ s.value.toLocaleString('en-US') }}</b>
      </div>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-dn {
    display: flex;
    align-items: center;
    gap: 18px;
    min-width: 0;
  }
  .nv-scr-dn-ring {
    position: relative;
    flex: none;
  }
  .nv-scr-dn-svg {
    width: 100%;
    height: 100%;
  }
  .nv-scr-dn-track {
    stroke: rgba(255, 255, 255, 0.06);
  }
  .nv-scr-dn-arc {
    transition:
      stroke-dasharray 0.6s var(--nv-scr-ease-emphasized),
      stroke-dashoffset 0.6s var(--nv-scr-ease-emphasized);
  }
  .nv-scr-dn-center {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    text-align: center;
    pointer-events: none;
  }
  .nv-scr-dn-legend {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 7px;
  }
  .nv-scr-dn-item {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 12.5px;
    color: var(--nv-scr-muted);
    min-width: 0;
  }
  .nv-scr-dn-item i {
    width: 8px;
    height: 8px;
    border-radius: 2px;
    flex: none;
  }
  .nv-scr-dn-l {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .nv-scr-dn-v {
    margin-left: auto;
    padding-left: 10px;
    font-weight: 700;
    color: var(--nv-scr-text);
    font-variant-numeric: tabular-nums;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-dn-arc {
      transition: none;
    }
  }
}
</style>
