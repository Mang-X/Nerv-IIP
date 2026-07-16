<script setup lang="ts">
import { NvSparkline, NvScreenStatusLight } from '@nerv-iip/ui'
import { Factory } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import type { LineSummaryCard } from '@/data/contracts/line'

/**
 * 车间产线状态墙卡（spec §三 一眼焦点②）：一线一卡 —— 状态灯 + 当班产量
 * plan vs actual + 达成/节拍偏差 + 小时趋势 + 设备点排（与设备屏同源），
 * 点击进入单线大屏。数据直接吃产线屏 LineSummaryCard（数字精确同源）。
 * 报警卡红发丝边 + 缓脉冲（辉光只给活异常）；正常卡保持中性。
 */
const props = defineProps<{ card: LineSummaryCard }>()

const tone = computed(() =>
  props.card.state === 'alarm'
    ? ('alarm' as const)
    : props.card.state === 'attention'
      ? ('idle' as const)
      : ('run' as const),
)
const sparkColor = computed(() =>
  props.card.state === 'alarm'
    ? 'var(--nv-scr-red)'
    : props.card.state === 'attention'
      ? 'var(--nv-scr-amber)'
      : 'var(--nv-scr-cyan)',
)
const nf = new Intl.NumberFormat('en-US')
</script>

<template>
  <RouterLink :to="`/line/${card.id}`" class="wlc-link">
    <article class="wlc" :class="card.state">
      <header class="wlc-top">
        <NvScreenStatusLight :tone="tone" :label="card.stateLabel" />
        <span v-if="card.offlineDevices > 0" class="wlc-off">{{ card.offlineDevices }} 台失联</span>
        <span v-if="card.currentWo" class="wlc-wo">{{ card.currentWo }}</span>
      </header>

      <h3 class="wlc-name">
        <span class="wlc-ic"><Factory :size="16" :stroke-width="1.6" /></span>{{ card.name }}
      </h3>

      <div class="wlc-nums">
        <div class="wlc-out">
          <dt>当班产量（件）</dt>
          <dd>
            {{ nf.format(card.output.good) }}<small>/ {{ nf.format(card.output.plan) }}</small>
          </dd>
        </div>
        <div>
          <dt>达成</dt>
          <dd :class="{ bad: card.achievement < 85 }">{{ card.achievement }}<small>%</small></dd>
        </div>
        <div>
          <dt>节拍偏差</dt>
          <dd
            :class="{
              bad: card.taktDeviationPct > 8,
              warn: card.taktDeviationPct > 0 && card.taktDeviationPct <= 8,
            }"
          >
            {{ card.taktDeviationPct > 0 ? '+' : '' }}{{ card.taktDeviationPct }}<small>%</small>
          </dd>
        </div>
      </div>

      <div class="wlc-spark">
        <NvSparkline :data="card.hourly" area :color="sparkColor" />
      </div>

      <footer class="wlc-foot">
        <span class="wlc-dots" :aria-label="`设备 ${card.deviceDots.length} 台`">
          <i v-for="(d, i) in card.deviceDots" :key="i" class="wlc-dot" :class="d" />
          <em class="wlc-dots-n">{{ card.deviceDots.length }} 台</em>
        </span>
        <span class="wlc-alert" :class="card.state">{{ card.alert ?? '作业平稳' }}</span>
      </footer>
    </article>
  </RouterLink>
</template>

