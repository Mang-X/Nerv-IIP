<script setup lang="ts">
import type { TimelineTick } from '../time-scale/timelineLayout'
import { computed } from 'vue'

interface Props {
  ticks: TimelineTick[]
  width: number
  labelWidth: number
  scrollLeft?: number
  viewportWidth?: number
}

const props = withDefaults(defineProps<Props>(), {
  scrollLeft: 0,
  viewportWidth: 0,
})

const visibleTicks = computed(() => {
  if (props.viewportWidth <= 0) {
    return props.ticks
  }

  const overscan = 160
  const timelineStart = props.labelWidth + props.scrollLeft - overscan
  const timelineEnd = props.labelWidth + props.scrollLeft + props.viewportWidth + overscan

  return props.ticks.filter((tick) => tick.x >= timelineStart && tick.x <= timelineEnd)
})
</script>

<template>
  <div
    class="timeline-axis"
    data-test="timeline-axis"
    :style="{ gridTemplateColumns: `${labelWidth}px minmax(0, 1fr)` }"
  >
    <div class="timeline-axis__corner">Timeline</div>
    <div class="timeline-axis__ticks">
      <div
        class="timeline-axis__tick-track"
        :style="{
          width: `${Math.max(width - labelWidth, 0)}px`,
          transform: `translateX(-${scrollLeft}px)`,
        }"
      >
        <span
          v-for="tick in visibleTicks"
          :key="tick.date"
          class="timeline-axis__tick"
          :style="{ left: `${tick.x - labelWidth}px` }"
        >
          {{ tick.label }}
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.timeline-axis {
  display: grid;
  min-width: 100%;
  width: 100%;
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
  padding-inline: 14px;
}

.timeline-axis__tick-track {
  position: relative;
  height: 100%;
}

.timeline-axis__tick {
  position: absolute;
  top: 9px;
  transform: translateX(0);
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
