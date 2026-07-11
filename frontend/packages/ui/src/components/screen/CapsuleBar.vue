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
 * grow from a clamped 0–100 value and ease in. Built on the independent `--nv-scr-*`
 * tokens; tone carries meaning, but the number is always shown too.
 */
const props = withDefaults(
  defineProps<{
    items?: CapsuleItem[]
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
  props.items.map((it) => ({
    ...it,
    tone: it.tone ?? 'cyan',
    pct: Math.max(0, Math.min(100, it.value)),
  })),
)
</script>

<template>
  <div class="nv-scr-cb">
    <div v-for="(r, i) in rows" :key="i" class="nv-scr-cb-row">
      <span class="nv-scr-cb-label" :title="r.label">{{ r.label }}</span>
      <span class="nv-scr-cb-track" :class="r.tone">
        <span
          v-if="r.pct > 0"
          class="nv-scr-cb-fill"
          :class="r.tone"
          :style="{ width: `${r.pct}%` }"
        />
      </span>
      <span class="nv-scr-cb-val">{{ r.value }}{{ suffix }}</span>
    </div>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-cb {
    display: flex;
    flex-direction: column;
    gap: 14px;
    font-variant-numeric: tabular-nums;
  }
  .nv-scr-cb-row {
    display: grid;
    grid-template-columns: 64px 1fr 52px;
    align-items: center;
    gap: 12px;
  }
  .nv-scr-cb-label {
    font-size: 12px;
    color: var(--nv-scr-muted);
    text-align: right;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .nv-scr-cb-track {
    position: relative;
    height: 12px;
    border-radius: 999px;
    background: rgba(255, 255, 255, 0.05);
    box-shadow: inset 0 0 0 1px var(--nv-scr-line);
    overflow: hidden;
  }
  .nv-scr-cb-fill {
    position: absolute;
    inset: 0 auto 0 0;
    border-radius: 999px;
    transition: width 0.6s var(--nv-scr-ease-emphasized);
  }
  .nv-scr-cb-val {
    font-size: 14px;
    font-weight: 600;
    text-align: right;
    color: #fff;
  }

  /* tones — fill gradient + matching glow + value text */
  .nv-scr-cb-fill.cyan {
    background: linear-gradient(90deg, rgba(0, 229, 255, 0.35), var(--nv-scr-cyan));
    box-shadow: 0 0 8px var(--nv-scr-cyan-dim);
  }
  .nv-scr-cb-val.cyan {
    color: var(--nv-scr-cyan);
  }
  .nv-scr-cb-fill.indigo {
    background: linear-gradient(90deg, rgba(167, 139, 250, 0.3), var(--nv-scr-indigo));
    box-shadow: 0 0 8px rgba(167, 139, 250, 0.5);
  }
  .nv-scr-cb-val.indigo {
    color: var(--nv-scr-indigo);
  }
  .nv-scr-cb-fill.green {
    background: linear-gradient(90deg, rgba(0, 230, 118, 0.3), var(--nv-scr-green));
    box-shadow: 0 0 8px rgba(0, 230, 118, 0.5);
  }
  .nv-scr-cb-val.green {
    color: var(--nv-scr-green);
  }
  .nv-scr-cb-fill.amber {
    background: linear-gradient(90deg, rgba(255, 214, 0, 0.3), var(--nv-scr-amber));
    box-shadow: 0 0 8px rgba(255, 214, 0, 0.5);
  }
  .nv-scr-cb-val.amber {
    color: var(--nv-scr-amber);
  }
  .nv-scr-cb-fill.red {
    background: linear-gradient(90deg, rgba(255, 23, 68, 0.3), var(--nv-scr-red));
    box-shadow: 0 0 8px rgba(255, 23, 68, 0.5);
  }
  .nv-scr-cb-val.red {
    color: var(--nv-scr-red);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-scr-cb-fill {
      transition: none;
    }
  }
}
</style>
