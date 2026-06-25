<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Screen — base panel. A near-black gradient body inside a gradient hairline that
 * brightens down the two sides (dim top/bottom), a white top highlight for glass,
 * and a quiet depth shadow. An optional `accent` recolors the whole edge plus a
 * thin top line for status / categorized panels: cyan / green / amber / red /
 * indigo. Built on the independent `--sb-*` tokens.
 */
defineProps<{
  title?: string
  /** Recolors the edge + top line for status / categorized panels. */
  accent?: 'cyan' | 'green' | 'amber' | 'red' | 'indigo'
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <section :class="cn('sb-panel', accent, $props.class)">
    <span v-if="accent" class="sb-panel-accent" />
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
  border-radius: var(--sb-radius);
  padding: 17px 20px;
  color: var(--sb-text);
  isolation: isolate;
  /* white top highlight (glass) + a quiet depth shadow — no colored bloom */
  box-shadow:
    inset 0 1px 0 var(--sb-highlight),
    0 10px 30px -18px rgba(0, 0, 0, 0.9);
}
/* gradient hairline — a touch brighter down the two sides, dim top/bottom */
.sb-panel::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  padding: 1px;
  background: var(--sb-edge-gradient);
  -webkit-mask:
    linear-gradient(#000 0 0) content-box,
    linear-gradient(#000 0 0);
  -webkit-mask-composite: xor;
  mask-composite: exclude;
  pointer-events: none;
  z-index: 0;
}
/* color variants — recolor the edge to the accent (sides bright, ends dim) */
.sb-panel.cyan {
  --pa: 74, 166, 238;
}
.sb-panel.green {
  --pa: 69, 208, 137;
}
.sb-panel.amber {
  --pa: 242, 193, 78;
}
.sb-panel.red {
  --pa: 239, 90, 99;
}
.sb-panel.indigo {
  --pa: 139, 155, 230;
}
.sb-panel.cyan::before,
.sb-panel.green::before,
.sb-panel.amber::before,
.sb-panel.red::before,
.sb-panel.indigo::before {
  background: linear-gradient(
    90deg,
    rgba(var(--pa), 0.85),
    rgba(var(--pa), 0.14) 16%,
    rgba(var(--pa), 0.14) 84%,
    rgba(var(--pa), 0.55)
  );
}
.sb-panel > * {
  position: relative;
  z-index: 1;
}
.sb-panel-h {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}
.sb-panel-t {
  font-size: 15px;
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
/* status accent — a thin top line in the accent color, fading to its ends */
.sb-panel-accent {
  position: absolute;
  top: 0;
  left: 16px;
  right: 16px;
  height: 1px;
  z-index: 2;
  border-radius: 1px;
  background: linear-gradient(90deg, transparent, rgb(var(--pa)), transparent);
}
</style>
