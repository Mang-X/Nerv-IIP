<script setup lang="ts" generic="T extends object = Record<string, unknown>">
import type { HTMLAttributes } from 'vue'
import { computed, ref } from 'vue'
import {
  ArrowDownIcon,
  ArrowUpIcon,
  ChevronsUpDownIcon,
  CircleHelpIcon,
  ListFilterIcon,
  RotateCcwIcon,
  Settings2Icon,
  XIcon,
} from '@lucide/vue'
import { displayValue, isEmptyValue } from '../../../lib/empty'
import { cn } from '../../../lib/utils'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '../../ui/table'
import { Checkbox } from '../../ui/checkbox'
import { Skeleton } from '../../ui/skeleton'
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/popover'
import { Separator } from '../../ui/separator'
import NvButton from '../button/NvButton.vue'
import NvInput from '../input/NvInput.vue'
import NvPagination from '../pagination/NvPagination.vue'
import { NvTooltip, NvTooltipContent, NvTooltipProvider, NvTooltipTrigger } from '../tooltip'
import NvDataTableToolbar from './NvDataTableToolbar.vue'
import type { NvDataTableColumn, NvDataTableDensity, NvDataTableSort } from './types'
import NvDataTableFilterChips from './NvDataTableFilterChips.vue'
import NvDataTableStatusRows from './NvDataTableStatusRows.vue'
import { useDataTableState } from './useDataTableState'
/**
 * Pro — a complete, premium data-table experience (does NOT touch原版 Table).
 * Built-in toolbar (search · field filters · density · column settings · action
 * slot), polished sortable header, row selection, and clickable numbered
 * pagination. Filtering / sorting / paging run client-side over `rows` by
 * default; set `manual` to own the pipeline in the parent.
 */
