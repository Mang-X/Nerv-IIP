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
  <ScreenPanel :accent="accent" class="sb-lc">
    <div class="sb-lc-top">
      <div class="sb-lc-nm">{{ name }} · {{ state }}</div>
      <span class="sb-lc-dot" :class="tone" />
    </div>
    <div class="sb-lc-state">当前状态</div>
    <div class="sb-lc-big" :class="tone">{{ label }}</div>
    <div class="sb-lc-stats">
      <div><i>计划产量</i><b>{{ plan }} 件</b></div>
      <div><i>实际产量</i><b>{{ actual }} 件</b></div>
      <div><i>达成率</i><b>{{ rate }}</b></div>
    </div>
    <div class="sb-lc-foot"><span>停机时长</span><b>{{ downtime }}</b></div>
  </ScreenPanel>
</template>

<style scoped>
.sb-lc-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.sb-lc-nm {
  font-size: 17px;
  font-weight: 600;
}
.sb-lc-dot {
  width: 11px;
  height: 11px;
  border-radius: 50%;
  animation: sb-breathe 2s ease-in-out infinite;
}
.sb-lc-dot.run {
  background: var(--sb-green);
  box-shadow: 0 0 9px var(--sb-green);
}
.sb-lc-dot.idle {
  background: var(--sb-amber);
  box-shadow: 0 0 9px var(--sb-amber);
}
.sb-lc-dot.alarm {
  background: var(--sb-red);
  box-shadow: 0 0 9px var(--sb-red);
  /* alarm pulses faster than run/idle — urgency, not ambience */
  animation-duration: 0.9s;
}
.sb-lc-state {
  font-size: 13px;
  color: var(--sb-muted);
  margin: 14px 0 3px;
}
.sb-lc-big {
  font-size: 21px;
  font-weight: 600;
}
.sb-lc-big.run {
  color: var(--sb-green);
}
.sb-lc-big.idle {
  color: var(--sb-amber);
}
.sb-lc-big.alarm {
  color: var(--sb-red);
}
.sb-lc-stats {
  display: flex;
  justify-content: space-between;
  margin-top: 16px;
}
.sb-lc-stats i {
  font-size: 12px;
  color: var(--sb-muted);
  font-style: normal;
}
.sb-lc-stats b {
  display: block;
  font-size: 16px;
  font-weight: 600;
  margin-top: 4px;
}
.sb-lc-foot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid var(--sb-divider);
  font-size: 12px;
  color: var(--sb-muted);
}
.sb-lc-foot b {
  font-size: 15px;
  color: var(--sb-text-2);
  font-weight: 600;
}
@media (prefers-reduced-motion: reduce) {
  .sb-lc-dot {
    animation: none;
  }
}
@keyframes sb-breathe {
  0%,
  100% {
    opacity: 0.55;
  }
  50% {
    opacity: 1;
  }
}
</style>
