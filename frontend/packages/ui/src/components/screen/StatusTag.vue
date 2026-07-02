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
  <span class="sb-tag" :class="color">
    <i class="sb-tag-dot" />
    <span class="sb-tag-label">{{ label }}</span>
  </span>
</template>

<style scoped>
.sb-tag {
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
.sb-tag-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
  box-shadow: 0 0 6px currentColor;
  flex: none;
}
.sb-tag-label {
  color: var(--sb-text-2);
}
.sb-tag.cyan {
  color: var(--sb-cyan);
  border-color: rgba(0, 229, 255, 0.4);
  background: rgba(0, 229, 255, 0.08);
}
.sb-tag.green {
  color: var(--sb-green);
  border-color: rgba(0, 230, 118, 0.4);
  background: rgba(0, 230, 118, 0.08);
}
.sb-tag.amber {
  color: var(--sb-amber);
  border-color: rgba(255, 214, 0, 0.4);
  background: rgba(255, 214, 0, 0.08);
}
.sb-tag.red {
  color: var(--sb-red);
  border-color: rgba(255, 23, 68, 0.4);
  background: rgba(255, 23, 68, 0.08);
}
</style>
