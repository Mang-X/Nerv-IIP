<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Screen — base panel for the big-board surface: translucent gradient body,
 * hairline border, a faint top sheen + glassy diagonal highlight. Optional title
 * row and a colored top accent edge (status panels). Built on the independent
 * `--sb-*` tokens. The container every other screen module sits in.
 */
defineProps<{
  title?: string
  /** Colored top accent edge — the board's status signature (cyan/green/amber/red). */
  accent?: 'cyan' | 'green' | 'amber' | 'red'
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <section :class="cn('sb-panel', $props.class)">
    <span v-if="accent" class="sb-panel-accent" :class="accent" />
    <div v-if="title || $slots.extra" class="sb-panel-h">
      <span class="sb-panel-t">{{ title }}<slot name="title-extra" /></span>
      <div v-if="$slots.extra" class="sb-panel-extra"><slot name="extra" /></div>
    </div>
    <slot />
  </section>
</template>

<style scoped>
.sb-panel {
  position: relative;
  background: linear-gradient(180deg, var(--sb-panel-a), var(--sb-panel-b));
  border: 1px solid var(--sb-line);
  border-radius: var(--sb-radius);
  padding: 17px 20px;
  box-shadow: var(--sb-sheen);
  overflow: hidden;
  color: var(--sb-text);
}
/* glassy diagonal highlight — material feel without backdrop-filter */
.sb-panel::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: var(--sb-radius);
  background: linear-gradient(140deg, rgba(125, 170, 255, 0.05), transparent 40%);
  pointer-events: none;
}
.sb-panel > * {
  position: relative;
}
.sb-panel-h {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}
.sb-panel-t {
  font-size: 16px;
  font-weight: 500;
  color: var(--sb-text-2);
  display: inline-flex;
  align-items: center;
  gap: 6px;
}
.sb-panel-extra {
  font-size: 13px;
  color: var(--sb-muted);
}
.sb-panel-accent {
  position: absolute;
  top: 0;
  left: 14px;
  right: 14px;
  height: 2px;
  border-radius: 2px;
}
.sb-panel-accent.cyan {
  background: linear-gradient(90deg, transparent, var(--sb-cyan), transparent);
  box-shadow: 0 0 10px var(--sb-cyan-dim);
}
.sb-panel-accent.green {
  background: linear-gradient(90deg, transparent, var(--sb-green), transparent);
  box-shadow: 0 0 10px rgba(0, 230, 118, 0.45);
}
.sb-panel-accent.amber {
  background: linear-gradient(90deg, transparent, var(--sb-amber), transparent);
  box-shadow: 0 0 10px rgba(255, 214, 0, 0.45);
}
.sb-panel-accent.red {
  background: linear-gradient(90deg, transparent, var(--sb-red), transparent);
  box-shadow: 0 0 10px rgba(255, 23, 68, 0.45);
}
</style>
