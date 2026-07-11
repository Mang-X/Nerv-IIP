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
      ({
        run: 'green',
        idle: 'amber',
        alarm: 'red',
        cyan: 'cyan',
        green: 'green',
        amber: 'amber',
        red: 'red',
      }) as const
    )[props.tone],
)
</script>

<template>
  <span class="nv-scr-sl" :class="color">
    <span class="nv-scr-sl-dot" />
    <span v-if="label" class="nv-scr-sl-label">{{ label }}</span>
  </span>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-sl {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    font-size: 13px;
    color: var(--nv-scr-text-2);
  }
  .nv-scr-sl-dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    flex: none;
    animation: nv-scr-sl-breathe 2s ease-in-out infinite;
  }
  .nv-scr-sl.cyan .nv-scr-sl-dot {
    background: var(--nv-scr-cyan);
    box-shadow: 0 0 9px var(--nv-scr-cyan);
  }
  .nv-scr-sl.green .nv-scr-sl-dot {
    background: var(--nv-scr-green);
    box-shadow: 0 0 9px var(--nv-scr-green);
  }
  .nv-scr-sl.amber .nv-scr-sl-dot {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 9px var(--nv-scr-amber);
  }
  .nv-scr-sl.red .nv-scr-sl-dot {
    background: var(--nv-scr-red);
    box-shadow: 0 0 9px var(--nv-scr-red);
  }
  .nv-scr-sl-label {
    line-height: 1;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-sl-dot {
      animation: none;
    }
  }
  @keyframes nv-scr-sl-breathe {
    0%,
    100% {
      opacity: 0.55;
    }
    50% {
      opacity: 1;
    }
  }
}
</style>