const props = withDefaults(
  defineProps<{
    columns: NvDataTableColumn<T>[]
    rows: T[]
    /** Row key field name or a function returning a stable key. */
    rowKey: string | ((row: T) => string | number)
    loading?: boolean
    title?: string
    description?: string
    emptyMessage?: string
    /**
     * 请求失败时传入错误（Error / 响应对象 / 现成的中文消息）。非空即进入**错误态**。
     *
     * 选型理由：这里刻意不引入统一的 `state: 'idle'|'loading'|'error'|'ready'` 枚举——
     * 1) 已有 85 个调用点传的是布尔 `loading`，再加一个 `state` 会出现两套并存的真相
     *    来源（`loading` 与 `state==='loading'`），互相打架且无法向后兼容；
     * 2) 页面侧的数据来自 query composable，天然产出 `error` 对象与「范围是否选定」的
     *    布尔量，离散 prop 可直接绑定，不必在每个调用点手写一层状态映射。
     * 组件内部仍会归一成单一的 `bodyState`，优先级在 `bodyState` 处说明。
     */
    error?: unknown
    /** 覆盖错误态正文；默认从 `error` 上取 message 文本。 */
    errorMessage?: string
    /**
     * 业务范围尚未选定（还没查）。与「查了但 0 条」是两件事，必须分开表达，
     * 否则用户看到「暂无数据」会以为真的没有数据。
     */
    awaitingScope?: boolean
    /** 未查询态的引导语：一句话说清还需要选什么，别写成说明书。 */
    awaitingScopeMessage?: string
    skeletonRows?: number
    /** Toolbar search box. */
    searchable?: boolean
    searchPlaceholder?: string
    /** Leading checkbox column + bulk bar. */
    selectable?: boolean
    /** Column show/hide + density menu. */
    columnSettings?: boolean
    /** Pagination footer. Default on. */
    pagination?: boolean
    /** Server-driven pagination: parent owns the page window. Rows render verbatim (no client
     *  slicing); the footer reflects `page` / `total` and emits `update:page` / `update:pageSize`. */
    manual?: boolean
    /** Controlled current page (1-based) — manual mode (`v-model:page`). */
    page?: number
    /** External total row count — manual mode（对应调用点的 `:total-items`）。 */
    totalItems?: number
    /** Initial (client mode) or controlled (manual mode) page size. */
    pageSize?: number | string
    /** Controlled sort state (`v-model:sort`); uncontrolled (internal) when omitted. */
    sort?: NvDataTableSort | null
    /** Sort rows client-side. Turn off (`:client-sort="false"`) when the parent sorts
     *  (server-side / page-controlled) and feeds already-sorted rows. */
    clientSort?: boolean
    pageSizeOptions?: number[]
    /** Initial density. */
    density?: NvDataTableDensity
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
    /** Per-row class hook — de-emphasize / tint rows by state (e.g. resolved items). */
    rowClass?: string | ((row: T) => string | undefined)
    class?: HTMLAttributes['class']
  }>(),
  {
    loading: false,
    emptyMessage: '暂无数据',
    awaitingScope: false,
    awaitingScopeMessage: '请先在上方选择查询范围。',
    skeletonRows: 6,
    searchable: true,
    searchPlaceholder: '搜索…',
    selectable: false,
    columnSettings: true,
    pagination: true,
    manual: false,
    clientSort: true,
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
  'update:page': [value: number]
  'update:pageSize': [value: number]
  'update:sort': [value: NvDataTableSort | null]
  'row-click': [row: T]
  refresh: []
  /**
   * 错误态里的「重新加载」。与 `refresh` 分开：`refresh` 是用户在**已有结果**上主动
   * 要最新数据（工具栏刷新按钮），`retry` 是失败后的恢复动作——调用点往往要额外清错、
   * 打点或换一条重试路径。组件不会把 `retry` 同时当作 `refresh` 发出去，两者不重叠。
   */
  retry: []
}>()

const alignClass: Record<'start' | 'center' | 'end', string> = {
  start: 'text-left',
  center: 'text-center',
  end: 'text-right',
}

// 2) Tailwind 宽度类（`w-48`、`w-[22rem]`）→ 原样作为 class，Tailwind 自己解析成
//    width。任意值写法 `w-[…]` 也走这条，`table-layout: fixed` 只认 width，
//    而 w-* 解析出来的正是 width。
//
// 这里刻意不解析 `min-w-*`：它的 `min-width` 在 fixed 下不参与列宽计算，若靠正则
// 反推等价 width 等于在本仓库重写一份 Tailwind 主题表，认不出的写法会静默变成
// 0px 的列。要「至少这么宽」就写 `w-[22rem]`。
function isCssDimension(width?: string): boolean {
  return !!width && /^\d+(\.\d+)?(px|rem|em|%|vh|vw|ch)$/.test(width)
}
const widthStyle = (w?: string) => (isCssDimension(w) ? { width: w } : undefined)
const widthClass = (w?: string) => (w && !isCssDimension(w) ? w : undefined)

const headerHelpOpen = ref<Record<string, boolean>>({})

function setHeaderHelpOpen(key: string, open: boolean) {
  headerHelpOpen.value[key] = open
}

function openHeaderHelpForTouch(event: PointerEvent, key: string) {
  if (event.pointerType === 'touch') setHeaderHelpOpen(key, true)
}

const {
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
} = useDataTableState(props, emit)

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
  <div :class="cn('nv-dt flex flex-col rounded-xl border bg-card shadow-sm', props.class)">
    <!-- ░░ Toolbar (standalone NvDataTableToolbar, composed) ░░ -->
    <NvDataTableToolbar
      v-if="hasToolbar"
      v-model:search="search"
      v-model:tab="activeTab"
      v-model:density="innerDensity"
      surface="plain"
      :title="title"
      :description="description"
      :count="
        title != null && bodyState !== 'error' && bodyState !== 'awaiting'
          ? resolvedTotal
          : undefined
      "
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
            <NvButton variant="outline" size="sm" class="relative">
              <ListFilterIcon aria-hidden="true" />
              筛选
              <span v-if="activeFilterCount" class="nv-dt-count">{{ activeFilterCount }}</span>
            </NvButton>
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
                <NvInput
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
                    class="nv-dt-opt flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm"
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
            <NvButton variant="outline" size="icon-sm" aria-label="列设置">
              <Settings2Icon aria-hidden="true" />
            </NvButton>
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
                class="nv-dt-opt flex w-full items-center gap-2.5 rounded-md px-2 py-1.5 text-left text-sm"
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
        <NvDataTableFilterChips
          :filter-chips="filterChips"
          @clear="clearFilter"
          @clear-all="clearAllFilters"
        />
      </template>
    </NvDataTableToolbar>

    <Separator v-if="hasToolbar" />

    <!-- ░░ Contextual bulk bar (appears alongside the toolbar, never replaces it) ░░ -->
    <Transition name="nv-dt-bulk">
      <div v-if="showBulk" class="nv-dt-bulk-wrap">
        <div class="nv-dt-bulk-inner">
          <div class="nv-dt-bulk flex flex-wrap items-center gap-2 px-3 py-2 sm:px-4">
            <span class="text-sm font-medium">
              已选
              <span class="tabular-nums text-brand-strong">{{ selected.length }}</span>
              项
            </span>
            <NvButton variant="ghost" size="sm" class="h-7" @click="clearSelection">
              <XIcon aria-hidden="true" />
              清除
            </NvButton>
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
      <Table class="nv-dt-table">
        <TableHeader>
          <TableRow class="nv-dt-headrow" :data-sticky="stickyHeader || undefined">
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
                  'nv-dt-th h-10 text-xs font-medium text-muted-foreground',
                  alignClass[col.align ?? 'start'],
                  widthClass(col.width),
                  !col.width && 'nv-dt-fill',
                  col.headerClass,
                )
              "
              :style="widthStyle(col.width)"
            >
              <NvTooltipProvider v-if="col.headerTitle">
                <NvTooltip
                  :open="headerHelpOpen[col.key] ?? false"
                  disable-closing-trigger
                  @update:open="setHeaderHelpOpen(col.key, $event)"
                >
                  <NvTooltipTrigger as-child>
                    <button
                      v-if="col.sortable"
                      type="button"
                      class="nv-dt-sort group/sort"
                      :class="col.align === 'end' ? 'flex-row-reverse' : ''"
                      :data-active="sortStateOf(col.key) || undefined"
                      :aria-label="`${col.header}：${col.headerTitle}`"
                      @click="cycleSort(col)"
                      @pointerup="openHeaderHelpForTouch($event, col.key)"
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
                      <ChevronsUpDownIcon v-else class="size-3.5 opacity-60" aria-hidden="true" />
                    </button>
                    <button
                      v-else
                      type="button"
                      class="inline-flex items-center gap-1 rounded-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
                      :aria-label="`${col.header}：${col.headerTitle}`"
                      @pointerup="openHeaderHelpForTouch($event, col.key)"
                    >
                      <span>{{ col.header }}</span>
                      <CircleHelpIcon class="size-3.5" aria-hidden="true" />
                    </button>
                  </NvTooltipTrigger>
                  <NvTooltipContent>{{ col.headerTitle }}</NvTooltipContent>
                </NvTooltip>
              </NvTooltipProvider>
              <button
                v-else-if="col.sortable"
                type="button"
                class="nv-dt-sort group/sort"
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
          <template v-if="bodyState === 'loading'">
            <TableRow v-for="n in skeletonRows" :key="`sk-${n}`" class="nv-dt-row">
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
          <template v-else-if="bodyState === 'rows'">
            <TableRow
              v-for="row in pagedRows"
              :key="keyOf(row)"
              :class="cn('nv-dt-row', typeof rowClass === 'function' ? rowClass(row) : rowClass)"
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
                <!-- 兜底渲染统一走空值占位：空单元格此前渲染成完全空白的 <td>，
                     读者分不清「没有值」还是「界面坏了」。插槽拿到的 `value`
                     仍是原始值，调用点想自己处理空值不受影响。 -->
                <slot :name="`cell-${col.key}`" :row="row" :value="valueOf(row, col)" :column="col">
                  <span
                    :class="isEmptyValue(valueOf(row, col)) ? 'text-muted-foreground' : undefined"
                    >{{ displayValue(valueOf(row, col)) }}</span
                  >
                </slot>
              </TableCell>
            </TableRow>
          </template>
          <NvDataTableStatusRows
            v-else
            :body-state="bodyState"
            :col-span="colSpan"
            :error="error"
            :resolved-error-message="resolvedErrorMessage"
            :loading="loading"
            :awaiting-scope-message="awaitingScopeMessage"
            :empty-message="emptyMessage"
            :has-active-search="!!search || activeFilterCount > 0"
            @retry="emit('retry')"
            @reset="resetView"
          >
            <template v-if="$slots.error" #error="slotProps"
              ><slot name="error" v-bind="slotProps"
            /></template>
            <template v-if="$slots.awaiting" #awaiting><slot name="awaiting" /></template>
            <template v-if="$slots.empty" #empty><slot name="empty" /></template>
          </NvDataTableStatusRows>
        </TableBody>
      </Table>
    </div>

    <!-- ░░ Footer / pagination ░░ -->
    <template v-if="showPagination">
      <Separator />
      <NvPagination
        class="px-3 py-3 sm:px-4"
        :page="currentPage"
        :page-size="currentPageSize"
        :total-items="resolvedTotal"
        :page-size-options="pageSizeOptions"
        show-jump
        @update:page="setPage"
        @update:page-size="setPageSize"
      />
    </template>
  </div>
