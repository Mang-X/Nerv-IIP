<script setup lang="ts">
import { AlertTriangle, CalendarClock, GitBranch, Gauge, TimerReset } from 'lucide-vue-next'

interface LegendItem {
  key: string
  label: string
  tone: 'blue' | 'slate' | 'green' | 'orange' | 'red' | 'purple'
  icon: typeof TimerReset
}

const items: LegendItem[] = [
  { key: 'today', label: 'Today line', tone: 'blue', icon: TimerReset },
  { key: 'baseline', label: 'Baseline', tone: 'slate', icon: TimerReset },
  { key: 'capacity', label: 'Capacity band', tone: 'green', icon: Gauge },
  { key: 'conflict', label: 'Conflict', tone: 'red', icon: AlertTriangle },
  { key: 'link', label: 'Dependency link', tone: 'slate', icon: GitBranch },
  { key: 'calendar', label: 'Calendar / downtime', tone: 'orange', icon: CalendarClock },
]
</script>

<template>
  <div class="scheduling-legend" data-test="scheduling-legend" aria-label="Scheduling visual legend">
    <span
      v-for="item in items"
      :key="item.key"
      class="scheduling-legend__item"
      :class="`scheduling-legend__item--${item.tone}`"
    >
      <component :is="item.icon" aria-hidden="true" />
      <span class="scheduling-legend__swatch" aria-hidden="true" />
      <span>{{ item.label }}</span>
    </span>
  </div>
</template>

<style scoped>
.scheduling-legend {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  padding: 8px 10px;
  border: 1px solid hsl(var(--border, 214 32% 91%));
  border-radius: 8px;
  background: hsl(var(--background, 0 0% 100%));
}

.scheduling-legend__item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 24px;
  padding: 3px 8px;
  border: 1px solid rgba(203, 213, 225, 0.82);
  border-radius: 999px;
  color: #334155;
  font-size: 12px;
  font-weight: 650;
}

.scheduling-legend__item svg {
  width: 13px;
  height: 13px;
}

.scheduling-legend__swatch {
  width: 18px;
  height: 4px;
  border-radius: 999px;
  background: currentColor;
}

.scheduling-legend__item--blue {
  color: #0369a1;
}

.scheduling-legend__item--slate {
  color: #475569;
}

.scheduling-legend__item--green {
  color: #15803d;
}

.scheduling-legend__item--orange {
  color: #c2410c;
}

.scheduling-legend__item--red {
  color: #b91c1c;
}

.scheduling-legend__item--purple {
  color: #6d28d9;
}
</style>
