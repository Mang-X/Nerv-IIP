import type { WatchSource } from 'vue'
import { computed, shallowRef, watch } from 'vue'

export interface PagedListFilters {
  skip?: number
  take?: number
}

export interface UsePagedListOptions {
  initialPageSize?: string
  resetOn?: WatchSource<unknown>[]
}

export function usePagedList(filters: PagedListFilters, options: UsePagedListOptions = {}) {
  const page = shallowRef(1)
  const pageSize = shallowRef(options.initialPageSize ?? '10')
  const pageSizeNumber = computed(() => Number(pageSize.value) || 10)

  function resetPage() {
    page.value = 1
  }

  watch(pageSize, resetPage)

  if (options.resetOn?.length) {
    watch(options.resetOn, resetPage)
  }

  watch([page, pageSize], () => {
    filters.skip = (page.value - 1) * pageSizeNumber.value
    filters.take = pageSizeNumber.value
  }, { immediate: true })

  return {
    page,
    pageSize,
    pageSizeNumber,
    resetPage,
  }
}
