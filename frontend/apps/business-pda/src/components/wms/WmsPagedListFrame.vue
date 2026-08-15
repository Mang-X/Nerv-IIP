<script setup lang="ts">
import TaskListShell from '@/components/task-list/TaskListShell.vue'

const props = withDefaults(
  defineProps<{
    stateKey?: string
    refreshing: boolean
    loadingMore: boolean
    pending: boolean
    loaded: number
    total: number
    emptyDescription?: string
    filterState?: Record<string, unknown>
    error?: unknown
    loadMoreError?: unknown
  }>(),
  { stateKey: 'wms-task-list', emptyDescription: '当前筛选范围暂无任务。' },
)

const emit = defineEmits<{
  refresh: []
  loadMore: []
  restore: [state: { filters: Record<string, unknown> }]
  retry: []
  retryLoadMore: []
}>()
</script>

<template>
  <TaskListShell
    :state-key="props.stateKey"
    scope="当前授权 WMS 作业范围"
    source="WMS 作业服务"
    :loaded="loaded"
    :total="total"
    :pending="pending"
    :refreshing="refreshing"
    :loading-more="loadingMore"
    :show-meta="false"
    :empty-description="emptyDescription"
    :filter-state="filterState"
    :error="error"
    :load-more-error="loadMoreError"
    error-test-id="error-banner"
    @refresh="emit('refresh')"
    @load-more="emit('loadMore')"
    @restore="emit('restore', $event)"
    @retry="emit('retry')"
    @retry-load-more="emit('retryLoadMore')"
  >
    <slot />
  </TaskListShell>
</template>
