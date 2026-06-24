<script setup lang="ts" generic="T extends object = Record<string, unknown>">
import type { HTMLAttributes } from 'vue'
import { computed, ref, watch } from 'vue'
import {
  ArrowDownIcon,
  ArrowUpIcon,
  ChevronsUpDownIcon,
  ListFilterIcon,
  RotateCcwIcon,
  SearchIcon,
  Settings2Icon,
  XIcon,
} from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../../ui/table'
import { Checkbox } from '../../ui/checkbox'
import { Skeleton } from '../../ui/skeleton'
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/popover'
import { Separator } from '../../ui/separator'
import ButtonPro from '../button/ButtonPro.vue'
import InputPro from '../input/InputPro.vue'
import DataTablePaginationPro from './DataTablePaginationPro.vue'
import DataTableToolbarPro from './DataTableToolbarPro.vue'
import type {
  DataTableProColumn,
  DataTableProDensity,
  DataTableProFilterOption,
  DataTableProFilters,
  DataTableProSort,
} from './types'

/**
 * Pro — a complete, premium data-table experience (does NOT touch原版 Table).
 * Built-in toolbar (search · field filters · density · column settings · action
 * slot), polished sortable header, row selection, and clickable numbered
 * pagination. Filtering / sorting / paging run client-side over `rows` by
 * default; set `manual` to own the pipeline in the parent.
 */
const props = withDefaults(
  defineProps<{
    columns: DataTableProColumn<T>[]
    rows: T[]
    /** Row key field name or a function returning a stable key. */
    rowKey: string | ((row: T) => string | number)
    loading?: boolean
    title?: string
    description?: string
    emptyMessage?: string
    skeletonRows?: number
    /** Toolbar search box. */
    searchable?: boolean
    searchPlaceholder?: string
    /** Leading checkbox column + bulk bar. */
    selectable?: boolean
    /** Column show/hide + density menu. */
    columnSettings?: boolean
    /** Initial page size. */
    pageSize?: number
    pageSizeOptions?: number[]
    /** Initial density. */
    density?: DataTableProDensity
    /** Selected row keys (v-model:selected). */
    selected?: (string | number)[]
    /** Quick-filter segmented tabs bound to `tabKey`; counts computed live. */
    tabs?: { label: string; value: string }[]
    /** Column key the quick-filter tabs filter on (enum match). */
    tabKey?: string
    /** Show a toolbar refresh button → emits `refresh`. */
    refreshable?: boolean
    /** Make the header stick when the table body scrolls. */
    stickyHeader?: boolean
    /** Constrain body height (enables vertical scroll + sticky header). */
    maxBodyHeight?: string
    class?: HTMLAttributes['class']
  }>(),
  {
    loading: false,
    emptyMessage: '暂无数据',
    skeletonRows: 6,
    searchable: true,
    searchPlaceholder: '搜索…',
    selectable: false,
    columnSettings: true,
    pageSize: 10,
    pageSizeOptions: () => [10, 20, 50, 100],
    density: 'comfortable',
    selected: () => [],
    refreshable: false,
    stickyHeader: true,
  },
)

const emit = defineEmits<{
  'update:selected': [value: (string | number)[]]
  'row-click': [row: T]
  refresh: []
}>()

const alignClass: Record<'start' | 'center' | 'end', string> = {
  start: 'text-left',
  center: 'text-center',
  end: 'text-right',
}

function keyOf(row: T): string | number {
  if (typeof props.rowKey === 'function') return props.rowKey(row)
  return (row as Record<string, unknown>)[props.rowKey] as string | number
}
function valueOf(row: T, column: DataTableProColumn<T>): unknown {
  return column.accessor ? column.accessor(row) : (row as Record<string, unknown>)[column.key]
}

// A raw CSS dimension → inline style; anything else → a Tailwind width class.
function isCssDimension(width?: string): boolean {
  return !!width && /^\d+(\.\d+)?(px|rem|em|%|vh|vw|ch)$/.test(width)
}
const widthStyle = (w?: string) => (isCssDimension(w) ? { width: w } : undefined)
const widthClass = (w?: string) => (w && !isCssDimension(w) ? w : undefined)

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

const innerDensity = ref<DataTableProDensity>(props.density)
const rowPad = computed(() => (innerDensity.value === 'compact' ? 'h-9 py-0' : 'h-12 py-2'))
const cellText = computed(() => (innerDensity.value === 'compact' ? 'text-[0.8125rem]' : 'text-sm'))

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
const filters = ref<DataTableProFilters>({})

