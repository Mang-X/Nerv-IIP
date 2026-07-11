<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Screen — base panel. A near-black gradient body inside a gradient hairline that
 * brightens down the two sides (dim top/bottom), a white top highlight for glass,
 * and a quiet depth shadow. An optional `accent` recolors the whole edge plus a
 * thin top line for status / categorized panels: cyan / green / amber / red /
 * indigo. Built on the independent `--nv-scr-*` tokens.
 */
defineProps<{
  title?: string
  /** Recolors the edge + top line for status / categorized panels. */
  accent?: 'cyan' | 'green' | 'amber' | 'red' | 'indigo'
  class?: HTMLAttributes['class']
}>()
</script>

<template>
  <section :class="cn('nv-scr-panel', accent, $props.class)">
    <span v-if="accent" class="nv-scr-panel-accent" />
    <div v-if="title || $slots.extra" class="nv-scr-panel-h">
      <i v-if="title" class="nv-scr-panel-glyph" aria-hidden="true" />
      <span class="nv-scr-panel-t">{{ title }}<slot name="title-extra" /></span>
      <span class="nv-scr-panel-rule" aria-hidden="true" />
      <div v-if="$slots.extra" class="nv-scr-panel-extra"><slot name="extra" /></div>
    </div>
    <slot />
  </section>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-panel {
    position: relative;
    background: linear-gradient(180deg, var(--nv-scr-panel-a), var(--nv-scr-panel-b));
    border-radius: var(--nv-scr-radius);
    padding: 17px 20px;
    color: var(--nv-scr-text);
    isolation: isolate;
    /* white top highlight (glass) + a quiet depth shadow — no colored bloom */
    box-shadow:
      inset 0 1px 0 var(--nv-scr-highlight),
      0 10px 30px -18px rgba(0, 0, 0, 0.9);
  }
  /* gradient hairline — a touch brighter down the two sides, dim top/bottom */
  .nv-scr-panel::before {
    content: '';
    position: absolute;
    inset: 0;
    border-radius: inherit;
    padding: 1px;
    background: var(--nv-scr-edge-gradient);
    -webkit-mask:
      linear-gradient(#000 0 0) content-box,
      linear-gradient(#000 0 0);
    -webkit-mask-composite: xor;
    mask-composite: exclude;
    pointer-events: none;
    z-index: 0;
  }
  /* color variants — recolor the edge to the accent (sides bright, ends dim) */
  .nv-scr-panel.cyan {
    --pa: 74, 166, 238;
  }
  .nv-scr-panel.green {
    --pa: 69, 208, 137;
  }
  .nv-scr-panel.amber {
    --pa: 242, 193, 78;
  }
  .nv-scr-panel.red {
    --pa: 239, 90, 99;
  }
  .nv-scr-panel.indigo {
    --pa: 139, 155, 230;
  }
  .nv-scr-panel.cyan::before,
  .nv-scr-panel.green::before,
  .nv-scr-panel.amber::before,
  .nv-scr-panel.red::before,
  .nv-scr-panel.indigo::before {
    /* sides keep the white-highlight feel (accent mixed toward white), ends dim */
    background: linear-gradient(
      90deg,
      color-mix(in srgb, color-mix(in oklch, rgb(var(--pa)), white 52%) 60%, transparent),
      rgba(var(--pa), 0.04) 18%,
      rgba(var(--pa), 0.04) 82%,
      color-mix(in srgb, color-mix(in oklch, rgb(var(--pa)), white 36%) 52%, transparent)
    );
  }
  .nv-scr-panel > * {
    position: relative;
    z-index: 1;
  }
  .nv-scr-panel-h {
    display: flex;
    align-items: center;
    gap: 11px;
    margin-bottom: 12px;
    min-height: 24px;
  }
  /* 斜切能量块 —— 面板标题的统一特效前缀（2026-07 生产走查加入） */
  .nv-scr-panel-glyph {
    width: 8px;
    height: 18px;
    flex: none;
    border-radius: 2px;
    transform: skewX(-16deg);
    background: linear-gradient(180deg, var(--nv-scr-cyan), rgba(74, 166, 238, 0.25));
    box-shadow: 0 0 11px rgba(74, 166, 238, 0.55);
  }
  .nv-scr-panel-t {
    font-size: 17px;
    font-weight: 700;
    letter-spacing: 0.1em;
    color: #fff;
    text-shadow: 0 0 16px rgba(96, 180, 255, 0.4);
    display: inline-flex;
    align-items: center;
    gap: 6px;
    white-space: nowrap;
    /* title-extra（如趋势图图例）可收缩截断，绝不把右侧 extra（tabs）挤出面板头；
     标题文本是匿名 flex 项（min-content 保护），永远完整 */
    min-width: 0;
    flex-shrink: 1;
    overflow: hidden;
  }
  /* 标题与右侧工具之间的渐隐引导线 */
  .nv-scr-panel-rule {
    flex: 1;
    height: 1px;
    margin: 0 6px;
    background: linear-gradient(
      90deg,
      rgba(135, 208, 255, 0.28),
      rgba(255, 255, 255, 0.05) 45%,
      transparent
    );
  }
  .nv-scr-panel-extra {
    font-size: 13px;
    color: var(--nv-scr-muted);
    min-width: 0;
  }
  /* status accent — a thin top line in the accent color, fading to its ends */
  .nv-scr-panel-accent {
    position: absolute;
    top: 0;
    left: 16px;
    right: 16px;
    height: 1px;
    z-index: 2;
    border-radius: 1px;
    background: linear-gradient(90deg, transparent, rgb(var(--pa)), transparent);
  }
}
</style>
