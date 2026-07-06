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

/** 半份列表高度（列表渲染两遍，滚过一半即无缝回到起点）；未溢出返回 0。 */
function halfIfOverflow(): number {
  const trackEl = track.value
  const viewEl = viewport.value
  if (!trackEl || !viewEl) return 0
  const half = trackEl.scrollHeight / 2
  return half > viewEl.clientHeight ? half : 0
}

/** 把任意 offset 无缝收回 (-half, 0]，保证正反向滚都循环。 */
function wrap(v: number, half: number): number {
  return -(((-v % half) + half) % half)
}

useRafFn(({ delta }) => {
  if (paused.value) return
  const half = halfIfOverflow()
  if (half === 0) {
    offset.value = 0
    return
  }
  offset.value = wrap(offset.value - (props.speed * delta) / 1000, half)
})

function onEnter() {
  if (props.pauseOnHover) paused.value = true
}
function onLeave() {
  if (props.pauseOnHover) paused.value = false
}

/** 悬停暂停时用滚轮自由查看：手动位移同样按半高取模，双向无缝。 */
function onWheel(e: WheelEvent) {
  const half = halfIfOverflow()
  if (half === 0) return
  e.preventDefault()
  offset.value = wrap(offset.value - e.deltaY, half)
}
</script>

<template>
  <div ref="viewport" class="scroll-board" @mouseenter="onEnter" @mouseleave="onLeave" @wheel="onWheel">
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
