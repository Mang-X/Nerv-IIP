<script setup lang="ts" generic="T extends object = Record<string, unknown>">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
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

const displayRows = computed(() => {
  if (!props.clientSort || !props.sort) return props.rows
  const { key, direction } = props.sort
  const column = props.columns.find((c) => c.key === key)
  if (!column) return props.rows
  const factor = direction === 'asc' ? 1 : -1
  return [...props.rows].sort((a, b) => {
    const av = valueOf(a, column)
    const bv = valueOf(b, column)
    if (av == null && bv == null) return 0
    if (av == null) return -factor
    if (bv == null) return factor
    if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * factor
    return String(av).localeCompare(String(bv), 'zh-Hans-CN') * factor
  })
})

function toggleSort(column: DataTableColumn<T>) {
  if (!column.sortable) return
  const current = props.sort
  let next: DataTableSort | null
  if (!current || current.key !== column.key) next = { key: column.key, direction: 'asc' }
  else if (current.direction === 'asc') next = { key: column.key, direction: 'desc' }
  else next = null
  emit('update:sort', next)
}

function sortIcon(column: DataTableColumn<T>) {
  if (props.sort?.key !== column.key) return ArrowUpDownIcon
  return props.sort.direction === 'asc' ? ArrowUpIcon : ArrowDownIcon
}
</script>

<template>
  <div :class="cn('overflow-hidden rounded-lg border bg-card', props.class)">
    <div class="overflow-x-auto">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead
              v-for="column in columns"
              :key="column.key"
              :class="cn(alignClass[column.align ?? 'start'], widthClass(column.width), column.headerClass)"
              :style="widthStyle(column.width)"
            >
              <Button
                v-if="column.sortable"
                type="button"
                variant="ghost"
                size="sm"
                class="-ml-3 h-8 data-[align=end]:-mr-3 data-[align=end]:ml-0"
                :data-align="column.align ?? 'start'"
                @click="toggleSort(column)"
              >
                {{ column.header }}
                <component :is="sortIcon(column)" class="size-4" data-icon="inline-end" aria-hidden="true" />
              </Button>
              <template v-else>{{ column.header }}</template>
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

          <template v-else-if="displayRows.length">
            <TableRow v-for="row in displayRows" :key="keyOf(row)">
              <TableCell
                v-for="column in columns"
                :key="column.key"
                :class="cn(alignClass[column.align ?? 'start'], column.cellClass)"
              >
                <slot :name="`cell-${column.key}`" :row="row" :value="valueOf(row, column)" :column="column">
                  {{ valueOf(row, column) }}
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
