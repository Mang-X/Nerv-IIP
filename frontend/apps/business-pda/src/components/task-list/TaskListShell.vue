<script setup lang="ts">
import ListScopeMeta from '@/components/ListScopeMeta.vue'
import RetryableListError from '@/components/RetryableListError.vue'
import { NvInfiniteList, NvMobileEmpty, NvPullRefresh } from '@nerv-iip/ui-mobile'
import { computed, nextTick, onMounted, shallowRef, watch } from 'vue'

interface PersistedTaskListState {
  filters: Record<string, unknown>
  scrollTop: number
}

const props = withDefaults(
  defineProps<{
    stateKey: string
    scope: string
    source: string
    loaded: number
    total: number
    updatedAt?: string | null
    pending: boolean
    refreshing: boolean
    loadingMore: boolean
    error?: unknown
    loadMoreError?: unknown
    errorTestId?: string
    failureExplanation?: string
    filterState?: Record<string, unknown>
    emptyDescription?: string
    showMeta?: boolean
  }>(),
  {
    updatedAt: null,
    error: undefined,
    loadMoreError: undefined,
    errorTestId: 'task-list-initial-error',
    failureExplanation: '任务服务未成功返回；已加载数据不会被清空。',
    filterState: () => ({}),
    emptyDescription: '当前筛选范围暂无任务。',
    showMeta: true,
  },
)

const emit = defineEmits<{
  refresh: []
  loadMore: []
  retry: []
  retryLoadMore: []
  restore: [state: PersistedTaskListState]
}>()

const scrollTop = shallowRef(0)
const pendingRestoredScrollTop = shallowRef<number>()
let restoredScrollApplied = false
const storageKey = computed(() => `nerv-iip.business-pda.task-list.${props.stateKey}`)
const hasMore = computed(() => props.loaded < props.total)
const initialError = computed(() => (props.loaded === 0 ? props.error : undefined))
const retainedError = computed(() => (props.loaded > 0 ? props.error : undefined))
const partialError = computed(() => props.loadMoreError)
const isEmpty = computed(
  () => !props.pending && !initialError.value && props.loaded === 0 && props.total === 0,
)

function readState(): PersistedTaskListState | undefined {
  if (typeof sessionStorage === 'undefined') return undefined
  try {
    const raw = sessionStorage.getItem(storageKey.value)
    if (!raw) return undefined
    const parsed = JSON.parse(raw) as Partial<PersistedTaskListState>
    if (!parsed.filters || typeof parsed.filters !== 'object') return undefined
    return {
      filters: parsed.filters as Record<string, unknown>,
      scrollTop: Math.max(0, Number(parsed.scrollTop) || 0),
    }
  } catch {
    return undefined
  }
}

function persistState() {
  if (typeof sessionStorage === 'undefined') return
  sessionStorage.setItem(
    storageKey.value,
    JSON.stringify({ filters: props.filterState, scrollTop: scrollTop.value }),
  )
}

function onScroll(value: number) {
  scrollTop.value = value
  persistState()
}

async function applyPendingRestoredScroll() {
  const target = pendingRestoredScrollTop.value
  if (
    target === undefined ||
    restoredScrollApplied ||
    props.pending ||
    (target > 0 && props.loaded === 0)
  ) {
    return
  }

  await nextTick()
  if (
    pendingRestoredScrollTop.value !== target ||
    restoredScrollApplied ||
    props.pending ||
    (target > 0 && props.loaded === 0)
  ) {
    return
  }

  scrollTop.value = target
  restoredScrollApplied = true
  pendingRestoredScrollTop.value = undefined
}

watch(() => props.filterState, persistState, { deep: true })
watch([() => props.pending, () => props.loaded], () => void applyPendingRestoredScroll(), {
  flush: 'post',
})
onMounted(async () => {
  const restored = readState()
  if (!restored) return
  pendingRestoredScrollTop.value = restored.scrollTop
  emit('restore', restored)
  await nextTick()
  await applyPendingRestoredScroll()
})
</script>

<template>
  <div class="flex h-full min-h-0 flex-1 flex-col">
    <div v-if="$slots.filters" class="shrink-0 border-b border-border bg-card">
      <slot name="filters" />
    </div>

    <div v-if="showMeta" data-testid="task-list-meta" class="shrink-0 px-4 py-3">
      <ListScopeMeta
        :scope="scope"
        :source="source"
        :loaded="loaded"
        :total="total"
        :updated-at="updatedAt"
        :failed="Boolean(initialError || retainedError || partialError)"
        :failure-explanation="failureExplanation"
        :empty="isEmpty"
        :empty-explanation="emptyDescription"
      />
    </div>

    <RetryableListError
      v-if="initialError"
      class="mx-4 mb-3"
      :error="initialError"
      :pending="pending"
      fallback="任务加载失败，请重试。"
      :test-id="errorTestId"
      @retry="emit('retry')"
    />

    <NvPullRefresh
      v-else
      class="min-h-0 flex-1"
      :model-value="refreshing"
      :scroll-top="scrollTop"
      @refresh="emit('refresh')"
      @scroll="onScroll"
    >
      <NvMobileEmpty v-if="isEmpty" :description="emptyDescription" />
      <slot v-else />

      <RetryableListError
        v-if="retainedError"
        class="mx-4 my-3"
        :error="retainedError"
        :pending="refreshing"
        fallback="任务刷新失败，已加载数据保留。"
        test-id="task-list-retained-error"
        @retry="emit('retry')"
      />

      <div
        v-if="partialError"
        data-testid="task-list-load-error"
        class="mx-4 my-3 space-y-2 rounded-xl border border-warning/40 bg-warning/10 p-3 text-sm"
      >
        <p>下一页加载失败，已加载数据保留。</p>
        <RetryableListError
          :error="partialError"
          :pending="loadingMore"
          fallback="下一页加载失败，请重试。"
          :test-id="errorTestId"
          @retry="emit('retryLoadMore')"
        />
      </div>

      <NvInfiniteList
        v-if="loaded > 0"
        parent-scroll
        :model-value="loadingMore"
        :finished="!hasMore"
        @load="emit('loadMore')"
      />
    </NvPullRefresh>
  </div>
</template>
