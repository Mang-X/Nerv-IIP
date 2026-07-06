<script setup lang="ts">
import { StatusLight } from '@nerv-iip/ui'
import { computed } from 'vue'
import type { WorkshopCell } from '@/data/contracts/factory'

/**
 * 车间健康卡（厂长 3 秒判绿/黄/红，spec §二）：主管 + 状态灯 + 达成率主数 +
 * 在产/超期/告警/停机四格。健康度着色克制：灯、状态词、达成率数字；
 * 红卡边框微染并缓脉冲（reduced-motion 静止）。边框整圈近暗，顶边仅亮一丝。
 */
const props = defineProps<{ cell: WorkshopCell }>()

const tone = computed(
  () => ({ red: 'alarm', yellow: 'idle', green: 'run' })[props.cell.health] as 'alarm' | 'idle' | 'run',
)
const nf = new Intl.NumberFormat('en-US')
</script>

<template>
  <article class="whc" :class="cell.health">
    <header class="whc-top">
      <div>
        <h4 class="whc-name">{{ cell.name }}</h4>
        <p class="whc-mgr">主管 {{ cell.manager }}</p>
      </div>
      <StatusLight :tone="tone" :label="cell.stateLabel" />
    </header>

    <div class="whc-rate">
      <span class="whc-rate-v" :class="cell.health">{{ cell.rate }}<small>%</small></span>
      <span class="whc-rate-d">
        <span>实际 {{ nf.format(cell.actualQty) }}</span>
        <span>计划 {{ nf.format(cell.planQty) }}</span>
      </span>
    </div>

    <div class="whc-bar">
      <i :class="cell.health" :style="{ width: `${cell.rate}%` }" />
    </div>

    <dl class="whc-minis">
      <div>
        <dt>在产</dt>
        <dd>{{ cell.wip }}</dd>
      </div>
      <div>
        <dt>超期</dt>
        <dd :class="{ bad: cell.overdue > 0 }">{{ cell.overdue }}</dd>
      </div>
      <div>
        <dt>告警</dt>
        <dd :class="{ bad: cell.critAlarms > 0 }">{{ cell.critAlarms }}</dd>
      </div>
      <div>
        <dt>停机</dt>
        <dd :class="{ warn: cell.openDowntime > 0 }">{{ cell.openDowntime }}</dd>
      </div>
    </dl>
  </article>
</template>

<style scoped>
.whc {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  min-height: 0;
  padding: 18px 20px 14px;
  border-radius: var(--sb-radius);
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  border: 1px solid var(--sb-line);
  border-top-color: rgba(255, 255, 255, 0.09);
}
/* 红卡：边框微染 + 缓脉冲（置顶已由数据层排序保证） */
.whc.red {
  border-color: rgba(239, 90, 99, 0.26);
  animation: whc-pulse 2.6s ease-in-out infinite;
}
@keyframes whc-pulse {
  50% {
    border-color: rgba(239, 90, 99, 0.5);
  }
}

.whc-top {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 10px;
}
.whc-name {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
  color: var(--sb-text);
}
.whc-mgr {
  margin: 4px 0 0;
  font-size: 12.5px;
  color: var(--sb-faint);
}

.whc-rate {
  display: flex;
  align-items: baseline;
  gap: 12px;
  margin: 13px 0 11px;
}
.whc-rate-v {
  font-size: 46px;
  font-weight: 700;
  line-height: 1;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
}
.whc-rate-v small {
  font-size: 20px;
  font-weight: 600;
  margin-left: 1px;
}
.whc-rate-v.yellow {
  color: var(--sb-amber);
}
.whc-rate-v.red {
  color: var(--sb-red);
}
.whc-rate-d {
  display: flex;
  flex-direction: column;
  gap: 3px;
  font-size: 12.5px;
  color: var(--sb-faint);
  font-variant-numeric: tabular-nums;
}

/* 达成率进度条：健康色随卡（绿卡走数据青色，黄/红卡语义色） */
.whc-bar {
  height: 6px;
  border-radius: 3px;
  background: rgba(255, 255, 255, 0.06);
  overflow: hidden;
  margin: 2px 0 10px;
}
.whc-bar i {
  display: block;
  height: 100%;
  border-radius: 3px;
  background: var(--sb-cyan);
  transition: width 0.6s var(--sb-ease-emphasized);
}
.whc-bar i.yellow {
  background: var(--sb-amber);
}
.whc-bar i.red {
  background: var(--sb-red);
}

.whc-minis {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 6px;
  margin: 0;
  padding-top: 11px;
  border-top: 1px solid var(--sb-divider);
}
.whc-minis dt {
  font-size: 12px;
  color: var(--sb-faint);
}
.whc-minis dd {
  margin: 3px 0 0;
  font-size: 18px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text-2);
}
.whc-minis dd.bad {
  color: var(--sb-red);
}
.whc-minis dd.warn {
  color: var(--sb-amber);
}

@media (prefers-reduced-motion: reduce) {
  .whc.red {
    animation: none;
    border-color: rgba(239, 90, 99, 0.38);
  }
  .whc-bar i {
    transition: none;
  }
}
</style>
