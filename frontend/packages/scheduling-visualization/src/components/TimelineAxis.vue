<script setup lang="ts">
import type { TimelineTick } from '../time-scale/timelineLayout'

interface Props {
  ticks: TimelineTick[]
  width: number
  labelWidth: number
}

defineProps<Props>()
</script>

<template>
  <div
    class="timeline-axis"
    data-test="timeline-axis"
    :style="{ width: `${width}px`, gridTemplateColumns: `${labelWidth}px 1fr` }"
  >
    <div class="timeline-axis__corner">Timeline</div>
    <div class="timeline-axis__ticks">
      <span
        v-for="tick in ticks"
        :key="tick.date"
        class="timeline-axis__tick"
        :style="{ left: `${tick.x - labelWidth}px` }"
      >
        {{ tick.label }}
      </span>
    </div>
  </div>
</template>

<style scoped>
.timeline-axis {
  display: grid;
  min-width: 100%;
  height: 34px;
  border-bottom: 1px solid rgba(226, 232, 240, 0.95);
  background: #f8fafc;
}

.timeline-axis__corner {
  display: flex;
  align-items: center;
  padding-inline: 12px;
  border-right: 1px solid rgba(226, 232, 240, 0.95);
  color: #475569;
  font-size: 12px;
  font-weight: 750;
}

.timeline-axis__ticks {
  position: relative;
  overflow: hidden;
}

.timeline-axis__tick {
  position: absolute;
  top: 9px;
  transform: translateX(-1px);
  color: #475569;
  font-size: 11px;
  font-weight: 650;
  white-space: nowrap;
}

.timeline-axis__tick::before {
  position: absolute;
  top: 19px;
  left: 0;
  width: 1px;
  height: 999px;
  background: rgba(203, 213, 225, 0.7);
  content: "";
}
</style>

