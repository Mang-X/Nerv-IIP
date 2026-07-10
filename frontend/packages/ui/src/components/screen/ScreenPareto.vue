<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — pareto. 帕累托 TOP-N（缺陷/停机原因/不良代码等"少数关键项"分析的
 * 正确形态）：名称 + 发丝级条 + 数量/占比 + **累计占比 Σ**（没有累计不算真帕累托，
 * 80/20 集中度要能直接读出）。条长按 TOP1 归一便于对比；轨道即 100% 全量口径，
 * 竖刻度标累计位置；TOP1 红 / TOP2 橙强调、其余青色渐弱；填充语义色**渐隐**
 * （左实右消，与数字强调线同源语言），不发光、不描边。底部汇总 = TOP 合计 vs 长尾。
 */
const props = withDefaults(
  defineProps<{
    /** 降序项：label 主名、sub 次要来源（如产线名）、count 数量、pct 占全量 % */
    items: { label: string; sub?: string; count: number; pct: number }[]
    /** 全量总数（算长尾行；不传则无长尾行） */
    total?: number
    /** 数量单位（默认 件） */
    unit?: string
  }>(),
  { total: undefined, unit: '件' },
)

const max = computed(() => Math.max(1, ...props.items.map((i) => i.count)))
/** 逐行累计占比（80/20 集中度直读） */
const cums = computed(() => {
  let acc = 0
  return props.items.map((i) => {
    acc += i.pct
    return Math.round(acc * 10) / 10
  })
})
const topSum = computed(() => cums.value.at(-1) ?? 0)
const tailCount = computed(() =>
  props.total ? props.total - props.items.reduce((n, i) => n + i.count, 0) : 0,
)

function tone(i: number): 't1' | 't2' | 't3' {
  return i === 0 ? 't1' : i === 1 ? 't2' : 't3'
}
</script>

<template>
  <div class="nv-scr-pa">
    <div v-for="(it, i) in items" :key="it.label" class="nv-scr-pa-item">
      <div class="nv-scr-pa-head">
        <span class="nv-scr-pa-rank" :class="tone(i)">{{ String(i + 1).padStart(2, '0') }}</span>
        <b class="nv-scr-pa-name">{{ it.label }}</b>
        <span class="nv-scr-pa-sub">{{ it.sub }}</span>
        <span class="nv-scr-pa-count">{{ it.count }} {{ unit }}</span>
        <b class="nv-scr-pa-pct" :class="tone(i)">{{ it.pct.toFixed(1) }}<small>%</small></b>
      </div>
      <div class="nv-scr-pa-track">
        <i
          class="nv-scr-pa-fill"
          :class="tone(i)"
          :style="{ width: `${(it.count / max) * 100}%` }"
        />
        <i
          class="nv-scr-pa-cum-tick"
          :style="{ left: `${Math.min(100, cums[i])}%` }"
          aria-hidden="true"
        />
      </div>
      <div class="nv-scr-pa-cum">Σ {{ cums[i].toFixed(1) }}%</div>
    </div>
    <div class="nv-scr-pa-sum">
      <span
        >TOP{{ items.length }} 合计 <b>{{ topSum.toFixed(1) }}%</b></span
      >
      <span v-if="total" class="nv-scr-pa-tail"
        >长尾 {{ tailCount }} {{ unit }} · {{ (100 - topSum).toFixed(1) }}%</span
      >
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-pa {
    display: flex;
    flex-direction: column;
    gap: 8px;
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-pa-head {
    display: flex;
    align-items: baseline;
    gap: 9px;
    min-width: 0;
    margin-bottom: 6px;
  }
  .nv-scr-pa-rank {
    flex: none;
    font-family: ui-monospace, monospace;
    font-size: 11px;
    color: var(--nv-scr-faint);
  }
  .nv-scr-pa-rank.t1 {
    color: var(--nv-scr-red);
  }
  .nv-scr-pa-rank.t2 {
    color: var(--nv-scr-amber);
  }
  .nv-scr-pa-name {
    flex: none;
    font-size: 13.5px;
    font-weight: 600;
    color: var(--nv-scr-text);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    min-width: 0;
  }
  .nv-scr-pa-sub {
    flex: 1;
    min-width: 0;
    font-size: 11.5px;
    color: var(--nv-scr-faint);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .nv-scr-pa-count {
    flex: none;
    font-size: 12px;
    color: var(--nv-scr-muted);
  }
  .nv-scr-pa-pct {
    flex: none;
    min-width: 52px;
    text-align: right;
    font-size: 15px;
    font-weight: 700;
    color: var(--nv-scr-text-2);
    line-height: 1;
  }
  .nv-scr-pa-pct small {
    font-size: 11px;
    font-weight: 600;
    margin-left: 1px;
  }
  .nv-scr-pa-pct.t1 {
    color: var(--nv-scr-red);
  }
  .nv-scr-pa-pct.t2 {
    color: var(--nv-scr-amber);
  }

  /* 发丝轨道 + 渐隐填充（左实右消；静态元素不发光）。
   轨道即 100% 全量口径 —— 填充是该项占比（TOP1 归一），
   竖刻度是**累计占比**位置（帕累托的 80/20 读法） */
  .nv-scr-pa-track {
    position: relative;
    height: 5px;
    border-radius: 999px;
    background: rgba(255, 255, 255, 0.045);
    box-shadow: inset 0 0 0 1px var(--nv-scr-line);
  }
  .nv-scr-pa-fill {
    display: block;
    height: 100%;
    border-radius: 999px;
    transition: width 0.6s var(--nv-scr-ease-emphasized);
  }
  .nv-scr-pa-fill.t1 {
    background: linear-gradient(90deg, rgba(239, 90, 99, 0.9), rgba(239, 90, 99, 0.22));
  }
  .nv-scr-pa-fill.t2 {
    background: linear-gradient(90deg, rgba(242, 193, 78, 0.85), rgba(242, 193, 78, 0.18));
  }
  .nv-scr-pa-fill.t3 {
    background: linear-gradient(90deg, rgba(74, 166, 238, 0.62), rgba(74, 166, 238, 0.1));
  }
  .nv-scr-pa-cum-tick {
    position: absolute;
    top: -3px;
    bottom: -3px;
    width: 1.5px;
    border-radius: 1px;
    background: rgba(180, 210, 250, 0.55);
    transition: left 0.6s var(--nv-scr-ease-emphasized);
  }
  .nv-scr-pa-cum {
    margin-top: 4px;
    font-size: 10.5px;
    color: var(--nv-scr-faint);
    text-align: right;
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-pa-sum {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 10px;
    padding-top: 9px;
    border-top: 1px solid var(--nv-scr-divider);
    font-size: 12px;
    color: var(--nv-scr-muted);
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-pa-sum b {
    color: var(--nv-scr-text);
    font-weight: 700;
  }
  .nv-scr-pa-tail {
    color: var(--nv-scr-faint);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-scr-pa-fill,
    .nv-scr-pa-cum-tick {
      transition: none;
    }
  }
}
</style>
