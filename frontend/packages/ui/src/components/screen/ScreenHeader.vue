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
    <h1 class="sb-hd-t">{{ title }}</h1>
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
  height: 66px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--sb-line);
  color: var(--sb-text);
  font-variant-numeric: tabular-nums;
}
.sb-hd-t {
  font-size: 26px;
  font-weight: 600;
  letter-spacing: 0.03em;
  margin: 0;
}
.sb-hd-tools {
  display: flex;
  align-items: center;
  gap: 28px;
  color: var(--sb-muted);
  font-size: 14px;
}
.sb-hd-tools span {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.sb-hd-tools svg {
  color: var(--sb-faint);
}
.sb-hd-menu {
  cursor: default;
}
</style>
