<script setup lang="ts" generic="T extends object = Record<string, unknown>">
import type { HTMLAttributes } from 'vue'
import type {
  ColumnDef,
  SortingFn,
  SortingState,
  Updater,
} from '@tanstack/vue-table'
import { computed } from 'vue'
import {
  getCoreRowModel,
  getSortedRowModel,
  useVueTable,
} from '@tanstack/vue-table'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon } from 'lucide-vue-next'
import {
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '../../ui/table'
import { Button } from '../../ui/button'
import { Skeleton } from '../../ui/skeleton'
import { cn } from '../../../lib/utils'
import type { DataTableAlign, DataTableColumn, DataTableSort } from './types'

const props = withDefaults(
  defineProps<{
    columns: DataTableColumn<T>[]
    rows: T[]
    /** Row key field name or a function returning a stable key. */
    rowKey: string | ((row: T) => string | number)
    loading?: boolean
    emptyMessage?: string
    skeletonRows?: number
    /** Current sort (v-model:sort). */
    sort?: DataTableSort | null
    /** Sort rows in-component from the sort state. Set false to sort in the parent. */
    clientSort?: boolean
    class?: HTMLAttributes['class']
  }>(),
  {
    loading: false,
    emptyMessage: '暂无数据',
    skeletonRows: 5,
    sort: null,
    clientSort: true,
  },
)

const emit = defineEmits<{ 'update:sort': [value: DataTableSort | null] }>()

const alignClass: Record<DataTableAlign, string> = {
  start: 'text-left',
  center: 'text-center',
  end: 'text-right',
}

function keyOf(row: T): string | number {
  if (typeof props.rowKey === 'function') return props.rowKey(row)
  return (row as Record<string, unknown>)[props.rowKey] as string | number
}

function valueOf(row: T, column: DataTableColumn<T>): unknown {
  return column.accessor ? column.accessor(row) : (row as Record<string, unknown>)[column.key]
}

// A raw CSS dimension (e.g. '120px', '12rem', '50%') → inline style; anything else
// (e.g. 'w-40', 'min-w-32', 'sm:w-40') is treated as a Tailwind width class.
function isCssDimension(width?: string): boolean {
  return !!width && /^\d+(\.\d+)?(px|rem|em|%|vh|vw|ch)$/.test(width)
}
function widthStyle(width?: string) {
  return isCssDimension(width) ? { width } : undefined
}
function widthClass(width?: string) {
  return width && !isCssDimension(width) ? width : undefined
}

// Number-aware comparator with Chinese-locale string collation — preserves the
// previous DataTable sort semantics under TanStack's sorting model.
const zhSortingFn: SortingFn<T> = (rowA, rowB, columnId) => {
  const av = rowA.getValue(columnId)
  const bv = rowB.getValue(columnId)
  if (av == null && bv == null) return 0
  if (av == null) return -1
  if (bv == null) return 1
  if (typeof av === 'number' && typeof bv === 'number') return av - bv
  return String(av).localeCompare(String(bv), 'zh-Hans-CN')
}

const columnByKey = computed(() => new Map(props.columns.map((c) => [c.key, c])))
function colOf(key: string): DataTableColumn<T> {
  return columnByKey.value.get(key) as DataTableColumn<T>
}

const tableColumns = computed<ColumnDef<T>[]>(() =>
  props.columns.map((col) => ({
    id: col.key,
    accessorFn: (row: T) => valueOf(row, col),
    header: col.header,
    enableSorting: !!col.sortable,
    sortingFn: zhSortingFn,
  })),
)

// Controlled sorting: state derives from the `sort` prop; changes are emitted up.
const sortingState = computed<SortingState>(() =>
  props.sort ? [{ id: props.sort.key, desc: props.sort.direction === 'desc' }] : [],
)

function handleSortingChange(updater: Updater<SortingState>) {
  const next = typeof updater === 'function' ? updater(sortingState.value) : updater
  const first = next[0]
  emit('update:sort', first ? { key: first.id, direction: first.desc ? 'desc' : 'asc' } : null)
}

const table = useVueTable({
  get data() {
    return props.rows
  },
  get columns() {
    return tableColumns.value
  },
  state: {
    get sorting() {
      return sortingState.value
    },
  },
  // When clientSort is false the parent owns ordering; TanStack only tracks state.
  get manualSorting() {
    return !props.clientSort
  },
  enableSortingRemoval: true,
  onSortingChange: handleSortingChange,
  getRowId: (row) => String(keyOf(row)),
  getCoreRowModel: getCoreRowModel(),
  getSortedRowModel: getSortedRowModel(),
})

function sortIcon(state: false | 'asc' | 'desc') {
  if (!state) return ArrowUpDownIcon
  return state === 'asc' ? ArrowUpIcon : ArrowDownIcon
}
</script>

<template>
  <div :class="cn('overflow-hidden rounded-lg border bg-card', props.class)">
    <div class="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow v-for="headerGroup in table.getHeaderGroups()" :key="headerGroup.id">
            <TableHead
              v-for="header in headerGroup.headers"
              :key="header.id"
              :class="cn(
                alignClass[colOf(header.column.id).align ?? 'start'],
                widthClass(colOf(header.column.id).width),
                colOf(header.column.id).headerClass,
              )"
              :style="widthStyle(colOf(header.column.id).width)"
            >
              <Button
                v-if="header.column.getCanSort()"
                type="button"
                variant="ghost"
                size="sm"
                class="-ml-3 h-8 data-[align=end]:-mr-3 data-[align=end]:ml-0"
                :data-align="colOf(header.column.id).align ?? 'start'"
                @click="header.column.getToggleSortingHandler()?.($event)"
              >
                {{ colOf(header.column.id).header }}
                <component :is="sortIcon(header.column.getIsSorted())" class="size-4" data-icon="inline-end" aria-hidden="true" />
              </Button>
              <template v-else>{{ colOf(header.column.id).header }}</template>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <template v-if="loading">
            <TableRow v-for="n in skeletonRows" :key="`sk-${n}`">
              <TableCell
                v-for="column in columns"
                :key="column.key"
                :class="alignClass[column.align ?? 'start']"
              >
                <Skeleton class="h-5 w-full max-w-32" :class="column.align === 'end' ? 'ml-auto' : ''" />
              </TableCell>
            </TableRow>
          </template>

          <template v-else-if="table.getRowModel().rows.length">
            <TableRow v-for="row in table.getRowModel().rows" :key="row.id">
              <TableCell
                v-for="cell in row.getVisibleCells()"
                :key="cell.id"
                :class="cn(alignClass[colOf(cell.column.id).align ?? 'start'], colOf(cell.column.id).cellClass)"
              >
                <slot
                  :name="`cell-${cell.column.id}`"
                  :row="row.original"
                  :value="cell.getValue()"
                  :column="colOf(cell.column.id)"
                >
                  {{ cell.getValue() }}
                </slot>
              </TableCell>
            </TableRow>
          </template>

          <TableEmpty v-else :colspan="columns.length">
            <slot name="empty">{{ emptyMessage }}</slot>
          </TableEmpty>
        </TableBody>
      </Table>
    </div>
  </div>
</template>