<style scoped>
@layer app {
  .wlc-link {
    display: block;
    min-width: 0;
    min-height: 0;
    flex: 1 1 0;
    text-decoration: none;
    border-radius: var(--nv-scr-radius);
  }
  .wlc-link:focus-visible {
    outline: none;
    box-shadow:
      0 0 0 2px var(--nv-scr-bg),
      0 0 0 4px var(--nv-scr-cyan-dim);
  }
  .wlc {
    height: 100%;
    box-sizing: border-box;
    overflow: hidden;
    display: flex;
    flex-direction: column;
    padding: 13px 17px 11px;
    border-radius: var(--nv-scr-radius);
    background: linear-gradient(180deg, var(--nv-scr-panel-a), var(--nv-scr-panel-b));
    border: 1px solid var(--nv-scr-line);
    border-top-color: rgba(255, 255, 255, 0.09);
    transition:
      border-color 0.18s var(--nv-scr-ease),
      transform 0.12s var(--nv-scr-ease);
  }
  .wlc-link:hover .wlc {
    border-color: rgba(135, 208, 255, 0.3);
  }
  .wlc-link:active .wlc {
    transform: scale(0.985);
  }
  /* 报警卡：红发丝边 + 缓脉冲外辉（辉光只给活异常） */
  .wlc.alarm {
    position: relative;
    border-color: rgba(239, 90, 99, 0.4);
  }
  .wlc.alarm::after {
    content: '';
    position: absolute;
    inset: -1px;
    border-radius: inherit;
    pointer-events: none;
    box-shadow: 0 0 16px -4px rgba(239, 90, 99, 0.6);
    animation: wlc-alarm 1.8s ease-in-out infinite;
  }
  @keyframes wlc-alarm {
    50% {
      opacity: 0.25;
    }
  }

  .wlc-top {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .wlc-wo {
    margin-left: auto;
    font-family: ui-monospace, monospace;
    font-size: 12px;
    color: var(--nv-scr-muted);
  }
  .wlc-off {
    padding: 2px 8px;
    border-radius: 5px;
    border: 1px dashed rgba(255, 255, 255, 0.24);
    background: repeating-linear-gradient(
      -45deg,
      rgba(255, 255, 255, 0.04) 0 6px,
      transparent 6px 12px
    );
    font-size: 11.5px;
    color: var(--nv-scr-muted);
  }
  .wlc-name {
    margin: 7px 0 0;
    font-size: 21px;
    font-weight: 700;
    color: #fff;
    letter-spacing: 0.04em;
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
  /* 线名 leading 图标：发丝级去饱和线性符号（无填充块/边框/发光） */
  .wlc-ic {
    flex: none;
    display: inline-flex;
    color: var(--nv-scr-muted);
  }

  .wlc-nums {
    display: grid;
    grid-template-columns: 1.35fr 0.8fr 1fr;
    gap: 10px;
    margin: 8px 0 0;
  }
  .wlc-nums dt {
    font-size: 12px;
    color: var(--nv-scr-muted);
  }
  .wlc-nums dd {
    margin: 3px 0 0;
    font-size: 23px;
    font-weight: 700;
    font-variant-numeric: tabular-nums;
    color: var(--nv-scr-text);
    line-height: 1;
  }
  .wlc-nums dd small {
    font-size: 13px;
    font-weight: 600;
    margin-left: 2px;
    color: var(--nv-scr-muted);
  }
  .wlc-nums dd.warn {
    color: var(--nv-scr-amber);
  }
  .wlc-nums dd.bad {
    color: var(--nv-scr-red);
  }

  .wlc-spark {
    flex: 1;
    min-height: 22px;
    margin: 8px 0 6px;
  }

  .wlc-foot {
    display: flex;
    align-items: center;
    gap: 12px;
    min-width: 0;
  }
  .wlc-dots {
    display: inline-flex;
    align-items: center;
    gap: 5px;
    flex: none;
  }
  .wlc-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--nv-scr-faint);
  }
  .wlc-dot.run {
    background: var(--nv-scr-green);
    box-shadow: 0 0 6px var(--nv-scr-green);
  }
  .wlc-dot.idle {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 6px var(--nv-scr-amber);
  }
  .wlc-dot.alarm {
    background: var(--nv-scr-red);
    box-shadow: 0 0 6px var(--nv-scr-red);
  }
  .wlc-dot.down {
    background: var(--nv-scr-muted);
  }
  .wlc-dots-n {
    margin-left: 3px;
    font-style: normal;
    font-size: 11.5px;
    color: var(--nv-scr-faint);
    font-variant-numeric: tabular-nums;
  }
  .wlc-alert {
    flex: 1;
    min-width: 0;
    text-align: right;
    font-size: 12.5px;
    color: var(--nv-scr-faint);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .wlc-alert.alarm {
    color: var(--nv-scr-red);
  }
  .wlc-alert.attention {
    color: var(--nv-scr-amber);
  }

  @media (prefers-reduced-motion: reduce) {
    .wlc {
      transition: none;
    }
    .wlc.alarm::after {
      animation: none;
    }
  }
}
</style>
