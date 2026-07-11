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
      {
        name: '焊接线 A',
        segs: [
          ['run', 34],
          ['idle', 6],
          ['run', 60],
        ],
      },
      {
        name: '装配线 B',
        segs: [
          ['idle', 22],
          ['stop', 8],
          ['run', 18],
          ['idle', 30],
          ['run', 22],
        ],
      },
      {
        name: 'CNC 线 C',
        segs: [
          ['alarm', 14],
          ['stop', 20],
          ['run', 12],
          ['alarm', 10],
          ['stop', 14],
          ['run', 12],
          ['alarm', 18],
        ],
      },
    ],
  },
)

const LEGEND = [
  { tone: 'run', label: '运行' },
  { tone: 'idle', label: '待机' },
  { tone: 'stop', label: '停机' },
  { tone: 'alarm', label: '报警' },
] as const

const TONE_LABEL = { run: '运行', idle: '待机', stop: '停机', alarm: '报警' } as const
</script>

<template>
  <ScreenPanel :title="title" class="nv-scr-tg">
    <template #extra>
      <div class="nv-scr-tg-legend">
        <span v-for="l in LEGEND" :key="l.tone"><i :class="l.tone" />{{ l.label }}</span>
      </div>
    </template>
    <div class="nv-scr-tg-axis">
      <span v-for="(t, i) in axis" :key="i">{{ t }}</span>
    </div>
    <div v-for="r in rows" :key="r.name" class="nv-scr-tg-row">
      <span class="nv-scr-tg-nm">{{ r.name }}</span>
      <div class="nv-scr-tg-bar">
        <span
          v-for="(s, i) in r.segs"
          :key="i"
          class="nv-scr-tg-seg"
          :class="s[0]"
          :style="{ width: s[1] + '%' }"
          :title="`${r.name} · ${TONE_LABEL[s[0]]} ${s[1]}%`"
        />
      </div>
    </div>
  </ScreenPanel>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-tg-legend {
    display: flex;
    gap: 18px;
    font-size: 12px;
    color: var(--nv-scr-muted);
  }
  .nv-scr-tg-legend span {
    display: inline-flex;
    align-items: center;
    gap: 6px;
  }
  .nv-scr-tg-legend i {
    width: 11px;
    height: 11px;
    border-radius: 2px;
  }
  .nv-scr-tg-seg.run,
  .nv-scr-tg-legend i.run {
    background: var(--nv-scr-cyan);
  }
  .nv-scr-tg-seg.idle,
  .nv-scr-tg-legend i.idle {
    background: var(--nv-scr-amber);
  }
  .nv-scr-tg-seg.stop,
  .nv-scr-tg-legend i.stop {
    background: #37445a;
  }
  .nv-scr-tg-seg.alarm,
  .nv-scr-tg-legend i.alarm {
    background: var(--nv-scr-red);
  }
  .nv-scr-tg-axis {
    display: flex;
    justify-content: space-between;
    color: var(--nv-scr-faint);
    font-size: 11px;
    margin: 6px 0 9px;
    padding-left: 78px;
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-tg-row {
    display: flex;
    align-items: center;
    gap: 14px;
    margin: 8px 0;
  }
  .nv-scr-tg-nm {
    width: 64px;
    font-size: 12px;
    color: var(--nv-scr-muted);
    text-align: right;
    flex: none;
  }
  .nv-scr-tg-bar {
    flex: 1;
    height: 16px;
    border-radius: 3px;
    overflow: hidden;
    display: flex;
    box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.04);
  }
  .nv-scr-tg-seg {
    height: 100%;
    transition: filter 0.15s var(--nv-scr-ease);
  }
  .nv-scr-tg-seg:hover {
    filter: brightness(1.3);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-tg-seg {
      transition: none;
    }
  }
}
</style>
