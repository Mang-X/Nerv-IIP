<script setup lang="ts">
import { computed } from 'vue'

type Tone = 'cyan' | 'indigo' | 'green' | 'amber' | 'red'

interface CapsuleItem {
  /** Row caption, e.g. 焊接线 A. */
  label: string
  /** Fill 0–100. */
  value: number
  /** Bar color; defaults to cyan. */
  tone?: Tone
}

/**
 * Screen — horizontal capsule bars. Each row is a caption, a rounded track and a
 * gradient fill that glows in its tone; the percentage reads at the right. Fills
 * grow from a clamped 0–100 value and ease in. Built on the independent `--sb-*`
 * tokens; tone carries meaning, but the number is always shown too.
 */
const props = withDefaults(
  defineProps<{
    items: CapsuleItem[]
    /** Append to each value, e.g. %. */
    suffix?: string
  }>(),
  {
    suffix: '%',
    items: () => [
      { label: '焊接线 A', value: 93, tone: 'cyan' },
      { label: '装配线 B', value: 76, tone: 'indigo' },
      { label: 'CNC 线 C', value: 41, tone: 'amber' },
      { label: '涂装线 D', value: 88, tone: 'green' },
    ],
  },
)

const rows = computed(() =>
  props.items.map(it => ({
    ...it,
    tone: it.tone ?? 'cyan',
    pct: Math.max(0, Math.min(100, it.value)),
  })),
)
</script>

<template>
  <div class="sb-cb">
    <div v-for="(r, i) in rows" :key="i" class="sb-cb-row">
      <span class="sb-cb-label">{{ r.label }}</span>
      <span class="sb-cb-track" :class="r.tone">
        <span class="sb-cb-fill" :class="r.tone" :style="{ width: `${r.pct}%` }" />
      </span>
      <span class="sb-cb-val" :class="r.tone">{{ r.value }}{{ suffix }}</span>
    </div>
  </div>
</template>

<style scoped>
.sb-cb {
  display: flex;
  flex-direction: column;
  gap: 14px;
  font-variant-numeric: tabular-nums;
}
.sb-cb-row {
  display: grid;
  grid-template-columns: 64px 1fr 52px;
  align-items: center;
  gap: 12px;
}
.sb-cb-label {
  font-size: 12px;
  color: var(--sb-muted);
  text-align: right;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.sb-cb-track {
  position: relative;
  height: 12px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.05);
  box-shadow: inset 0 0 0 1px var(--sb-line);
  overflow: hidden;
}
.sb-cb-fill {
  position: absolute;
  inset: 0 auto 0 0;
  border-radius: 999px;
  transition: width 0.6s var(--sb-ease);
}
.sb-cb-val {
  font-size: 14px;
  font-weight: 600;
  text-align: right;
}

/* tones — fill gradient + matching glow + value text */
.sb-cb-fill.cyan {
  background: linear-gradient(90deg, rgba(0, 229, 255, 0.35), var(--sb-cyan));
  box-shadow: 0 0 8px var(--sb-cyan-dim);
}
.sb-cb-val.cyan {
  color: var(--sb-cyan);
}
.sb-cb-fill.indigo {
  background: linear-gradient(90deg, rgba(167, 139, 250, 0.3), var(--sb-indigo));
  box-shadow: 0 0 8px rgba(167, 139, 250, 0.5);
}
.sb-cb-val.indigo {
  color: var(--sb-indigo);
}
.sb-cb-fill.green {
  background: linear-gradient(90deg, rgba(0, 230, 118, 0.3), var(--sb-green));
  box-shadow: 0 0 8px rgba(0, 230, 118, 0.5);
}
.sb-cb-val.green {
  color: var(--sb-green);
}
.sb-cb-fill.amber {
  background: linear-gradient(90deg, rgba(255, 214, 0, 0.3), var(--sb-amber));
  box-shadow: 0 0 8px rgba(255, 214, 0, 0.5);
}
.sb-cb-val.amber {
  color: var(--sb-amber);
}
.sb-cb-fill.red {
  background: linear-gradient(90deg, rgba(255, 23, 68, 0.3), var(--sb-red));
  box-shadow: 0 0 8px rgba(255, 23, 68, 0.5);
}
.sb-cb-val.red {
  color: var(--sb-red);
}

@media (prefers-reduced-motion: reduce) {
  .sb-cb-fill {
    transition: none;
  }
}
</style>