</template>

<style scoped>
@layer nv-components {
  /* `table-layout: fixed` is what makes a column's `width` a real contract
   instead of a suggestion: under the default `auto`, a width is only a hint the
   layout algorithm is free to trade away against cell content, so a declared
   column could render at its min-content and clip its own content (the receipt
   status badge rendered one character wide — #3734). `fixed` sizes columns from
   the first row's declared widths alone, so no column is ever narrowed or
   widened to fit its content; when the declarations leave spare room, the
   surplus is shared out across the columns.

   `min-width: 100%` (on top of the primitive's `w-full`) is what keeps a narrow
   window honest: the table can grow past the container and let the primitive's
   `overflow-x-auto` wrapper scroll it, instead of squeezing the columns. Without
   it, `fixed` would let the undeclared columns collapse toward 0px.

   `:deep()` is required, not decoration: `Table` primitive's root element is the
   scroll wrapper `<div>`, so Vue puts this component's scope id on that div —
   the inner `<table>` never carries it, and a bare `.nv-dt-table` selector here
   matched nothing at runtime (#3734 第 1 轮的实现就是死 CSS). */
  :deep(.nv-dt-table) {
    table-layout: fixed;
    min-width: 100%;
    border-collapse: separate;
    border-spacing: 0;
  }

  /* Columns without an explicit `width` share the leftover space, so the table
   fills its container instead of leaving dead space on the right. Under `fixed`
   these resolve to the table's leftover width once the declared columns are
   subtracted; give a column an explicit `width` to take it out of that share
   (number / status columns that should stay compact). */
  .nv-dt-fill {
    width: 100%;
  }

  /* Header: a slightly recessed second surface, sticky when asked. */
  .nv-dt-headrow {
    background-color: color-mix(in oklch, var(--muted) 55%, var(--card));
  }
  .nv-dt-headrow[data-sticky] :deep(th) {
    position: sticky;
    top: 0;
    z-index: 1;
    background-color: color-mix(in oklch, var(--muted) 55%, var(--card));
  }
  .nv-dt-th :deep(*) {
    vertical-align: middle;
  }

  .nv-dt-sort {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    border-radius: 5px;
    padding: 0.125rem 0.25rem;
    margin-inline-start: -0.25rem;
    color: var(--muted-foreground);
    outline: none;
    transition: color 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-dt-sort:hover,
  .nv-dt-sort[data-active] {
    color: var(--foreground);
  }
  .nv-dt-sort:focus-visible {
    box-shadow: 0 0 0 3px color-mix(in oklch, var(--ring) 45%, transparent);
  }

  /* Rows: hairline divider + calm hover; selection tinted with the brand. */
  .nv-dt-row {
    transition: background-color 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-dt-row:hover {
    background-color: color-mix(in oklch, var(--muted) 45%, transparent);
  }
  .nv-dt-row[data-state='selected'] {
    background-color: color-mix(in oklch, var(--nv-brand) 8%, transparent);
  }
  .nv-dt-row[data-state='selected']:hover {
    background-color: color-mix(in oklch, var(--nv-brand) 12%, transparent);
  }

  /* Contextual selection strip — a brand-tinted band signalling bulk mode. */
  .nv-dt-bulk {
    background-color: color-mix(in oklch, var(--nv-brand) 7%, var(--card));
  }
  .nv-dt-bulk-wrap {
    display: grid;
    grid-template-rows: 1fr;
  }
  .nv-dt-bulk-inner {
    overflow: hidden;
    min-height: 0;
  }
  .nv-dt-bulk-enter-active,
  .nv-dt-bulk-leave-active {
    transition:
      opacity 0.2s var(--nv-ease-out-quart, ease-out),
      grid-template-rows 0.2s var(--nv-ease-out-quart, ease-out);
  }
  .nv-dt-bulk-enter-from,
  .nv-dt-bulk-leave-to {
    opacity: 0;
    grid-template-rows: 0fr;
  }

  .nv-dt-clear {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border-radius: 9999px;
    color: var(--muted-foreground);
    transition:
      color 0.15s ease,
      background-color 0.15s ease;
  }
  .nv-dt-clear:hover {
    color: var(--foreground);
    background-color: var(--muted);
  }

  /* Count pip on the filter button. */
  .nv-dt-count {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 1.125rem;
    height: 1.125rem;
    padding-inline: 0.25rem;
    border-radius: 9999px;
    background-color: var(--nv-brand);
    color: var(--nv-brand-foreground);
    font-size: 0.6875rem;
    font-weight: 600;
    font-variant-numeric: tabular-nums;
  }

  .nv-dt-opt {
    transition: background-color 0.12s ease;
  }
  .nv-dt-opt:hover {
    background-color: var(--muted);
  }

  @media (prefers-reduced-motion: reduce) {
    .nv-dt-row,
    .nv-dt-sort,
    .nv-dt-bulk-enter-active,
    .nv-dt-bulk-leave-active {
      transition: none;
    }
  }
}
</style>
