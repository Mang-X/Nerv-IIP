<script setup lang="ts">
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useBusinessErp } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  DataTablePagination,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '采购与供应' } })

type ProcurementRow = {
  purchaseOrderNo: string
  supplierCode: string
  lineNo: string
  siteCode: string
  skuCode: string
  orderedQuantity: number
  receivedQuantity: number
  openQuantity: number
  promisedDate: string
  status: string
  receiptReadiness: string
  amount: number
}
type SortColumn = 'purchaseOrderNo' | 'supplierCode' | 'skuCode' | 'openQuantity' | 'promisedDate' | 'receiptReadiness'

const { filters, purchaseOrders, purchaseOrdersPending, purchaseOrdersTotal, refreshPurchaseOrders } = useBusinessErp()

const keyword = ref('')
const statusFilter = ref('all')
const sortState = reactive({
  sortBy: 'promisedDate' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const { page, pageSize } = usePagedList(filters, { resetOn: [keyword, statusFilter] })

const rows = computed<ProcurementRow[]>(() =>
  purchaseOrders.value.flatMap((order) =>
    (order.lines ?? []).map((line) => ({
      purchaseOrderNo: order.purchaseOrderNo ?? '',
      supplierCode: order.supplierCode ?? '',
      lineNo: line.lineNo ?? '',
      siteCode: order.siteCode ?? '',
      skuCode: line.skuCode ?? '',
      orderedQuantity: Number(line.orderedQuantity),
      receivedQuantity: Number(line.receivedQuantity),
      openQuantity: Math.max(Number(line.orderedQuantity) - Number(line.receivedQuantity), 0),
      promisedDate: String(line.promisedDate ?? ''),
      status: order.status ?? '',
      receiptReadiness: order.receiptReadiness ?? '',
      amount: Number(line.orderedQuantity) * Number(line.unitPrice),
    })),
  ),
)
const sortedRows = computed(() => {
  const direction = sortState.sortDirection === 'asc' ? 1 : -1

  return [...rows.value].sort((left, right) => {
    const leftValue = sortValue(left, sortState.sortBy)
    const rightValue = sortValue(right, sortState.sortBy)

    if (typeof leftValue === 'number' && typeof rightValue === 'number') {
      return (leftValue - rightValue) * direction
    }

    return String(leftValue).localeCompare(String(rightValue), 'zh-Hans-CN') * direction
  })
})
const pagedRows = computed(() => sortedRows.value)
const pendingArrivalCount = computed(() =>
  rows.value.filter((row) => row.receiptReadiness === 'awaiting-arrival').length,
)
const inspectionCount = computed(() =>
  rows.value.filter((row) => row.receiptReadiness === 'partially-received').length,
)
const openQuantity = computed(() =>
  rows.value.reduce((total, row) => total + row.openQuantity, 0),
)

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
}, { immediate: true })

watchDebounced(keyword, (value) => {
  filters.keyword = value.trim() || undefined
}, { debounce: 300, maxWait: 1000 })

function clearFilters() {
  statusFilter.value = 'all'
  keyword.value = ''
  filters.keyword = undefined
}

function setSort(column: SortColumn) {
  if (sortState.sortBy === column) {
    sortState.sortDirection = sortState.sortDirection === 'asc' ? 'desc' : 'asc'
    return
  }
  sortState.sortBy = column
  sortState.sortDirection = 'asc'
}

function sortIcon(column: SortColumn) {
  if (sortState.sortBy !== column) return ArrowUpDownIcon
  return sortState.sortDirection === 'asc' ? ArrowUpIcon : ArrowDownIcon
}

function sortValue(row: ProcurementRow, column: SortColumn) {
  return row[column] ?? ''
}

function readinessLabel(value: string) {
  const labels: Record<string, string> = {
    'awaiting-arrival': '待到货',
    'partially-received': '部分收货',
    received: '已收货',
    'no-lines': '无明细',
  }
  return labels[value] ?? value
}

function formatQuantity(value: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value)
}

