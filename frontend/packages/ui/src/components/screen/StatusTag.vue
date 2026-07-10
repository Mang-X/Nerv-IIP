<script setup lang="ts">
import { computed } from 'vue'

/**
 * Screen — status chip. A small semantic tag: a colored dot + label inside a
 * tinted hairline border over a very faint wash (no solid fill block — keeps the
 * board dark). Operational aliases (run/idle/alarm) fold onto the palette. The
 * dot plus the word carry the state, so it reads without relying on color.
 */
const props = withDefaults(
  defineProps<{
    /** Operational alias or a raw palette color. */
    tone?: 'run' | 'idle' | 'alarm' | 'cyan' | 'green' | 'amber' | 'red'
    /** Chip text, e.g. 运行中 / 待机 / 报警. */
    label?: string
  }>(),
  { tone: 'run', label: '运行中' },
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
  <span class="nv-scr-tag" :class="color">
    <i class="nv-scr-tag-dot" />
    <span class="nv-scr-tag-label">{{ label }}</span>
  </span>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-tag {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    height: 22px;
    padding: 0 9px;
    border-radius: 999px;
    font-size: 12px;
    line-height: 1;
    font-variant-numeric: tabular-nums;
    border: 1px solid currentColor;
    /* color drives border + dot + text via currentColor; bg stays a faint wash */
  }
  .nv-scr-tag-dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: currentColor;
    box-shadow: 0 0 6px currentColor;
    flex: none;
  }
  .nv-scr-tag-label {
    color: var(--nv-scr-text-2);
  }
  .nv-scr-tag.cyan {
    color: var(--nv-scr-cyan);
    border-color: rgba(0, 229, 255, 0.4);
    background: rgba(0, 229, 255, 0.08);
  }
  .nv-scr-tag.green {
    color: var(--nv-scr-green);
    border-color: rgba(0, 230, 118, 0.4);
    background: rgba(0, 230, 118, 0.08);
  }
  .nv-scr-tag.amber {
    color: var(--nv-scr-amber);
    border-color: rgba(255, 214, 0, 0.4);
    background: rgba(255, 214, 0, 0.08);
  }
  .nv-scr-tag.red {
    color: var(--nv-scr-red);
    border-color: rgba(255, 23, 68, 0.4);
    background: rgba(255, 23, 68, 0.08);
  }
}
</style>
