import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter, type Ref } from 'vue'

export interface TaskListPage<TItem> {
  items: TItem[]
  total: number
}

interface TaskListPageRequest {
  skip: number
  take: number
}

interface UseTaskListPaginationOptions<TItem> {
  identity: MaybeRefOrGetter<string>
  firstPage: Readonly<Ref<TaskListPage<TItem> | undefined>>
  pageSize?: number
  itemKey: (item: TItem) => string
  fetchPage: (request: TaskListPageRequest) => Promise<TaskListPage<TItem>>
  refreshFirstPage: () => Promise<unknown>
}

export function useTaskListPagination<TItem>(options: UseTaskListPaginationOptions<TItem>) {
  const pageSize = Math.max(1, options.pageSize ?? 20)
  const items = shallowRef<TItem[]>([])
  const total = shallowRef(0)
  const nextSkip = shallowRef(0)
  const loadingMore = shallowRef(false)
  const refreshing = shallowRef(false)
  const loadMoreError = shallowRef<unknown>()
  let generation = 0

  function reset() {
    generation += 1
    items.value = []
    total.value = 0
    nextSkip.value = 0
    loadingMore.value = false
    loadMoreError.value = undefined
  }

  function mergePage(page: TaskListPage<TItem>, replace: boolean) {
    const merged = replace ? [] : [...items.value]
    const seen = new Set(merged.map(options.itemKey))
    for (const item of page.items) {
      const key = options.itemKey(item)
      if (seen.has(key)) continue
      seen.add(key)
      merged.push(item)
    }
    items.value = merged
    total.value = Math.max(0, page.total)
    nextSkip.value = replace ? page.items.length : nextSkip.value + page.items.length
  }

  watch(() => toValue(options.identity), reset, { flush: 'sync' })

  watch(
    options.firstPage,
    (page) => {
      if (!page) return
      mergePage(page, true)
      loadMoreError.value = undefined
    },
    { immediate: true, flush: 'sync' },
  )

  const loaded = computed(() => items.value.length)
  const hasMore = computed(() => nextSkip.value < total.value)

  async function loadMore() {
    if (loadingMore.value || !hasMore.value) return
    const request = { skip: nextSkip.value, take: pageSize }
    const currentGeneration = generation
    loadingMore.value = true
    loadMoreError.value = undefined
    try {
      const page = await options.fetchPage(request)
      if (generation !== currentGeneration) return
      mergePage(page, false)
    } catch (error) {
      if (generation === currentGeneration) loadMoreError.value = error
    } finally {
      if (generation === currentGeneration) loadingMore.value = false
    }
  }

  async function refresh() {
    reset()
    refreshing.value = true
    try {
      await options.refreshFirstPage()
    } finally {
      refreshing.value = false
    }
  }

  return {
    items,
    total,
    loaded,
    hasMore,
    loadingMore,
    refreshing,
    loadMoreError,
    loadMore,
    refresh,
    reset,
  }
}