function formatAmount(value: number) {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', maximumFractionDigits: 0 }).format(value)
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="经营管理"
        title="采购与供应"
        summary="跟进外购件采购订单、供应商承诺、预计到货和部分收货状态，支撑生产齐套判断。"
      />

      <div class="grid gap-3 md:grid-cols-3">
        <div class="rounded-lg border bg-background p-4">
          <p class="text-sm text-muted-foreground">待到货明细</p>
          <p class="mt-2 text-2xl font-semibold tabular-nums">{{ pendingArrivalCount }}</p>
        </div>
        <div class="rounded-lg border bg-background p-4">
          <p class="text-sm text-muted-foreground">部分收货</p>
          <p class="mt-2 text-2xl font-semibold tabular-nums">{{ inspectionCount }}</p>
        </div>
        <div class="rounded-lg border bg-background p-4">
          <p class="text-sm text-muted-foreground">未到数量</p>
          <p class="mt-2 text-2xl font-semibold tabular-nums">{{ formatQuantity(openQuantity) }}</p>
        </div>
      </div>

      <div class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">查询条件</h2>
        </div>
        <div class="p-4">
          <FieldGroup class="grid gap-3 md:grid-cols-[220px_minmax(0,1fr)_auto]">
            <Field>
              <FieldLabel for="erp-status">订单状态</FieldLabel>
              <Select v-model="statusFilter">
                <SelectTrigger id="erp-status">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部</SelectItem>
                  <SelectItem value="Released">已下达</SelectItem>
                  <SelectItem value="Closed">已关闭</SelectItem>
                  <SelectItem value="Cancelled">已取消</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="erp-keyword">关键字</FieldLabel>
              <Input id="erp-keyword" v-model="keyword" placeholder="采购单、供应商、物料、工厂" />
            </Field>
            <div class="flex items-end gap-2">
              <Button type="button" variant="outline" @click="clearFilters">清空</Button>
            </div>
          </FieldGroup>
        </div>
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">采购订单供应明细</h2>
          <Button size="sm" type="button" variant="outline" :disabled="purchaseOrdersPending" @click="refreshPurchaseOrders">
            <Spinner v-if="purchaseOrdersPending" data-icon="inline-start" />
            <RefreshCwIcon v-else data-icon="inline-start" />
            刷新
          </Button>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('purchaseOrderNo')">
                    采购单
                    <component :is="sortIcon('purchaseOrderNo')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('supplierCode')">
                    供应商
                    <component :is="sortIcon('supplierCode')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('skuCode')">
                    物料
                    <component :is="sortIcon('skuCode')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead class="text-right">订单数量</TableHead>
                <TableHead class="text-right">已收数量</TableHead>
                <TableHead class="text-right">
                  <Button class="-mr-3" size="sm" type="button" variant="ghost" @click="setSort('openQuantity')">
                    未到数量
                    <component :is="sortIcon('openQuantity')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('promisedDate')">
                    预计到货
                    <component :is="sortIcon('promisedDate')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('receiptReadiness')">
                    供应状态
                    <component :is="sortIcon('receiptReadiness')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead class="text-right">金额</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in pagedRows" :key="`${row.purchaseOrderNo}-${row.lineNo}`">
                <TableCell class="font-medium">{{ row.purchaseOrderNo }}</TableCell>
                <TableCell>
                  <div class="grid gap-1">
                    <span>{{ row.supplierCode }}</span>
                  </div>
                </TableCell>
                <TableCell>
                  <div class="grid gap-1">
                    <span>{{ row.skuCode }}</span>
                    <span class="text-xs text-muted-foreground">{{ row.siteCode }}</span>
                  </div>
                </TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.orderedQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.receivedQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.openQuantity) }}</TableCell>
                <TableCell>{{ row.promisedDate }}</TableCell>
                <TableCell><Badge variant="secondary">{{ readinessLabel(row.receiptReadiness) }}</Badge></TableCell>
                <TableCell class="text-right tabular-nums">{{ formatAmount(row.amount) }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!sortedRows.length" :colspan="9">未找到采购供应明细。</TableEmpty>
            </TableBody>
          </Table>
        </div>
        <div class="border-t px-4 py-3">
          <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="purchaseOrdersTotal" />
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
