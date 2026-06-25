<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — status light. A breathing glow dot in a semantic color, with an
 * optional label. Operational aliases (run/idle/alarm) map onto the palette
 * (green/amber/red); raw colors are also accepted. State never rests on color
 * alone — pair it with the label. Breathing stops under reduced-motion.
 */
const props = withDefaults(
  defineProps<{
    /** Operational alias or a raw palette color. */
    tone?: 'run' | 'idle' | 'alarm' | 'cyan' | 'green' | 'amber' | 'red'
    /** Optional text beside the dot, e.g. 运行中. */
    label?: string
  }>(),
  { tone: 'run' },
)

/** Fold operational aliases onto the color palette. */
const color = computed(
  () =>
    (
      {
        run: 'green',
        idle: 'amber',
        alarm: 'red',
        cyan: 'cyan',
        green: 'green',
        amber: 'amber',
        red: 'red',
      } as const
    )[props.tone],
)
</script>

<template>
  <span class="sb-sl" :class="color">
    <span class="sb-sl-dot" />
    <span v-if="label" class="sb-sl-label">{{ label }}</span>
  </span>
</template>

<style scoped>
.sb-sl {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: var(--sb-text-2);
}
.sb-sl-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex: none;
  animation: sb-sl-breathe 2s ease-in-out infinite;
}
.sb-sl.cyan .sb-sl-dot {
  background: var(--sb-cyan);
  box-shadow: 0 0 9px var(--sb-cyan);
}
.sb-sl.green .sb-sl-dot {
  background: var(--sb-green);
  box-shadow: 0 0 9px var(--sb-green);
}
.sb-sl.amber .sb-sl-dot {
  background: var(--sb-amber);
  box-shadow: 0 0 9px var(--sb-amber);
}
.sb-sl.red .sb-sl-dot {
  background: var(--sb-red);
  box-shadow: 0 0 9px var(--sb-red);
}
.sb-sl-label {
  line-height: 1;
}
@media (prefers-reduced-motion: reduce) {
  .sb-sl-dot {
    animation: none;
  }
}
@keyframes sb-sl-breathe {
  0%,
  100% {
    opacity: 0.55;
  }
  50% {
    opacity: 1;
  }
}
</style>
