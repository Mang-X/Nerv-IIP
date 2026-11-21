import { computed, ref, watch } from 'vue'
import type {
  NvDataTableColumn,
  NvDataTableDensity,
  NvDataTableFilterOption,
  NvDataTableFilters,
  NvDataTableSort,
} from './types'

interface DataTableStateProps<T extends object> {
  columns: NvDataTableColumn<T>[]
  rows: T[]
  rowKey: string | ((row: T) => string | number)
  loading: boolean
  error?: unknown
  errorMessage?: string
  awaitingScope: boolean
  pagination: boolean
  manual: boolean
  page?: number
  totalItems?: number
  pageSize: number | string
  sort?: NvDataTableSort | null
  clientSort: boolean
  pageSizeOptions: number[]
  density: NvDataTableDensity
  selected: (string | number)[]
  tabs?: { label: string; value: string }[]
  tabKey?: string
}

interface DataTableStateEmit {
  (event: 'update:selected', value: (string | number)[]): void
  (event: 'update:page', value: number): void
  (event: 'update:pageSize', value: number): void
  (event: 'update:sort', value: NvDataTableSort | null): void
}

export function useDataTableState<T extends object>(
  props: DataTableStateProps<T>,
  emit: DataTableStateEmit,
) {
  function keyOf(row: T): string | number {
    if (typeof props.rowKey === 'function') return props.rowKey(row)
    return (row as Record<string, unknown>)[props.rowKey] as string | number
  }
  function valueOf(row: T, column: NvDataTableColumn<T>): unknown {
    return column.accessor ? column.accessor(row) : (row as Record<string, unknown>)[column.key]
  }

  // ── Column visibility & density ──
  const hiddenKeys = ref(new Set(props.columns.filter((c) => c.defaultHidden).map((c) => c.key)))
  const visibleColumns = computed(() => props.columns.filter((c) => !hiddenKeys.value.has(c.key)))
  function toggleColumn(key: string, visible: boolean) {
    const next = new Set(hiddenKeys.value)
    if (visible) next.delete(key)
    else next.add(key)
    hiddenKeys.value = next
  }
  function resetColumns() {
    hiddenKeys.value = new Set(props.columns.filter((c) => c.defaultHidden).map((c) => c.key))
  }
  const hideableColumns = computed(() => props.columns.filter((c) => c.hideable !== false))

  const innerDensity = ref<NvDataTableDensity>(props.density)
  const rowPad = computed(() => (innerDensity.value === 'compact' ? 'h-9 py-0' : 'h-12 py-2'))
  const cellText = computed(() =>
    innerDensity.value === 'compact' ? 'text-[0.8125rem]' : 'text-sm',
  )

  // ── Search ──
  const search = ref('')
  const searchKeys = computed(() => props.columns.map((c) => c.key))
  function matchesSearch(row: T): boolean {
    const q = search.value.trim().toLowerCase()
    if (!q) return true
    return searchKeys.value.some((k) => {
      const col = props.columns.find((c) => c.key === k)
      if (!col) return false
      const v = valueOf(row, col)
      return v != null && String(v).toLowerCase().includes(q)
    })
  }
  function resetView() {
    search.value = ''
    filters.value = {}
    resetPage()
  }
  // Searching narrows the result set — always snap back to the first page.
  watch(search, () => resetPage())

  // ── Per-column filters ──
  const filterableColumns = computed(() => props.columns.filter((c) => c.filter))
  const filters = ref<NvDataTableFilters>({})

  function distinctOptions(col: NvDataTableColumn<T>): NvDataTableFilterOption[] {
    if (col.filterOptions) return col.filterOptions
    const seen = new Map<string, string>()
    for (const row of props.rows) {
      const v = valueOf(row, col)
      if (v == null) continue
      const s = String(v)
      if (!seen.has(s)) seen.set(s, s)
    }
    return [...seen.values()]
      .sort((a, b) => a.localeCompare(b, 'zh-Hans-CN'))
      .map((v) => ({ label: v, value: v }))
  }

  function enumSelected(key: string): string[] {
    const v = filters.value[key]
    return Array.isArray(v) ? v : []
  }
  function toggleEnum(key: string, value: string, on: boolean) {
    const cur = new Set(enumSelected(key))
    if (on) cur.add(value)
    else cur.delete(value)
    const next = { ...filters.value }
    if (cur.size) next[key] = [...cur]
    else delete next[key]
    filters.value = next
    resetPage()
  }
  function setText(key: string, value: string) {
    const next = { ...filters.value }
    if (value.trim()) next[key] = value
    else delete next[key]
    filters.value = next
    resetPage()
  }
  function clearFilter(key: string) {
    const next = { ...filters.value }
    delete next[key]
    filters.value = next
    resetPage()
  }
  function clearAllFilters() {
    filters.value = {}
    resetPage()
  }
  const activeFilterCount = computed(() => Object.keys(filters.value).length)

  // ── Quick-filter tabs (a segmented shortcut over the `tabKey` enum filter) ──
  const tabsWithCount = computed(() => {
    if (!props.tabs || !props.tabKey) return undefined
    const col = props.columns.find((c) => c.key === props.tabKey)
    return props.tabs.map((t) => ({
      ...t,
      count:
        t.value === ''
          ? props.rows.length
          : props.rows.filter((r) => String(col ? valueOf(r, col) : '') === t.value).length,
    }))
  })
  const activeTab = computed<string>({
    get() {
      if (!props.tabKey) return ''
      const f = filters.value[props.tabKey]
      return Array.isArray(f) && f.length === 1 ? f[0] : ''
    },
    set(value) {
      if (!props.tabKey) return
      if (!value) {
        clearFilter(props.tabKey)
      } else {
        filters.value = { ...filters.value, [props.tabKey]: [value] }
        resetPage()
      }
    },
  })

  function matchesFilters(row: T): boolean {
    for (const col of filterableColumns.value) {
      const f = filters.value[col.key]
      if (f == null) continue
      const raw = valueOf(row, col)
      const v = raw == null ? '' : String(raw)
      if (col.filter === 'text') {
        if (!v.toLowerCase().includes(String(f).toLowerCase())) return false
      } else if (col.filter === 'enum') {
        const sel = Array.isArray(f) ? f : [f]
        if (sel.length && !sel.includes(v)) return false
      }
    }
    return true
  }

  // Map an enum value back to its human label (falls back to the raw value).
  function labelFor(col: NvDataTableColumn<T> | undefined, value: string): string {
    return col?.filterOptions?.find((o) => o.value === value)?.label ?? value
  }

  // Active filter chips (label + value preview) for the summary row.
  interface FilterChip {
    key: string
    header: string
    text: string
  }
  const filterChips = computed<FilterChip[]>(() =>
    Object.keys(filters.value).map((key) => {
      const col = props.columns.find((c) => c.key === key)
      const f = filters.value[key]
      const text = Array.isArray(f) ? f.map((v) => labelFor(col, v)).join('、') : String(f)
      return { key, header: col?.header ?? key, text }
    }),
  )

  // ── Sorting (client-side) ──
  const innerSort = ref<NvDataTableSort | null>(null)
  // Controlled when the parent passes `sort` (including null); otherwise internal.
  const currentSort = computed(() => (props.sort !== undefined ? props.sort : innerSort.value))
  function cycleSort(col: NvDataTableColumn<T>) {
    if (!col.sortable) return
    const cur = currentSort.value
    let next: NvDataTableSort | null
    if (!cur || cur.key !== col.key) next = { key: col.key, direction: 'asc' }
    else if (cur.direction === 'asc') next = { key: col.key, direction: 'desc' }
    else next = null
    emit('update:sort', next)
    if (props.sort === undefined) innerSort.value = next
  }
  function sortStateOf(key: string): false | 'asc' | 'desc' {
    return currentSort.value?.key === key ? currentSort.value.direction : false
  }

  // ── Data pipeline: filter → search → sort ──
  const processed = computed(() => {
    let out = props.rows.filter((r) => matchesSearch(r) && matchesFilters(r))
    if (props.clientSort && currentSort.value) {
      const { key, direction } = currentSort.value
      const col = props.columns.find((c) => c.key === key)
      const sign = direction === 'desc' ? -1 : 1
      out = [...out].sort((a, b) => {
        const av = col ? valueOf(a, col) : (a as Record<string, unknown>)[key]
        const bv = col ? valueOf(b, col) : (b as Record<string, unknown>)[key]
        if (av == null && bv == null) return 0
        if (av == null) return -sign
        if (bv == null) return sign
        const r =
          typeof av === 'number' && typeof bv === 'number'
            ? av - bv
            : String(av).localeCompare(String(bv), 'zh-Hans-CN')
        return r * sign
      })
    }
    return out
  })

  // ── Pagination ──
  const innerPage = ref(1)
  const innerPageSize = ref(Number(props.pageSize) || 10)
  // Manual (server-driven) mode: parent owns page / size / total. Otherwise paginate client-side.
  const currentPage = computed(() => (props.manual ? (props.page ?? 1) : innerPage.value))
  const currentPageSize = computed(() =>
    props.manual ? Number(props.pageSize) || 10 : innerPageSize.value,
  )
  const resolvedTotal = computed(() =>
    props.manual ? (props.totalItems ?? processed.value.length) : processed.value.length,
  )
  /**
   * A pager under a list that already shows everything is pure furniture — it
   * offers a page to jump to that is the page you are on, and a size to pick that
   * changes nothing. Hide it only when even the SMALLEST offered page size would
   * still fit the whole result: below that threshold every control is inert, above
   * it shrinking the page size is a real (if unusual) choice the user keeps.
   */
  const smallestPageSize = computed(() =>
    props.pageSizeOptions.length > 0 ? Math.min(...props.pageSizeOptions) : currentPageSize.value,
  )
  const showPagination = computed(
    () =>
      props.pagination &&
      !props.loading &&
      // 失败 / 未查询时页脚是假信息（manual 模式下 totalItems 还留着上一次的总数）。
      !hasError.value &&
      !props.awaitingScope &&
      resolvedTotal.value > Math.max(1, smallestPageSize.value),
  )
  const pagedRows = computed(() => {
    // Manual mode (parent already paged) or pagination off → render the given rows verbatim.
    if (props.manual || !props.pagination) return processed.value
    const start = (innerPage.value - 1) * innerPageSize.value
    return processed.value.slice(start, start + innerPageSize.value)
  })
  function setPage(p: number) {
    if (props.manual) emit('update:page', p)
    else innerPage.value = p
  }
  function setPageSize(s: number) {
    if (props.manual) {
      emit('update:pageSize', s)
      return
    }
    innerPageSize.value = s
    innerPage.value = 1
  }
  // Any pipeline change that shrinks results below the window snaps back to page 1.
  function resetPage() {
    if (props.manual) emit('update:page', 1)
    else innerPage.value = 1
  }

  // ── Selection (covers the rows currently in view) ──
  const selectedSet = computed(() => new Set(props.selected))
  const visibleKeys = computed(() => pagedRows.value.map((r) => keyOf(r)))
  const allSelected = computed(
    () => visibleKeys.value.length > 0 && visibleKeys.value.every((k) => selectedSet.value.has(k)),
  )
  const someSelected = computed(
    () => !allSelected.value && visibleKeys.value.some((k) => selectedSet.value.has(k)),
  )
  const headerState = computed<boolean | 'indeterminate'>(() =>
    allSelected.value ? true : someSelected.value ? 'indeterminate' : false,
  )
  function toggleAll() {
    if (allSelected.value) {
      const remove = new Set(visibleKeys.value)
      emit(
        'update:selected',
        props.selected.filter((k) => !remove.has(k)),
      )
    } else {
      const next = new Set(props.selected)
      for (const k of visibleKeys.value) next.add(k)
      emit('update:selected', [...next])
    }
  }
  function toggleRow(key: string | number) {
    const next = new Set(props.selected)
    if (next.has(key)) next.delete(key)
    else next.add(key)
    emit('update:selected', [...next])
  }
  function clearSelection() {
    emit('update:selected', [])
  }

  // ── 表体状态机：加载 / 失败 / 未查询 / 有数据 / 空 ──
  // 失败最高优先：一次 500 与「真的 0 条」必须长得不一样，绝不允许失败落进空态。
  // `error` + `loading` 同时为真 = 正在重试，仍停在错误态，只把重试按钮切成进行中。
  const hasError = computed(() => {
    const e = props.error
    return e !== undefined && e !== null && e !== false && e !== ''
  })
  const resolvedErrorMessage = computed(() => {
    if (props.errorMessage?.trim()) return props.errorMessage
    const e = props.error
    if (typeof e === 'string' && e.trim()) return e
    const m = (e as { message?: unknown } | null | undefined)?.message
    if (typeof m === 'string' && m.trim()) return m
    return '请检查网络连接后重试；若持续失败请联系管理员。'
  })
  const bodyState = computed<'error' | 'loading' | 'awaiting' | 'rows' | 'empty'>(() => {
    if (hasError.value) return 'error'
    if (props.loading) return 'loading'
    if (props.awaitingScope) return 'awaiting'
    return pagedRows.value.length ? 'rows' : 'empty'
  })

  return {
    keyOf,
    valueOf,
    hiddenKeys,
    visibleColumns,
    toggleColumn,
    resetColumns,
    hideableColumns,
    innerDensity,
    rowPad,
    cellText,
    search,
    resetView,
    filterableColumns,
    filters,
    distinctOptions,
    enumSelected,
    toggleEnum,
    setText,
    clearFilter,
    clearAllFilters,
    activeFilterCount,
    tabsWithCount,
    activeTab,
    filterChips,
    cycleSort,
    sortStateOf,
    currentPage,
    currentPageSize,
    resolvedTotal,
    showPagination,
    pagedRows,
    setPage,
    setPageSize,
    selectedSet,
    headerState,
    toggleAll,
    toggleRow,
    clearSelection,
    resolvedErrorMessage,
    bodyState,
  }
}
