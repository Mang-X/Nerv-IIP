<script setup lang="ts" generic="T">
import { useRafFn } from '@vueuse/core'
import { ref } from 'vue'

const props = withDefaults(
  defineProps<{
    items: T[]
    /** 滚动速度 px/s */
    speed?: number
    pauseOnHover?: boolean
    rowKey?: (item: T, index: number) => string | number
  }>(),
  { speed: 28, pauseOnHover: true },
)

const viewport = ref<HTMLElement | null>(null)
const track = ref<HTMLElement | null>(null)
const offset = ref(0)
const paused = ref(false)

function rowKeyFor(item: T, index: number): string | number {
  return props.rowKey ? props.rowKey(item, index) : index
}

useRafFn(({ delta }) => {
  if (paused.value) return
  const trackEl = track.value
  const viewEl = viewport.value
  if (!trackEl || !viewEl) return
  // 列表渲染两遍，滚过一半即无缝回到起点
  const half = trackEl.scrollHeight / 2
  if (half <= viewEl.clientHeight) {
    offset.value = 0
    return
  }
  offset.value -= (props.speed * delta) / 1000
  if (Math.abs(offset.value) >= half) {
    offset.value = 0
  }
})

function onEnter() {
  if (props.pauseOnHover) paused.value = true
}
function onLeave() {
  if (props.pauseOnHover) paused.value = false
}
</script>

<template>
  <div ref="viewport" class="scroll-board sb-scroll" @mouseenter="onEnter" @mouseleave="onLeave">
    <div ref="track" class="scroll-board__track" :style="{ transform: `translateY(${offset}px)` }">
      <div v-for="(item, i) in items" :key="rowKeyFor(item, i)" class="scroll-board__row">
        <slot name="row" :item="item" :index="i">{{ item }}</slot>
      </div>
      <div
        v-for="(item, i) in items"
        :key="`dup-${rowKeyFor(item, i)}`"
        class="scroll-board__row"
        aria-hidden="true"
      >
        <slot name="row" :item="item" :index="i">{{ item }}</slot>
      </div>
    </div>
  </div>
</template>

<style scoped>
.scroll-board {
  height: 100%;
  overflow: hidden;
  position: relative;
}
.scroll-board__track {
  will-change: transform;
}
</style>
