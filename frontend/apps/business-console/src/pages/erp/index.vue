<script setup lang="ts">
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import BusinessTablePagination from '@/components/business/BusinessTablePagination.vue'
import { demoErpFinance, demoErpProcurement, demoErpSalesOrders } from '@/data/shockAbsorberDemo'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { ArrowDownIcon, ArrowUpDownIcon, ArrowUpIcon } from 'lucide-vue-next'
import { computed, reactive, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: 'ERP 业务协同' } })

type ErpRow = {
  area: string
  documentNo: string
  partner: string
  subject: string
  quantity?: number
  amount?: number
  dueDate?: string
  status: string
  owner?: string
}
type SortColumn = 'area' | 'documentNo' | 'partner' | 'subject' | 'quantity' | 'amount' | 'dueDate' | 'status'

const filterDraft = reactive({ area: 'all', keyword: '' })
const appliedFilter = reactive({ area: 'all', keyword: '' })
const tableState = reactive({
  page: 1,
  pageSize: '10',
  sortBy: 'documentNo' as SortColumn,
  sortDirection: 'asc' as 'asc' | 'desc',
})
const rows = computed<ErpRow[]>(() => [
  ...demoErpSalesOrders.map((row) => ({
    area: '销售',
    documentNo: row.documentNo,
    partner: row.customer,
    subject: row.sku,
    quantity: row.quantity,
    dueDate: row.dueDate,
    status: row.status,
  })),
  ...demoErpProcurement.map((row) => ({
    area: '采购',
    documentNo: row.documentNo,
    partner: row.supplier,
    subject: row.material,
    quantity: row.quantity,
    dueDate: row.dueDate,
    status: row.status,
  })),
  ...demoErpFinance.map((row) => ({
    area: '财务',
    documentNo: row.documentNo,
    partner: row.owner,
    subject: row.source,
    amount: row.amount,
    status: row.status,
    owner: row.owner,
  })),
])
const filteredRows = computed(() => {
  const keyword = appliedFilter.keyword.trim().toLowerCase()
  return rows.value.filter((row) => {
    const areaMatched = appliedFilter.area === 'all' || row.area === appliedFilter.area
    const keywordMatched =
      !keyword ||
      [row.area, row.documentNo, row.partner, row.subject, row.status, row.owner]
        .some((value) => (value ?? '').toLowerCase().includes(keyword))
    return areaMatched && keywordMatched
  })
})
const pageSizeNumber = computed(() => Number(tableState.pageSize) || 10)
const sortedRows = computed(() => {
  const direction = tableState.sortDirection === 'asc' ? 1 : -1

  return [...filteredRows.value].sort((left, right) => {
    const leftValue = sortValue(left, tableState.sortBy)
    const rightValue = sortValue(right, tableState.sortBy)

    if (typeof leftValue === 'number' && typeof rightValue === 'number') {
      return (leftValue - rightValue) * direction
    }

    return String(leftValue).localeCompare(String(rightValue), 'zh-Hans-CN') * direction
  })
})
const pagedRows = computed(() => {
  const start = (tableState.page - 1) * pageSizeNumber.value
  return sortedRows.value.slice(start, start + pageSizeNumber.value)
})

watch(
  () => [appliedFilter.area, appliedFilter.keyword, tableState.pageSize],
  () => {
    tableState.page = 1
  },
)

function applyFilters() {
  appliedFilter.area = filterDraft.area
  appliedFilter.keyword = filterDraft.keyword
}

function clearFilters() {
  filterDraft.area = 'all'
  filterDraft.keyword = ''
  applyFilters()
}

function setSort(column: SortColumn) {
  if (tableState.sortBy === column) {
    tableState.sortDirection = tableState.sortDirection === 'asc' ? 'desc' : 'asc'
    return
  }
  tableState.sortBy = column
  tableState.sortDirection = 'asc'
}

function sortIcon(column: SortColumn) {
  if (tableState.sortBy !== column) return ArrowUpDownIcon
  return tableState.sortDirection === 'asc' ? ArrowUpIcon : ArrowDownIcon
}

function sortValue(row: ErpRow, column: SortColumn) {
  return row[column] ?? ''
}

