<script setup lang="ts">
import { Calendar, Clock, Filter, Menu, Monitor } from '@lucide/vue'

/**
 * Screen — big-board header bar. A left-aligned title and a right-aligned tool
 * cluster (clock / weekday / line filter / screen name, each a lucide icon + a
 * short label, closed by a menu glyph), seated on a bottom hairline. Pure props;
 * any tool whose value is absent is simply dropped, so a bare header still reads.
 */
withDefaults(
  defineProps<{
    /** Board title, e.g. 智能工厂 MES 运营看板. */
    title?: string
    /** Wall-clock string, e.g. 2024-06-12 10:24:36. */
    time?: string
    /** Date / weekday, e.g. 星期三. */
    date?: string
    /** Active line filter, e.g. 全部产线. */
    line?: string
    /** Screen / station name, e.g. 中央控制室大屏 01. */
    screen?: string
  }>(),
  {
    title: '智能工厂 MES 运营看板',
    time: '2024-06-12 10:24:36',
    date: '星期三',
    line: '全部产线',
    screen: '中央控制室大屏 01',
  },
)
</script>

<template>
  <header class="nv-scr-hd">
    <div class="nv-scr-hd-left">
      <svg class="nv-scr-hd-core" viewBox="0 0 28 28" aria-hidden="true">
        <polygon points="14,2.5 24.5,8.25 24.5,19.75 14,25.5 3.5,19.75 3.5,8.25" />
        <rect
          class="nv-scr-hd-core-dot"
          x="10.9"
          y="10.9"
          width="6.2"
          height="6.2"
          transform="rotate(45 14 14)"
        />
      </svg>
      <h1 class="nv-scr-hd-t">{{ title }}</h1>
    </div>
    <div class="nv-scr-hd-tools">
      <span v-if="time"><Clock :size="15" />{{ time }}</span>
      <span v-if="date"><Calendar :size="15" />{{ date }}</span>
      <span v-if="line"><Filter :size="15" />{{ line }}</span>
      <span v-if="screen"><Monitor :size="15" />{{ screen }}</span>
      <Menu class="nv-scr-hd-menu" :size="18" />
    </div>
  </header>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-hd {
    position: relative;
    height: 70px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    color: var(--nv-scr-text);
    font-variant-numeric: tabular-nums;
  }
  /* 底线：整条极淡发丝，仅标题块下方一段亮起（局部光，不做整条亮线） */
  .nv-scr-hd::after {
    content: '';
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    height: 1px;
    background: rgba(255, 255, 255, 0.06);
  }
  .nv-scr-hd-left {
    position: relative;
    align-self: stretch;
    display: flex;
    align-items: center;
    gap: 15px;
  }
  /* 亮段跟随标题块宽度自适应，右端渐隐并落一枚菱形节点 */
  .nv-scr-hd-left::after {
    content: '';
    position: absolute;
    left: 0;
    right: -56px;
    bottom: 0;
    height: 1px;
    z-index: 1;
    background: linear-gradient(
      90deg,
      rgba(126, 190, 255, 0.55),
      rgba(126, 190, 255, 0.25) 70%,
      transparent
    );
  }
  .nv-scr-hd-left::before {
    content: '';
    position: absolute;
    right: -59px;
    bottom: -2.5px;
    width: 6px;
    height: 6px;
    transform: rotate(45deg);
    background: var(--nv-scr-cyan);
    box-shadow: 0 0 8px rgba(74, 166, 238, 0.7);
  }
  /* 页头核心徽标：六边形线框 + 中心菱点缓呼吸（与面板斜切块小标题分层） */
  .nv-scr-hd-core {
    width: 28px;
    height: 28px;
    flex: none;
  }
  .nv-scr-hd-core polygon {
    fill: none;
    stroke: rgba(126, 190, 255, 0.6);
    stroke-width: 1.5;
  }
  .nv-scr-hd-core-dot {
    fill: var(--nv-scr-cyan);
    filter: drop-shadow(0 0 5px rgba(74, 166, 238, 0.8));
    animation: nv-scr-hd-core 3.6s ease-in-out infinite;
  }
  @keyframes nv-scr-hd-core {
    50% {
      opacity: 0.4;
    }
  }
  .nv-scr-hd-t {
    font-size: 34px;
    font-weight: 800;
    letter-spacing: 0.08em;
    margin: 0;
    color: #fff;
    text-shadow: 0 0 26px rgba(110, 190, 255, 0.45);
  }
  .nv-scr-hd-tools {
    display: flex;
    align-items: center;
    gap: 28px;
    color: var(--nv-scr-text-2);
    font-size: 14px;
  }
  .nv-scr-hd-tools span {
    display: inline-flex;
    align-items: center;
    gap: 8px;
  }
  .nv-scr-hd-tools svg {
    color: var(--nv-scr-muted);
  }
  .nv-scr-hd-menu {
    cursor: default;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-hd-core-dot {
      animation: none;
    }
  }
}
</style>
