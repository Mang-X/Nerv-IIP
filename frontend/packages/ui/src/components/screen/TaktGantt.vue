<script setup lang="ts">
import ScreenPanel from './ScreenPanel.vue'

/** A single colored segment of a line's timeline: [tone, width-percent]. */
type Seg = ['run' | 'idle' | 'stop' | 'alarm', number]

/**
 * Screen — takt (节拍) gantt. One row per production line: a right-aligned name
 * and a single bar split into colored segments (running cyan / idle amber /
 * stopped gray / alarm red). A shared time axis sits above the bars and a legend
 * keys the four tones. Driven entirely by `rows` + `axis`.
 */
withDefaults(
  defineProps<{
    /** One entry per line: name + ordered segments whose widths sum to ~100. */
    rows?: { name: string; segs: Seg[] }[]
    /** Time-axis ticks spread across the bar area, e.g. 09:30 … 10:30. */
    axis?: string[]
    title?: string
  }>(),
  {
    title: '节拍 Takt 58s',
    axis: () => ['09:30', '09:40', '09:50', '10:00', '10:10', '10:20', '10:30'],
    rows: () => [
      { name: '焊接线 A', segs: [['run', 34], ['idle', 6], ['run', 60]] },
      { name: '装配线 B', segs: [['idle', 22], ['stop', 8], ['run', 18], ['idle', 30], ['run', 22]] },
      { name: 'CNC 线 C', segs: [['alarm', 14], ['stop', 20], ['run', 12], ['alarm', 10], ['stop', 14], ['run', 12], ['alarm', 18]] },
    ],
  },
)

const LEGEND = [
  { tone: 'run', label: '运行' },
  { tone: 'idle', label: '待机' },
  { tone: 'stop', label: '停机' },
  { tone: 'alarm', label: '报警' },
] as const
</script>

<template>
  <ScreenPanel :title="title" class="sb-tg">
    <template #extra>
      <div class="sb-tg-legend">
        <span v-for="l in LEGEND" :key="l.tone"><i :class="l.tone" />{{ l.label }}</span>
      </div>
    </template>
    <div class="sb-tg-axis">
      <span v-for="(t, i) in axis" :key="i">{{ t }}</span>
    </div>
    <div v-for="r in rows" :key="r.name" class="sb-tg-row">
      <span class="sb-tg-nm">{{ r.name }}</span>
      <div class="sb-tg-bar">
        <span
          v-for="(s, i) in r.segs"
          :key="i"
          class="sb-tg-seg"
          :class="s[0]"
          :style="{ width: s[1] + '%' }"
        />
      </div>
    </div>
  </ScreenPanel>
</template>

<style scoped>
.sb-tg-legend {
  display: flex;
  gap: 18px;
  font-size: 12px;
  color: var(--sb-muted);
}
.sb-tg-legend span {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.sb-tg-legend i {
  width: 11px;
  height: 11px;
  border-radius: 2px;
}
.sb-tg-seg.run,
.sb-tg-legend i.run {
  background: var(--sb-cyan);
}
.sb-tg-seg.idle,
.sb-tg-legend i.idle {
  background: var(--sb-amber);
}
.sb-tg-seg.stop,
.sb-tg-legend i.stop {
  background: #37445a;
}
.sb-tg-seg.alarm,
.sb-tg-legend i.alarm {
  background: var(--sb-red);
}
.sb-tg-axis {
  display: flex;
  justify-content: space-between;
  color: var(--sb-faint);
  font-size: 11px;
  margin: 6px 0 9px;
  padding-left: 78px;
  font-variant-numeric: tabular-nums;
}
.sb-tg-row {
  display: flex;
  align-items: center;
  gap: 14px;
  margin: 8px 0;
}
.sb-tg-nm {
  width: 64px;
  font-size: 12px;
  color: var(--sb-muted);
  text-align: right;
  flex: none;
}
.sb-tg-bar {
  flex: 1;
  height: 16px;
  border-radius: 3px;
  overflow: hidden;
  display: flex;
  box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.04);
}
.sb-tg-seg {
  height: 100%;
}
</style>