function formatAmount(value?: number) {
  if (value === undefined) return '无'
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', maximumFractionDigits: 0 }).format(value)
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="ERP"
        title="ERP 业务协同"
        summary="查看销售订单、采购跟进、应收应付和成本归集进度，协调客户需求、供应到货与生产计划。"
      />

      <div class="grid gap-3 md:grid-cols-3">
        <div class="rounded-lg border bg-background p-4">
          <p class="text-sm text-muted-foreground">销售订单</p>
          <p class="mt-2 text-2xl font-semibold tabular-nums">{{ demoErpSalesOrders.length }}</p>
        </div>
        <div class="rounded-lg border bg-background p-4">
          <p class="text-sm text-muted-foreground">采购跟进</p>
          <p class="mt-2 text-2xl font-semibold tabular-nums">{{ demoErpProcurement.length }}</p>
        </div>
        <div class="rounded-lg border bg-background p-4">
          <p class="text-sm text-muted-foreground">财务候选</p>
          <p class="mt-2 text-2xl font-semibold tabular-nums">{{ demoErpFinance.length }}</p>
        </div>
      </div>

      <div class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">查询条件</h2>
        </div>
        <div class="p-4">
          <FieldGroup class="grid gap-3 md:grid-cols-[220px_minmax(0,1fr)_auto]">
            <Field>
              <FieldLabel for="erp-area">业务范围</FieldLabel>
              <Select v-model="filterDraft.area">
                <SelectTrigger id="erp-area">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">全部</SelectItem>
                  <SelectItem value="销售">销售</SelectItem>
                  <SelectItem value="采购">采购</SelectItem>
                  <SelectItem value="财务">财务</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="erp-keyword">关键字</FieldLabel>
              <Input id="erp-keyword" v-model="filterDraft.keyword" placeholder="单号、客户、供应商、物料" @keydown.enter="applyFilters" />
            </Field>
            <div class="flex items-end gap-2">
              <Button type="button" @click="applyFilters">查询</Button>
              <Button type="button" variant="outline" @click="clearFilters">清空</Button>
            </div>
          </FieldGroup>
        </div>
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="flex items-center justify-between border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">业务单据</h2>
          <span class="text-sm text-muted-foreground">销售 / 采购 / 财务</span>
        </div>
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('area')">
                    范围
                    <component :is="sortIcon('area')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('documentNo')">
                    单号
                    <component :is="sortIcon('documentNo')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('partner')">
                    对象
                    <component :is="sortIcon('partner')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('subject')">
                    物料/来源
                    <component :is="sortIcon('subject')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead class="text-right">
                  <Button class="-mr-3" size="sm" type="button" variant="ghost" @click="setSort('quantity')">
                    数量
                    <component :is="sortIcon('quantity')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead class="text-right">
                  <Button class="-mr-3" size="sm" type="button" variant="ghost" @click="setSort('amount')">
                    金额
                    <component :is="sortIcon('amount')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('dueDate')">
                    日期
                    <component :is="sortIcon('dueDate')" data-icon="inline-end" />
                  </Button>
                </TableHead>
                <TableHead>
                  <Button class="-ml-3" size="sm" type="button" variant="ghost" @click="setSort('status')">
                    状态
                    <component :is="sortIcon('status')" data-icon="inline-end" />
                  </Button>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in pagedRows" :key="`${row.area}-${row.documentNo}`">
                <TableCell>{{ row.area }}</TableCell>
                <TableCell class="font-medium">{{ row.documentNo }}</TableCell>
                <TableCell>{{ row.partner }}</TableCell>
                <TableCell>{{ row.subject }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ row.quantity ?? '无' }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatAmount(row.amount) }}</TableCell>
                <TableCell>{{ row.dueDate ?? '无' }}</TableCell>
                <TableCell><Badge variant="secondary">{{ row.status }}</Badge></TableCell>
              </TableRow>
              <TableEmpty v-if="!filteredRows.length" :colspan="8">未找到单据。</TableEmpty>
            </TableBody>
          </Table>
        </div>
        <div class="border-t px-4 py-3">
          <BusinessTablePagination
            v-model:page="tableState.page"
            v-model:page-size="tableState.pageSize"
            :total-items="sortedRows.length"
          />
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
