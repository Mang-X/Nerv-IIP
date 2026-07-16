<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { ref } from 'vue'
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
    class?: HTMLAttributes['class']
  }>(),
  { finished: false, offset: 80, finishedText: '没有更多了' },
)
const emit = defineEmits<{ load: [] }>()
const loading = defineModel<boolean>({ default: false })

const scroller = ref<HTMLElement>()

function onScroll() {
  const el = scroller.value
  if (!el || loading.value || props.finished) return
  if (el.scrollHeight - el.scrollTop - el.clientHeight <= props.offset) {
    loading.value = true
    emit('load')
  }
}
</script>

<template>
  <div
    ref="scroller"
    data-slot="infinite-list"
    :class="cn('overflow-y-auto', props.class)"
    @scroll="onScroll"
  >
    <slot />
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
