<script setup lang="ts" generic="T">
import type { HTMLAttributes } from 'vue'
import { computed, onMounted, ref } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Mobile VirtualList — fixed-height virtual scrolling. Renders only the visible
 * window (+ buffer) so 10k+ rows stay smooth. Give it a bounded height via
 * `class`; provide `itemHeight` (px). Scoped slot exposes { item, index }.
 */
const props = withDefaults(
  defineProps<{
    items: T[]
    itemHeight: number
    buffer?: number
    class?: HTMLAttributes['class']
  }>(),
  { buffer: 6 },
)
defineSlots<{ default: (props: { item: T; index: number }) => unknown }>()

const scroller = ref<HTMLElement>()
const scrollTop = ref(0)
const viewport = ref(0)

function onScroll() {
  scrollTop.value = scroller.value?.scrollTop ?? 0
}
onMounted(() => {
  viewport.value = scroller.value?.clientHeight ?? 0
})

const total = computed(() => props.items.length * props.itemHeight)
const start = computed(() =>
  Math.max(0, Math.floor(scrollTop.value / props.itemHeight) - props.buffer),
)
const visibleCount = computed(() => Math.ceil(viewport.value / props.itemHeight) + props.buffer * 2)
const end = computed(() => Math.min(props.items.length, start.value + visibleCount.value))
const offsetY = computed(() => start.value * props.itemHeight)
const visible = computed(() =>
  props.items.slice(start.value, end.value).map((data, i) => ({ data, index: start.value + i })),
)
</script>

<template>
  <div
    ref="scroller"
    data-slot="virtual-list"
    :class="cn('overflow-y-auto', props.class)"
    @scroll="onScroll"
  >
    <div :style="{ height: `${total}px`, position: 'relative' }">
      <div :style="{ transform: `translateY(${offsetY}px)` }">
        <div v-for="v in visible" :key="v.index" :style="{ height: `${itemHeight}px` }">
          <slot :item="v.data" :index="v.index" />
        </div>
      </div>
    </div>
  </div>
</template>
