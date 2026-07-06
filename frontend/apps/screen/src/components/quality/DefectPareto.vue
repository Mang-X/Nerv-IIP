<script setup lang="ts">
import { computed } from 'vue'
import type { ParetoItem } from '@/data/contracts/quality'

/**
 * 缺陷帕累托 TOP5 —— 本地轻组件（packages/ui 原版零改动）。
 * 名称 + 发丝级条 + 件数/占比：条长按 TOP1 归一，TOP1 红 / TOP2 橙强调、
 * 其余青色渐弱；填充为语义色**渐隐**（左实右消，与数字强调线同源语言），
 * 不发光、不描边。抖动更新时条宽缓动（reduced-motion 降级）。
 */
const props = defineProps<{ items: ParetoItem[] }>()

const max = computed(() => Math.max(1, ...props.items.map((i) => i.count)))

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
      </div>
    </div>
  </div>
</template>

<style scoped>
.dp {
  display: flex;
  flex-direction: column;
  gap: 13px;
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

/* 发丝轨道 + 渐隐填充（左实右消；静态元素不发光） */
.dp-track {
  height: 5px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.045);
  box-shadow: inset 0 0 0 1px var(--sb-line);
  overflow: hidden;
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
