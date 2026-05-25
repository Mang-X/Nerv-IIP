<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  x: number
  y: number
}

const props = defineProps<Props>()

const tooltipStyle = computed(() => {
  const width = 340
  const height = 72
  const viewportWidth = typeof window === 'undefined' ? props.x + width : window.innerWidth
  const viewportHeight = typeof window === 'undefined' ? props.y + height : window.innerHeight

  return {
    left: `${Math.max(Math.min(props.x + 14, viewportWidth - width - 8), 8)}px`,
    top: `${Math.max(Math.min(props.y + 16, viewportHeight - height - 8), 8)}px`,
  }
})
</script>

<template>
  <div class="scheduling-pointer-tooltip" :style="tooltipStyle" role="tooltip">
    <slot />
  </div>
</template>

<style scoped>
.scheduling-pointer-tooltip {
  position: fixed;
  z-index: 50;
  max-width: 320px;
  padding: 7px 10px;
  border-radius: 6px;
  background: hsl(var(--foreground, 222 47% 11%));
  box-shadow: 0 10px 24px rgba(15, 23, 42, 0.16);
  color: hsl(var(--background, 0 0% 100%));
  font-size: 12px;
  font-weight: 650;
  line-height: 1.35;
  pointer-events: none;
}
</style>
