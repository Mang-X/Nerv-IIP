<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Screen — base panel. A near-black gradient body inside a gradient hairline that
 * brightens subtly down the two sides (dim top/bottom), a white top highlight for
 * glass, and a quiet depth shadow. No body glow — structure reads from the white
 * highlight, not from color. Optional title row and a restrained status accent (a
 * thin colored top line, no bloom). Built on the independent `--sb-*` tokens.
 */
defineProps<{
  title?: string
  /** A thin colored top line for status panels (cyan/green/amber/red). */
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
  border-radius: var(--sb-radius);
  padding: 17px 20px;
  color: var(--sb-text);
  isolation: isolate;
  /* white top highlight (glass) + a quiet depth shadow — no colored bloom */
  box-shadow:
    inset 0 1px 0 var(--sb-highlight),
    0 10px 30px -18px rgba(0, 0, 0, 0.9);
}
/* corner points of light — a soft radial pooled in the two LEFT corners only
   (not along the edges); top-left brighter, bottom-left fainter. */
.sb-panel::after {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  pointer-events: none;
  z-index: 0;
  /* a highlight that traces the rounded corner itself — the top + left border
     light up and the border-radius bends them into an arc; mask keeps it to the
     top-left (bright) and bottom-left (faint) corners only. */
  border: 1.6px solid transparent;
  border-top-color: rgba(150, 214, 255, 0.85);
  border-left-color: rgba(150, 214, 255, 0.85);
  border-bottom-color: rgba(150, 214, 255, 0.35);
  -webkit-mask:
    linear-gradient(135deg, #000, transparent 18%),
    linear-gradient(45deg, #000, transparent 13%);
  mask:
    linear-gradient(135deg, #000, transparent 18%),
    linear-gradient(45deg, #000, transparent 13%);
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
/* status accent — a thin top line that fades to its ends, no bloom */
.sb-panel-accent {
  position: absolute;
  top: 0;
  left: 16px;
  right: 16px;
  height: 1px;
  z-index: 2;
  border-radius: 1px;
}
.sb-panel-accent.cyan {
  background: linear-gradient(90deg, transparent, var(--sb-cyan), transparent);
}
.sb-panel-accent.green {
  background: linear-gradient(90deg, transparent, var(--sb-green), transparent);
}
.sb-panel-accent.amber {
  background: linear-gradient(90deg, transparent, var(--sb-amber), transparent);
}
.sb-panel-accent.red {
  background: linear-gradient(90deg, transparent, var(--sb-red), transparent);
}
</style>
