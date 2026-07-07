<script setup lang="ts">
import { computed } from 'vue'
import type { ParetoItem } from '@/data/contracts/quality'

/**
 * 缺陷帕累托 TOP5 —— 本地轻组件（packages/ui 原版零改动）。
 * 名称 + 发丝级条 + 件数/占比 + **累计占比 Σ**（没有累计线不算真帕累托：
 * 80/20 集中度要能直接读出）；条长按 TOP1 归一，TOP1 红 / TOP2 橙强调、
 * 其余青色渐弱；填充为语义色**渐隐**（左实右消，与数字强调线同源语言），
 * 不发光、不描边。底部汇总行 = TOP 合计 vs 长尾。抖动更新时条宽缓动。
 */
const props = defineProps<{
  items: ParetoItem[]
  /** 全部缺陷件数（算长尾；不传则以 TOP 合计为分母，无长尾行） */
  total?: number
}>()

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
  <div class="dp">
    <div v-for="(it, i) in items" :key="it.defect" class="dp-item">
      <div class="dp-head">
        <span class="dp-rank" :class="tone(i)">{{ String(i + 1).padStart(2, '0') }}</span>
        <b class="dp-name">{{ it.defect }}</b>
        <span class="dp-line">{{ it.lineName }}</span>
        <span class="dp-count">{{ it.count }} 件</span>
        <b class="dp-pct" :class="tone(i)">{{ it.pct.toFixed(1) }}<small>%</small></b>
      </div>
      <div class="dp-track">
        <i class="dp-fill" :class="tone(i)" :style="{ width: `${(it.count / max) * 100}%` }" />
        <!-- 累计占比刻度：整条轨道 = 100% 全量口径 -->
        <i class="dp-cum-tick" :style="{ left: `${Math.min(100, cums[i])}%` }" aria-hidden="true" />
      </div>
      <div class="dp-cum">Σ {{ cums[i].toFixed(1) }}%</div>
    </div>
    <div class="dp-sum">
      <span>TOP{{ items.length }} 合计 <b>{{ topSum.toFixed(1) }}%</b></span>
      <span v-if="total" class="dp-tail">长尾 {{ tailCount }} 件 · {{ (100 - topSum).toFixed(1) }}%</span>
    </div>
  </div>
</template>

<style scoped>
.dp {
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-variant-numeric: tabular-nums;
}
.dp-head {
  display: flex;
  align-items: baseline;
  gap: 9px;
  min-width: 0;
  margin-bottom: 6px;
}
.dp-rank {
  flex: none;
  font-family: ui-monospace, monospace;
  font-size: 11px;
  color: var(--sb-faint);
}
.dp-rank.t1 {
  color: var(--sb-red);
}
.dp-rank.t2 {
  color: var(--sb-amber);
}
.dp-name {
  flex: none;
  font-size: 13.5px;
  font-weight: 600;
  color: var(--sb-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
}
.dp-line {
  flex: 1;
  min-width: 0;
  font-size: 11.5px;
  color: var(--sb-faint);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dp-count {
  flex: none;
  font-size: 12px;
  color: var(--sb-muted);
}
.dp-pct {
  flex: none;
  min-width: 52px;
  text-align: right;
  font-size: 15px;
  font-weight: 700;
  color: var(--sb-text-2);
  line-height: 1;
}
.dp-pct small {
  font-size: 11px;
  font-weight: 600;
  margin-left: 1px;
}
.dp-pct.t1 {
  color: var(--sb-red);
}
.dp-pct.t2 {
  color: var(--sb-amber);
}

/* 发丝轨道 + 渐隐填充（左实右消；静态元素不发光）。
   轨道即 100% 全量口径 —— 填充是该项占比（TOP1 归一），
   竖刻度是**累计占比**位置（帕累托的 80/20 读法） */
.dp-track {
  position: relative;
  height: 5px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.045);
  box-shadow: inset 0 0 0 1px var(--sb-line);
}
.dp-cum-tick {
  position: absolute;
  top: -3px;
  bottom: -3px;
  width: 1.5px;
  border-radius: 1px;
  background: rgba(180, 210, 250, 0.55);
  transition: left 0.6s var(--sb-ease-emphasized);
}
.dp-cum {
  margin-top: 4px;
  font-size: 10.5px;
  color: var(--sb-faint);
  text-align: right;
  font-variant-numeric: tabular-nums;
}
.dp-sum {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  padding-top: 9px;
  border-top: 1px solid var(--sb-divider);
  font-size: 12px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.dp-sum b {
  color: var(--sb-text);
  font-weight: 700;
}
.dp-tail {
  color: var(--sb-faint);
}
.dp-fill {
  display: block;
  height: 100%;
  border-radius: 999px;
  transition: width 0.6s var(--sb-ease-emphasized);
}
.dp-fill.t1 {
  background: linear-gradient(90deg, rgba(239, 90, 99, 0.9), rgba(239, 90, 99, 0.22));
}
.dp-fill.t2 {
  background: linear-gradient(90deg, rgba(242, 193, 78, 0.85), rgba(242, 193, 78, 0.18));
}
.dp-fill.t3 {
  background: linear-gradient(90deg, rgba(74, 166, 238, 0.62), rgba(74, 166, 238, 0.1));
}

@media (prefers-reduced-motion: reduce) {
  .dp-fill {
    transition: none;
  }
}
</style>
