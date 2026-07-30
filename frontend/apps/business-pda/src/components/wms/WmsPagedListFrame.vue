<script setup lang="ts">
import { NvPullRefresh } from '@nerv-iip/ui-mobile'
import { useIntersectionObserver } from '@vueuse/core'
import { computed, useTemplateRef } from 'vue'

const props = defineProps<{
  refreshing: boolean
  loadingMore: boolean
  pending: boolean
  loaded: number
  total: number
}>()

const emit = defineEmits<{
  refresh: []
  loadMore: []
}>()

const hasMore = computed(() => props.loaded < props.total)
const loadMoreSentinel = useTemplateRef<HTMLElement>('loadMoreSentinel')

useIntersectionObserver(
  loadMoreSentinel,
  ([entry]) => {
    if (entry?.isIntersecting && hasMore.value && !props.pending && !props.loadingMore) {
      emit('loadMore')
    }
  },
  { rootMargin: '80px 0px' },
)
</script>

<template>
  <NvPullRefresh
    data-testid="pull-refresh"
    class="min-h-0 flex-1"
    :model-value="refreshing"
    @refresh="emit('refresh')"
  >
    <slot />
    <div
      v-if="loaded > 0"
      ref="loadMoreSentinel"
      data-testid="load-more-sentinel"
      class="flex min-h-12 items-center justify-center py-3 text-sm text-muted-foreground"
    >
      {{ loadingMore ? '加载中…' : hasMore ? '继续上滑加载' : '没有更多了' }}
    </div>
  </NvPullRefresh>
</template>
