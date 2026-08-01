<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { Loader2 } from '@lucide/vue'
import { cn } from '../../lib/utils'

/**
 * Mobile InfiniteList — load-more on scroll near the bottom (Vant List style).
 * Emits `load` when within `offset` px of the end; shows a loading footer, a
 * "no more" footer when `finished`. `v-model` is the loading flag.
 */
const props = withDefaults(
  defineProps<{
    finished?: boolean
    offset?: number
    finishedText?: string
    /** Use an intersection sentinel when a parent component owns the scroll area. */
    parentScroll?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { finished: false, offset: 80, finishedText: '没有更多了', parentScroll: false },
)
const emit = defineEmits<{ load: [] }>()
const loading = defineModel<boolean>({ default: false })

const scroller = ref<HTMLElement>()
const sentinel = ref<HTMLElement>()
let observer: IntersectionObserver | undefined

function requestLoad() {
  if (loading.value || props.finished) return
  loading.value = true
  emit('load')
}

function onScroll() {
  const el = scroller.value
  if (!el || loading.value || props.finished) return
  if (el.scrollHeight - el.scrollTop - el.clientHeight <= props.offset) {
    requestLoad()
  }
}

onMounted(() => {
  if (!props.parentScroll || !sentinel.value || typeof IntersectionObserver === 'undefined') return
  observer = new IntersectionObserver(
    ([entry]) => {
      if (entry?.isIntersecting) requestLoad()
    },
    { rootMargin: `${props.offset}px 0px` },
  )
  observer.observe(sentinel.value)
})

onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <div
    ref="scroller"
    data-slot="infinite-list"
    :class="cn(!parentScroll && 'overflow-y-auto', props.class)"
    @scroll="onScroll"
  >
    <slot />
    <span v-if="parentScroll" ref="sentinel" class="block h-px" aria-hidden="true" />
    <div class="flex items-center justify-center gap-2 py-3 text-sm text-muted-foreground">
      <template v-if="loading">
        <Loader2 class="size-4 animate-spin text-brand" aria-hidden="true" />
        加载中…
      </template>
      <template v-else-if="finished">
        {{ finishedText }}
      </template>
      <template v-else> 上拉加载更多 </template>
    </div>
  </div>
</template>
