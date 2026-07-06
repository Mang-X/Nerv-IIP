<script setup lang="ts">
import { useEventListener } from '@vueuse/core'
import { computed, onMounted, ref } from 'vue'
import { computeScale, type ScaleMode } from './scale'

const props = withDefaults(
  defineProps<{
    designWidth?: number
    designHeight?: number
    mode?: ScaleMode
  }>(),
  { designWidth: 1920, designHeight: 1080, mode: 'fit' },
)

const vw = ref(props.designWidth)
const vh = ref(props.designHeight)

function update() {
  if (typeof window === 'undefined') return
  vw.value = window.innerWidth
  vh.value = window.innerHeight
}

onMounted(update)
useEventListener('resize', update)

const scale = computed(() =>
  computeScale(vw.value, vh.value, props.designWidth, props.designHeight, props.mode),
)

const stageStyle = computed(() => ({
  width: `${props.designWidth}px`,
  height: `${props.designHeight}px`,
  transform: `scale(${scale.value.x}, ${scale.value.y})`,
  transformOrigin: 'center center',
}))
</script>

<template>
  <div class="screen-scaler">
    <div class="screen-scaler__stage" :style="stageStyle">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.screen-scaler {
  position: fixed;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  background: var(--sb-bg, #03050b);
}
.screen-scaler__stage {
  flex: none;
}
</style>
