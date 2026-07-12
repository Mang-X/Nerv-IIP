<script setup lang="ts">
import type { Component } from 'vue'
import { computed } from 'vue'
import {
  Activity,
  AlertTriangle,
  ClipboardCheck,
  ClipboardList,
  ListChecks,
  ShieldCheck,
  Zap,
} from 'lucide-vue-next'

/** One KPI cell: a lucide icon in a glow tile (or a ring), a value, and a label. */
interface Kpi {
  /** Lucide component for the icon tile — omitted when `ring` is set. */
  icon?: Component
  value: string
  label: string
  /** Colors the icon tile + value: cyan (default) / amber warn / green ok. */
  tone?: 'cyan' | 'amber' | 'green'
  /** 0–100 — renders a progress ring instead of an icon tile (e.g. 良品率). */
  ring?: number
}

/**
 * Screen — bottom KPI strip. A single bar of N cells separated by hairline rules;
 * each cell pairs a lucide icon (rounded, faintly glowing tile) with a value and
 * label. One cell may use a progress ring in place of the icon — for a rate such
 * as 良品率. Tone keys the accent color. Driven by `items`.
 */
const props = withDefaults(defineProps<{ items?: Kpi[] }>(), {
  items: () => [
    { icon: ClipboardList, value: '24', label: '工单总数' },
    { icon: ListChecks, value: '8', label: '进行中' },
    { icon: ClipboardCheck, value: '16', label: '已完成' },
    { value: '97.3%', label: '良品率', tone: 'cyan', ring: 97.3 },
    { icon: AlertTriangle, value: '36', label: '不良数', tone: 'amber' },
    { icon: Zap, value: '1,284 kWh', label: '能耗电量' },
    { icon: ShieldCheck, value: '128 天', label: '安全运行' },
    { icon: Activity, value: '正常', label: '系统状态', tone: 'green' },
  ],
})

const R = 16.5
const CIRC = 2 * Math.PI * R

const cells = computed(() =>
  props.items.map((it) => ({
    ...it,
    dashoffset: it.ring != null ? CIRC * (1 - Math.max(0, Math.min(100, it.ring)) / 100) : 0,
  })),
)
</script>

<template>
  <div class="nv-scr-kpis">
    <div v-for="(k, i) in cells" :key="i" class="nv-scr-kpi">
      <svg
        v-if="k.ring != null"
        width="42"
        height="42"
        viewBox="0 0 42 42"
        class="nv-scr-kpi-ring"
        role="img"
        :aria-label="`${k.label} ${k.value}`"
      >
        <circle class="nv-scr-kpi-track" cx="21" cy="21" :r="R" fill="none" stroke-width="4" />
        <circle
          class="nv-scr-kpi-arc"
          cx="21"
          cy="21"
          :r="R"
          fill="none"
          stroke-width="4"
          stroke-linecap="round"
          :stroke-dasharray="CIRC"
          :stroke-dashoffset="k.dashoffset"
          transform="rotate(-90 21 21)"
        />
      </svg>
      <span v-else class="nv-scr-kpi-ic" :class="k.tone ?? 'neutral'">
        <component :is="k.icon" :size="19" />
      </span>
      <div>
        <div class="nv-scr-kpi-v" :class="k.tone ?? 'neutral'">{{ k.value }}</div>
        <div class="nv-scr-kpi-k">{{ k.label }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-kpis {
    display: flex;
    align-items: center;
    overflow-x: auto;
    overflow-y: hidden;
    /* hide the scrollbar entirely so it never reserves height — content stays
     vertically centered. The strip still scrolls by wheel / trackpad / touch
     when it overflows a narrow board (it won't overflow at full board width). */
    scrollbar-width: none;
    border: 1px solid var(--nv-scr-line);
    border-radius: 8px;
    background: linear-gradient(180deg, var(--nv-scr-panel-a), var(--nv-scr-panel-b));
    padding: 15px 0;
    box-shadow: inset 0 1px 0 var(--nv-scr-highlight);
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-kpis::-webkit-scrollbar {
    display: none;
  }
  .nv-scr-kpi {
    display: flex;
    flex: 1 0 auto;
    min-width: 158px;
    align-items: center;
    gap: 13px;
    padding: 0 22px;
    position: relative;
    white-space: nowrap;
  }
  .nv-scr-kpi + .nv-scr-kpi::before {
    content: '';
    position: absolute;
    left: 0;
    top: 5px;
    bottom: 5px;
    width: 1px;
    background: var(--nv-scr-divider);
  }
  .nv-scr-kpi-ic {
    width: 38px;
    height: 38px;
    border-radius: 8px;
    display: grid;
    place-items: center;
    flex: none;
  }
  .nv-scr-kpi-ic.neutral {
    color: var(--nv-scr-text-2);
    background: rgba(255, 255, 255, 0.04);
    border: 1px solid var(--nv-scr-line-2);
  }
  .nv-scr-kpi-ic.cyan {
    color: var(--nv-scr-cyan);
    background: rgba(0, 229, 255, 0.08);
    border: 1px solid rgba(0, 229, 255, 0.18);
  }
  .nv-scr-kpi-ic.amber {
    color: var(--nv-scr-amber);
    background: rgba(255, 214, 0, 0.08);
    border: 1px solid rgba(255, 214, 0, 0.18);
  }
  .nv-scr-kpi-ic.green {
    color: var(--nv-scr-green);
    background: rgba(0, 230, 118, 0.08);
    border: 1px solid rgba(0, 230, 118, 0.18);
  }
  .nv-scr-kpi-ring {
    flex: none;
    filter: drop-shadow(0 0 3px rgba(0, 229, 255, 0.4));
  }
  .nv-scr-kpi-track {
    stroke: rgba(255, 255, 255, 0.08);
  }
  .nv-scr-kpi-arc {
    stroke: var(--nv-scr-cyan);
  }
  .nv-scr-kpi-v {
    font-size: 22px;
    font-weight: 700;
  }
  .nv-scr-kpi-v.neutral {
    color: var(--nv-scr-text);
  }
  .nv-scr-kpi-v.cyan {
    color: #fff;
  }
  .nv-scr-kpi-v.green {
    color: var(--nv-scr-green);
  }
  /* amber tone keys the icon tile, not the number — keep the value neutral */
  .nv-scr-kpi-v.amber {
    color: var(--nv-scr-text);
  }
  .nv-scr-kpi-k {
    font-size: 12px;
    color: var(--nv-scr-muted);
    margin-top: 2px;
  }
  .nv-scr-kpi-arc {
    transition: stroke-dashoffset 0.6s var(--nv-scr-ease-emphasized);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-kpi-arc {
      transition: none;
    }
  }
}
</style>
