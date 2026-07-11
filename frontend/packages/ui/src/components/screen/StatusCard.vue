<script setup lang="ts">
import { computed } from 'vue'
import ScreenPanel from './ScreenPanel.vue'

/**
 * Screen — production-line status card. Top accent edge + breathing status light,
 * the current state in its semantic color, a plan / actual / rate triple, and a
 * downtime footer. Tone drives both the accent and the light/text color.
 */
const props = defineProps<{
  /** Line name, e.g. 焊接线 A. */
  name: string
  /** Short state word shown after the name, e.g. 运行. */
  state: string
  /** Big state label, e.g. 运行中. */
  label: string
  tone: 'run' | 'idle' | 'alarm'
  plan: string
  actual: string
  rate: string
  downtime: string
}>()

const accent = computed(
  () => (({ run: 'green', idle: 'amber', alarm: 'red' }) as const)[props.tone],
)
</script>

<template>
  <ScreenPanel :accent="accent" class="nv-scr-lc">
    <div class="nv-scr-lc-top">
      <div class="nv-scr-lc-nm">{{ name }} · {{ state }}</div>
      <span class="nv-scr-lc-dot" :class="tone" />
    </div>
    <div class="nv-scr-lc-state">当前状态</div>
    <div class="nv-scr-lc-big" :class="tone">{{ label }}</div>
    <div class="nv-scr-lc-stats">
      <div>
        <i>计划产量</i><b>{{ plan }} 件</b>
      </div>
      <div>
        <i>实际产量</i><b>{{ actual }} 件</b>
      </div>
      <div>
        <i>达成率</i><b>{{ rate }}</b>
      </div>
    </div>
    <div class="nv-scr-lc-foot">
      <span>停机时长</span><b>{{ downtime }}</b>
    </div>
  </ScreenPanel>
</template>

<style scoped>
@layer nv-components {
  .nv-scr-lc-top {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .nv-scr-lc-nm {
    font-size: 17px;
    font-weight: 600;
  }
  .nv-scr-lc-dot {
    width: 11px;
    height: 11px;
    border-radius: 50%;
    animation: nv-scr-breathe 2s ease-in-out infinite;
  }
  .nv-scr-lc-dot.run {
    background: var(--nv-scr-green);
    box-shadow: 0 0 9px var(--nv-scr-green);
  }
  .nv-scr-lc-dot.idle {
    background: var(--nv-scr-amber);
    box-shadow: 0 0 9px var(--nv-scr-amber);
  }
  .nv-scr-lc-dot.alarm {
    background: var(--nv-scr-red);
    box-shadow: 0 0 9px var(--nv-scr-red);
    /* alarm pulses faster than run/idle — urgency, not ambience */
    animation-duration: 0.9s;
  }
  .nv-scr-lc-state {
    font-size: 13px;
    color: var(--nv-scr-muted);
    margin: 14px 0 3px;
  }
  .nv-scr-lc-big {
    font-size: 21px;
    font-weight: 600;
  }
  .nv-scr-lc-big.run {
    color: var(--nv-scr-green);
  }
  .nv-scr-lc-big.idle {
    color: var(--nv-scr-amber);
  }
  .nv-scr-lc-big.alarm {
    color: var(--nv-scr-red);
  }
  .nv-scr-lc-stats {
    display: flex;
    justify-content: space-between;
    margin-top: 16px;
  }
  .nv-scr-lc-stats i {
    font-size: 12px;
    color: var(--nv-scr-muted);
    font-style: normal;
  }
  .nv-scr-lc-stats b {
    display: block;
    font-size: 16px;
    font-weight: 600;
    margin-top: 4px;
  }
  .nv-scr-lc-foot {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-top: 14px;
    padding-top: 12px;
    border-top: 1px solid var(--nv-scr-divider);
    font-size: 12px;
    color: var(--nv-scr-muted);
  }
  .nv-scr-lc-foot b {
    font-size: 15px;
    color: var(--nv-scr-text-2);
    font-weight: 600;
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-scr-lc-dot {
      animation: none;
    }
  }
  @keyframes nv-scr-breathe {
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
