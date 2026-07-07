<script setup lang="ts">
import { Calendar, Clock, Filter, Menu, Monitor } from 'lucide-vue-next'

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
  <header class="sb-hd">
    <div class="sb-hd-left">
      <svg class="sb-hd-core" viewBox="0 0 28 28" aria-hidden="true">
        <polygon points="14,2.5 24.5,8.25 24.5,19.75 14,25.5 3.5,19.75 3.5,8.25" />
        <rect class="sb-hd-core-dot" x="10.9" y="10.9" width="6.2" height="6.2" transform="rotate(45 14 14)" />
      </svg>
      <h1 class="sb-hd-t">{{ title }}</h1>
    </div>
    <div class="sb-hd-tools">
      <span v-if="time"><Clock :size="15" />{{ time }}</span>
      <span v-if="date"><Calendar :size="15" />{{ date }}</span>
      <span v-if="line"><Filter :size="15" />{{ line }}</span>
      <span v-if="screen"><Monitor :size="15" />{{ screen }}</span>
      <Menu class="sb-hd-menu" :size="18" />
    </div>
  </header>
</template>

<style scoped>
.sb-hd {
  position: relative;
  height: 70px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
/* 底线：整条极淡发丝，仅标题块下方一段亮起（局部光，不做整条亮线） */
.sb-hd::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: 1px;
  background: rgba(255, 255, 255, 0.06);
}
.sb-hd-left {
  position: relative;
  align-self: stretch;
  display: flex;
  align-items: center;
  gap: 15px;
}
/* 亮段跟随标题块宽度自适应，右端渐隐并落一枚菱形节点 */
.sb-hd-left::after {
  content: '';
  position: absolute;
  left: 0;
  right: -56px;
  bottom: 0;
  height: 1px;
  z-index: 1;
  background: linear-gradient(90deg, rgba(126, 190, 255, 0.55), rgba(126, 190, 255, 0.25) 70%, transparent);
}
.sb-hd-left::before {
  content: '';
  position: absolute;
  right: -59px;
  bottom: -2.5px;
  width: 6px;
  height: 6px;
  transform: rotate(45deg);
  background: var(--sb-cyan);
  box-shadow: 0 0 8px rgba(74, 166, 238, 0.7);
}
/* 页头核心徽标：六边形线框 + 中心菱点缓呼吸（与面板斜切块小标题分层） */
.sb-hd-core {
  width: 28px;
  height: 28px;
  flex: none;
}
.sb-hd-core polygon {
  fill: none;
  stroke: rgba(126, 190, 255, 0.6);
  stroke-width: 1.5;
}
.sb-hd-core-dot {
  fill: var(--sb-cyan);
  filter: drop-shadow(0 0 5px rgba(74, 166, 238, 0.8));
  animation: sb-hd-core 3.6s ease-in-out infinite;
}
@keyframes sb-hd-core {
  50% {
    opacity: 0.4;
  }
}
.sb-hd-t {
  font-size: 34px;
  font-weight: 800;
  letter-spacing: 0.08em;
  margin: 0;
  color: #fff;
  text-shadow: 0 0 26px rgba(110, 190, 255, 0.45);
}
.sb-hd-tools {
  display: flex;
  align-items: center;
  gap: 28px;
  color: var(--sb-text-2);
  font-size: 14px;
}
.sb-hd-tools span {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.sb-hd-tools svg {
  color: var(--sb-muted);
}
.sb-hd-menu {
  cursor: default;
}
@media (prefers-reduced-motion: reduce) {
  .sb-hd-core-dot {
    animation: none;
  }
}
</style>