function distinctOptions(col: DataTableProColumn<T>): DataTableProFilterOption[] {
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
function labelFor(col: DataTableProColumn<T> | undefined, value: string): string {
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
const sort = ref<DataTableProSort | null>(null)
function cycleSort(col: DataTableProColumn<T>) {
  if (!col.sortable) return
  const cur = sort.value
  if (!cur || cur.key !== col.key) sort.value = { key: col.key, direction: 'asc' }
  else if (cur.direction === 'asc') sort.value = { key: col.key, direction: 'desc' }
  else sort.value = null
}
function sortStateOf(key: string): false | 'asc' | 'desc' {
  return sort.value?.key === key ? sort.value.direction : false
}

// ── Data pipeline: filter → search → sort ──
const processed = computed(() => {
  let out = props.rows.filter((r) => matchesSearch(r) && matchesFilters(r))
  if (sort.value) {
    const { key, direction } = sort.value
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
const page = ref(1)
const innerPageSize = ref(props.pageSize)
const totalItems = computed(() => processed.value.length)
const pagedRows = computed(() => {
  const start = (page.value - 1) * innerPageSize.value
  return processed.value.slice(start, start + innerPageSize.value)
})
// Any pipeline change that shrinks results below the window snaps back to page 1.
function resetPage() {
  page.value = 1
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

const colSpan = computed(() => visibleColumns.value.length + (props.selectable ? 1 : 0))
const hasToolbar = computed(
  () =>
    props.searchable ||
    filterableColumns.value.length > 0 ||
    props.columnSettings ||
    props.refreshable ||
    !!tabsWithCount.value ||
    !!props.title ||
    !!props.description,
)
const showBulk = computed(() => props.selectable && props.selected.length > 0)
const roundTop = computed(() => !hasToolbar.value && !showBulk.value)
</script>

<template>
  <div :class="cn('ds-dt flex flex-col rounded-xl border bg-card shadow-sm', props.class)">
    <!-- ░░ Toolbar (standalone DataTableToolbarPro, composed) ░░ -->
    <DataTableToolbarPro
      v-if="hasToolbar"
      v-model:search="search"
      v-model:tab="activeTab"
      v-model:density="innerDensity"
      surface="plain"
      :title="title"
      :description="description"
      :count="title != null ? totalItems : undefined"
      :tabs="tabsWithCount"
      :searchable="searchable"
      :search-placeholder="searchPlaceholder"
      show-density
      :refreshable="refreshable"
      :loading="loading"
      @refresh="emit('refresh')"
    >
      <!-- Field filters -->
      <template v-if="filterableColumns.length" #filters>
        <Popover>
          <PopoverTrigger as-child>
            <ButtonPro variant="outline" size="sm" class="relative">
              <ListFilterIcon aria-hidden="true" />
              筛选
              <span v-if="activeFilterCount" class="ds-dt-count">{{ activeFilterCount }}</span>
            </ButtonPro>
          </PopoverTrigger>
          <PopoverContent align="end" class="w-72 gap-0 p-0">
            <div class="flex items-center justify-between px-3 py-2.5">
              <span class="text-sm font-medium">字段筛选</span>
              <button
                v-if="activeFilterCount"
                type="button"
                class="text-xs text-muted-foreground transition-colors hover:text-foreground"
                @click="clearAllFilters"
              >
                重置
              </button>
            </div>
            <Separator />
            <div class="max-h-80 space-y-4 overflow-y-auto p-3">
              <div v-for="col in filterableColumns" :key="col.key" class="space-y-2">
                <p class="text-xs font-medium text-muted-foreground">{{ col.header }}</p>
                <InputPro
                  v-if="col.filter === 'text'"
                  :model-value="(filters[col.key] as string) ?? ''"
                  :placeholder="`按${col.header}筛选…`"
                  class="h-8"
                  @update:model-value="(v) => setText(col.key, String(v))"
                />
                <div v-else class="grid grid-cols-1 gap-0.5">
                  <button
                    v-for="opt in distinctOptions(col)"
                    :key="opt.value"
                    type="button"
                    role="checkbox"
                    :aria-checked="enumSelected(col.key).includes(opt.value)"
                    class="ds-dt-opt flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm"
                    @click="
                      toggleEnum(col.key, opt.value, !enumSelected(col.key).includes(opt.value))
                    "
                  >
                    <Checkbox
                      :model-value="enumSelected(col.key).includes(opt.value)"
                      tabindex="-1"
                      aria-hidden="true"
                      class="pointer-events-none"
                    />
                    <span class="truncate">{{ opt.label }}</span>
                  </button>
                </div>
              </div>
            </div>
          </PopoverContent>
        </Popover>
      </template>

      <!-- Column settings -->
      <template v-if="columnSettings && hideableColumns.length" #columns>
        <Popover>
          <PopoverTrigger as-child>
            <ButtonPro variant="outline" size="icon-sm" aria-label="列设置">
              <Settings2Icon aria-hidden="true" />
            </ButtonPro>
          </PopoverTrigger>
          <PopoverContent align="end" class="w-56 gap-0 p-0">
            <div class="flex items-center justify-between px-3 py-2.5">
              <span class="text-sm font-medium">列设置</span>
              <button
                type="button"
                class="inline-flex items-center gap-1 text-xs text-muted-foreground transition-colors hover:text-foreground"
                @click="resetColumns"
              >
                <RotateCcwIcon class="size-3" />
                重置
              </button>
            </div>
            <Separator />
            <div class="max-h-80 space-y-0.5 overflow-y-auto p-1.5">
              <button
                v-for="col in hideableColumns"
                :key="col.key"
                type="button"
                role="checkbox"
                :aria-checked="!hiddenKeys.has(col.key)"
                class="ds-dt-opt flex w-full items-center gap-2.5 rounded-md px-2 py-1.5 text-left text-sm"
                @click="toggleColumn(col.key, hiddenKeys.has(col.key))"
              >
                <Checkbox
                  :model-value="!hiddenKeys.has(col.key)"
                  tabindex="-1"
                  aria-hidden="true"
                  class="pointer-events-none"
                />
                <span class="truncate">{{ col.header }}</span>
              </button>
            </div>
          </PopoverContent>
        </Popover>
      </template>

      <template v-if="$slots.actions" #actions><slot name="actions" /></template>

      <!-- Active filter chips -->
      <template v-if="filterChips.length" #below>
        <div class="flex flex-wrap items-center gap-1.5">
          <span class="text-xs text-muted-foreground">筛选：</span>
          <button
            v-for="chip in filterChips"
            :key="chip.key"
            type="button"
            class="ds-dt-chip"
            @click="clearFilter(chip.key)"
          >
            <span class="text-muted-foreground">{{ chip.header }}</span>
            <span class="font-medium">{{ chip.text }}</span>
            <XIcon class="size-3 opacity-60" aria-hidden="true" />
          </button>
          <button
            type="button"
            class="text-xs text-muted-foreground underline-offset-2 transition-colors hover:text-foreground hover:underline"
            @click="clearAllFilters"
          >
            清除全部
          </button>
        </div>
      </template>
    </DataTableToolbarPro>

    <Separator v-if="hasToolbar" />

    <!-- ░░ Contextual bulk bar (appears alongside the toolbar, never replaces it) ░░ -->
    <Transition name="ds-dt-bulk">
      <div v-if="showBulk" class="ds-dt-bulk-wrap">
        <div class="ds-dt-bulk-inner">
          <div class="ds-dt-bulk flex flex-wrap items-center gap-2 px-3 py-2 sm:px-4">
            <span class="text-sm font-medium">
              已选
              <span class="tabular-nums text-brand-strong">{{ selected.length }}</span>
              项
            </span>
            <ButtonPro variant="ghost" size="sm" class="h-7" @click="clearSelection">
              <XIcon aria-hidden="true" />
              清除
            </ButtonPro>
            <div class="ms-auto flex items-center gap-2">
              <slot name="bulk-actions" :selected="selected" />
            </div>
          </div>
          <Separator />
        </div>
      </div>
    </Transition>

    <!-- ░░ Table ░░ -->
    <div
      class="overflow-auto"
      :class="roundTop ? 'rounded-t-xl' : ''"
      :style="maxBodyHeight ? { maxHeight: maxBodyHeight } : undefined"
    >
      <Table class="ds-dt-table">
        <TableHeader>
          <TableRow class="ds-dt-headrow" :data-sticky="stickyHeader || undefined">
            <TableHead v-if="selectable" class="w-10 ps-3">
              <Checkbox
                :model-value="headerState"
                aria-label="全选"
                @update:model-value="toggleAll"
              />
            </TableHead>
            <TableHead
              v-for="col in visibleColumns"
              :key="col.key"
              :class="
                cn(
                  'ds-dt-th h-10 text-xs font-medium text-muted-foreground',
                  alignClass[col.align ?? 'start'],
                  widthClass(col.width),
                  !col.width && 'ds-dt-fill',
                  col.headerClass,
                )
              "
              :style="widthStyle(col.width)"
            >
              <button
                v-if="col.sortable"
                type="button"
                class="ds-dt-sort group/sort"
                :class="col.align === 'end' ? 'flex-row-reverse' : ''"
                :data-active="sortStateOf(col.key) || undefined"
                @click="cycleSort(col)"
              >
                <span>{{ col.header }}</span>
                <ArrowUpIcon
                  v-if="sortStateOf(col.key) === 'asc'"
                  class="size-3.5"
                  aria-hidden="true"
                />
                <ArrowDownIcon
                  v-else-if="sortStateOf(col.key) === 'desc'"
                  class="size-3.5"
                  aria-hidden="true"
                />
                <ChevronsUpDownIcon
                  v-else
                  class="size-3.5 opacity-0 transition-opacity group-hover/sort:opacity-60"
                  aria-hidden="true"
                />
              </button>
              <span v-else>{{ col.header }}</span>
            </TableHead>
          </TableRow>
        </TableHeader>

        <TableBody>
          <!-- Loading skeleton -->
          <template v-if="loading">
            <TableRow v-for="n in skeletonRows" :key="`sk-${n}`" class="ds-dt-row">
              <TableCell v-if="selectable" class="w-10 ps-3" :class="rowPad">
                <Skeleton class="size-4 rounded-[4px]" />
              </TableCell>
              <TableCell
                v-for="col in visibleColumns"
                :key="col.key"
                :class="cn(rowPad, alignClass[col.align ?? 'start'])"
              >
                <Skeleton
                  class="h-4 w-full max-w-28"
                  :class="col.align === 'end' ? 'ml-auto' : ''"
                />
              </TableCell>
            </TableRow>
          </template>

          <!-- Rows -->
          <template v-else-if="pagedRows.length">
            <TableRow
              v-for="row in pagedRows"
              :key="keyOf(row)"
              class="ds-dt-row"
              :data-state="selectable && selectedSet.has(keyOf(row)) ? 'selected' : undefined"
              @click="emit('row-click', row)"
            >
              <TableCell v-if="selectable" class="w-10 ps-3" :class="rowPad" @click.stop>
                <Checkbox
                  :model-value="selectedSet.has(keyOf(row))"
                  aria-label="选择行"
                  @update:model-value="toggleRow(keyOf(row))"
                />
              </TableCell>
              <TableCell
                v-for="col in visibleColumns"
                :key="col.key"
                :class="cn(rowPad, cellText, alignClass[col.align ?? 'start'], col.cellClass)"
              >
                <slot :name="`cell-${col.key}`" :row="row" :value="valueOf(row, col)" :column="col">
                  {{ valueOf(row, col) }}
                </slot>
              </TableCell>
            </TableRow>
          </template>

          <!-- Empty -->
          <template v-else>
            <TableRow class="hover:bg-transparent">
              <TableCell :colspan="colSpan" class="h-40 p-0">
                <div class="flex flex-col items-center justify-center gap-2 py-10 text-center">
                  <slot name="empty">
                    <div
                      class="flex size-10 items-center justify-center rounded-full bg-muted text-muted-foreground"
                    >
                      <SearchIcon class="size-5" aria-hidden="true" />
                    </div>
                    <p class="text-sm text-muted-foreground">
                      {{ search || activeFilterCount ? '没有匹配的结果' : emptyMessage }}
                    </p>
                    <button
                      v-if="search || activeFilterCount"
                      type="button"
                      class="text-xs text-brand-strong underline-offset-2 hover:underline"
                      @click="resetView"
                    >
                      清除筛选条件
                    </button>
                  </slot>
                </div>
              </TableCell>
            </TableRow>
          </template>
        </TableBody>
      </Table>
    </div>

    <!-- ░░ Footer / pagination ░░ -->
    <template v-if="!loading && totalItems > 0">
      <Separator />
      <DataTablePaginationPro
        class="px-3 py-3 sm:px-4"
        :page="page"
        :page-size="innerPageSize"
        :total-items="totalItems"
        :page-size-options="pageSizeOptions"
        show-jump
        @update:page="page = $event"
        @update:page-size="innerPageSize = $event"
      />
    </template>
  </div>
</template>

<style scoped>
.ds-dt-table {
  border-collapse: separate;
  border-spacing: 0;
}

/* Columns without an explicit `width` share the leftover space so the table
   fills its container instead of leaving dead space on the right (product-first
   default — shadcn's nowrap cells otherwise lock every column to content width).
   `nowrap` keeps each at least content-wide; equal `width:100%` distributes the
   rest evenly. Give a column an explicit `width` to opt it out (e.g. number /
   status columns that should stay compact). */
.ds-dt-fill {
  width: 100%;
}

/* Header: a slightly recessed second surface, sticky when asked. */
.ds-dt-headrow {
  background-color: color-mix(in oklch, var(--muted) 55%, var(--card));
}
.ds-dt-headrow[data-sticky] :deep(th) {
  position: sticky;
  top: 0;
  z-index: 1;
  background-color: color-mix(in oklch, var(--muted) 55%, var(--card));
}
.ds-dt-th :deep(*) {
  vertical-align: middle;
}

.ds-dt-sort {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  border-radius: 5px;
  padding: 0.125rem 0.25rem;
  margin-inline-start: -0.25rem;
  color: var(--muted-foreground);
  outline: none;
  transition: color 0.15s var(--ease-out-quart, ease-out);
}
.ds-dt-sort:hover,
.ds-dt-sort[data-active] {
  color: var(--foreground);
}
.ds-dt-sort:focus-visible {
  box-shadow: 0 0 0 3px color-mix(in oklch, var(--ring) 45%, transparent);
}

/* Rows: hairline divider + calm hover; selection tinted with the brand. */
.ds-dt-row {
  transition: background-color 0.15s var(--ease-out-quart, ease-out);
}
.ds-dt-row:hover {
  background-color: color-mix(in oklch, var(--muted) 45%, transparent);
}
.ds-dt-row[data-state='selected'] {
  background-color: color-mix(in oklch, var(--brand) 8%, transparent);
}
.ds-dt-row[data-state='selected']:hover {
  background-color: color-mix(in oklch, var(--brand) 12%, transparent);
}

/* Contextual selection strip — a brand-tinted band signalling bulk mode. */
.ds-dt-bulk {
  background-color: color-mix(in oklch, var(--brand) 7%, var(--card));
}
.ds-dt-bulk-wrap {
  display: grid;
  grid-template-rows: 1fr;
}
.ds-dt-bulk-inner {
  overflow: hidden;
  min-height: 0;
}
.ds-dt-bulk-enter-active,
.ds-dt-bulk-leave-active {
  transition:
    opacity 0.2s var(--ease-out-quart, ease-out),
    grid-template-rows 0.2s var(--ease-out-quart, ease-out);
}
.ds-dt-bulk-enter-from,
.ds-dt-bulk-leave-to {
  opacity: 0;
  grid-template-rows: 0fr;
}

.ds-dt-clear {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 9999px;
  color: var(--muted-foreground);
  transition:
    color 0.15s ease,
    background-color 0.15s ease;
}
.ds-dt-clear:hover {
  color: var(--foreground);
  background-color: var(--muted);
}

/* Count pip on the filter button. */
.ds-dt-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.125rem;
  height: 1.125rem;
  padding-inline: 0.25rem;
  border-radius: 9999px;
  background-color: var(--brand);
  color: var(--brand-foreground);
  font-size: 0.6875rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.ds-dt-opt {
  transition: background-color 0.12s ease;
}
.ds-dt-opt:hover {
  background-color: var(--muted);
}

/* Active-filter chips. */
.ds-dt-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  height: 1.625rem;
  padding-inline: 0.5rem;
  border-radius: 7px;
  border: 1px solid var(--border);
  background-color: color-mix(in oklch, var(--muted) 50%, var(--card));
  font-size: 0.75rem;
  color: var(--foreground);
  transition:
    border-color 0.15s ease,
    background-color 0.15s ease;
}
.ds-dt-chip:hover {
  border-color: color-mix(in oklch, var(--foreground) 20%, transparent);
  background-color: var(--muted);
}

@media (prefers-reduced-motion: reduce) {
  .ds-dt-row,
  .ds-dt-sort,
  .ds-dt-bulk-enter-active,
  .ds-dt-bulk-leave-active {
    transition: none;
  }
}
</style>
