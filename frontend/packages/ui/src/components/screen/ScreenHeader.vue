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
      <i class="sb-hd-glyph" aria-hidden="true" />
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
/* 底线升级：两端微亮的渐变发丝线（替代平灰 border，2026-07 生产走查） */
.sb-hd::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: 1px;
  background: linear-gradient(
    90deg,
    rgba(126, 190, 255, 0.5),
    rgba(255, 255, 255, 0.08) 28%,
    rgba(255, 255, 255, 0.08) 72%,
    rgba(126, 190, 255, 0.3)
  );
}
.sb-hd-left {
  display: flex;
  align-items: center;
  gap: 14px;
}
/* 页头标题的双斜切能量块 */
.sb-hd-glyph {
  position: relative;
  width: 11px;
  height: 26px;
  flex: none;
  border-radius: 2px;
  transform: skewX(-16deg);
  background: linear-gradient(180deg, var(--sb-cyan), rgba(74, 166, 238, 0.3));
  box-shadow: 0 0 14px rgba(74, 166, 238, 0.6);
}
.sb-hd-glyph::after {
  content: '';
  position: absolute;
  left: 15px;
  top: 4px;
  bottom: 4px;
  width: 5px;
  border-radius: 2px;
  background: linear-gradient(180deg, rgba(120, 190, 255, 0.6), rgba(74, 166, 238, 0.12));
}
.sb-hd-t {
  font-size: 31px;
  font-weight: 700;
  letter-spacing: 0.06em;
  margin: 0 0 0 8px;
  color: #fff;
  text-shadow: 0 0 24px rgba(110, 190, 255, 0.42);
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
</style>
