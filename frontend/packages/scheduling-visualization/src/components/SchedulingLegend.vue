<script setup lang="ts">
import { AlertTriangle, CalendarClock, ChevronDown, GitBranch, Gauge, TimerReset } from 'lucide-vue-next'
import { shallowRef } from 'vue'

interface LegendItem {
  key: string
  label: string
  tone: 'blue' | 'slate' | 'green' | 'orange' | 'red' | 'purple'
  icon: typeof TimerReset
}

interface LegendGroup {
  key: string
  title: string
  items: LegendItem[]
}

const collapsed = shallowRef(false)
const groups: LegendGroup[] = [
  {
    key: 'timeline',
    title: 'Timeline',
    items: [
      { key: 'today', label: 'Today line', tone: 'blue', icon: TimerReset },
      { key: 'baseline', label: 'Baseline', tone: 'slate', icon: TimerReset },
      { key: 'link', label: 'Dependency link', tone: 'slate', icon: GitBranch },
    ],
  },
  {
    key: 'resource',
    title: 'Resource',
    items: [
      { key: 'capacity', label: 'Capacity band', tone: 'green', icon: Gauge },
      { key: 'calendar', label: 'Calendar / downtime', tone: 'orange', icon: CalendarClock },
    ],
  },
  {
    key: 'risk',
    title: 'Risk',
    items: [
      { key: 'conflict', label: 'Conflict', tone: 'red', icon: AlertTriangle },
    ],
  },
]
</script>

<template>
  <div class="scheduling-legend" data-test="scheduling-legend" aria-label="Scheduling visual legend">
    <button
      class="scheduling-legend__toggle"
      type="button"
      :aria-expanded="!collapsed"
      @click="collapsed = !collapsed"
    >
      <ChevronDown :class="{ 'scheduling-legend__chevron--collapsed': collapsed }" aria-hidden="true" />
      Legend
    </button>

    <div v-if="!collapsed" class="scheduling-legend__groups">
      <section v-for="group in groups" :key="group.key" class="scheduling-legend__group">
        <p class="scheduling-legend__group-title">{{ group.title }}</p>
        <span
          v-for="item in group.items"
          :key="item.key"
          class="scheduling-legend__item"
          :class="`scheduling-legend__item--${item.tone}`"
        >
          <component :is="item.icon" aria-hidden="true" />
          <span class="scheduling-legend__swatch" aria-hidden="true" />
          <span>{{ item.label }}</span>
        </span>
      </section>
    </div>
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

.scheduling-legend__toggle {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  min-height: 26px;
  padding: 3px 8px;
  border: 1px solid rgba(203, 213, 225, 0.82);
  border-radius: 7px;
  background: #f8fafc;
  color: #0f172a;
  cursor: pointer;
  font: inherit;
  font-size: 12px;
  font-weight: 750;
}

.scheduling-legend__toggle svg {
  width: 13px;
  height: 13px;
}

.scheduling-legend__chevron--collapsed {
  transform: rotate(-90deg);
}

.scheduling-legend__groups {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.scheduling-legend__group {
  display: inline-flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 5px;
}

.scheduling-legend__group-title {
  margin: 0 2px 0 0;
  color: #64748b;
  font-size: 11px;
  font-weight: 750;
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
