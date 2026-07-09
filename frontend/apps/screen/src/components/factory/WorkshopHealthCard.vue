<script setup lang="ts">
import { NvScreenPanel, NvScreenStatusLight } from '@nerv-iip/ui'
import { computed } from 'vue'
import type { WorkshopCell } from '@/data/contracts/factory'

/**
 * 车间健康卡（厂长 3 秒判绿/黄/红，spec §二）：容器复用 ScreenPanel（渐变发丝边 +
 * 通透面板底，边框已按生产走查收暗），红卡走 accent=red + 缓呼吸辉光；
 * 主管 + 状态灯 + 达成率主数（与实际/计划垂直居中对齐）+ 健康色进度条 +
 * 在产/超期/告警/停机四格。
 */
const props = defineProps<{ cell: WorkshopCell }>()

const tone = computed(
  () =>
    ({ red: 'alarm', yellow: 'idle', green: 'run' })[props.cell.health] as 'alarm' | 'idle' | 'run',
)
const nf = new Intl.NumberFormat('en-US')
</script>

<template>
  <NvScreenPanel
    :accent="cell.health === 'red' ? 'red' : undefined"
    class="whc"
    :class="cell.health"
  >
    <header class="whc-top">
      <div>
        <h4 class="whc-name">{{ cell.name }}</h4>
        <p class="whc-mgr">主管 {{ cell.manager }}</p>
      </div>
      <NvScreenStatusLight :tone="tone" :label="cell.stateLabel" />
    </header>

    <div class="whc-rate">
      <span class="whc-rate-v" :class="cell.health">{{ cell.rate }}<small>%</small></span>
      <span class="whc-rate-d">
        <span
          >实际 <b>{{ nf.format(cell.actualQty) }}</b></span
        >
        <span
          >计划 <b>{{ nf.format(cell.planQty) }}</b></span
        >
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
  </NvScreenPanel>
</template>

<style scoped>
.whc {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  min-height: 0;
  height: 100%;
  box-sizing: border-box;
}
/* 红卡：向外缓呼吸辉光（reduced-motion 静止） */
.whc.red::after {
  content: '';
  position: absolute;
  inset: -1px;
  border-radius: inherit;
  pointer-events: none;
  box-shadow: 0 0 18px -6px rgba(239, 90, 99, 0.55);
  animation: whc-pulse 2.6s ease-in-out infinite;
}
@keyframes whc-pulse {
  50% {
    opacity: 0.35;
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
  font-size: 20px;
  font-weight: 600;
  color: #fff;
}
.whc-mgr {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--sb-muted);
}

/* 主数与实际/计划垂直居中对齐（生产走查：baseline 对不齐、右侧字太小） */
.whc-rate {
  display: flex;
  align-items: center;
  gap: 16px;
  margin: 12px 0 10px;
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
  justify-content: center;
  gap: 5px;
  font-size: 14px;
  color: var(--sb-muted);
  font-variant-numeric: tabular-nums;
}
.whc-rate-d b {
  font-weight: 600;
  color: var(--sb-text-2);
}

/* 达成率进度条：健康色随卡（绿卡走数据青色，黄/红卡语义色） */
.whc-bar {
  height: 6px;
  border-radius: 3px;
  background: rgba(255, 255, 255, 0.07);
  overflow: hidden;
  margin: 0 0 10px;
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
  font-size: 12.5px;
  color: var(--sb-muted);
}
.whc-minis dd {
  margin: 3px 0 0;
  font-size: 19px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--sb-text);
}
.whc-minis dd.bad {
  color: var(--sb-red);
}
.whc-minis dd.warn {
  color: var(--sb-amber);
}

@media (prefers-reduced-motion: reduce) {
  .whc.red::after {
    animation: none;
  }
  .whc-bar i {
    transition: none;
  }
}
</style>
