<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useBusinessErp } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePaginationPro,
  DataTablePro,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '采购与供应' } })

interface ProcurementRow {
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

const { filters, purchaseOrders, purchaseOrdersPending, purchaseOrdersTotal, refreshPurchaseOrders } = useBusinessErp()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status, () => filters.keyword] })

// reka-ui SelectItem 不接受空字符串 value，用 'all' 作「全部」哨兵并映射回 undefined。
const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => { filters.status = value === 'all' ? undefined : value },
})

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

// 语义 KPI（可行动数）：齐套判断关心待到货 / 部分收货与未到数量，而非机械行数。
const pendingArrivalCount = computed(() => rows.value.filter((r) => r.receiptReadiness === 'awaiting-arrival').length)
const partiallyReceivedCount = computed(() => rows.value.filter((r) => r.receiptReadiness === 'partially-received').length)
const openQuantity = computed(() => rows.value.reduce((total, r) => total + r.openQuantity, 0))

const columns: DataTableProColumn<ProcurementRow>[] = [
  { key: 'purchaseOrderNo', header: '采购单', cellClass: 'font-medium' },
  { key: 'supplierCode', header: '供应商' },
  { key: 'skuCode', header: '物料' },
  { key: 'orderedQuantity', header: '订单数量', align: 'end', width: 'w-28' },
  { key: 'receivedQuantity', header: '已收数量', align: 'end', width: 'w-28' },
  { key: 'openQuantity', header: '未到数量', align: 'end', width: 'w-28' },
  { key: 'promisedDate', header: '预计到货', width: 'w-32' },
  { key: 'status', header: '订单状态', width: 'w-28' },
  { key: 'receiptReadiness', header: '供应状态', width: 'w-28' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32' },
]

const READINESS_LABELS: Record<string, string> = {
  'awaiting-arrival': '待到货',
  'partially-received': '部分收货',
  'received': '已收货',
  'no-lines': '无明细',
}
const STATUS_LABELS: Record<string, string> = {
  Released: '已下达',
  Closed: '已关闭',
  Cancelled: '已取消',
}
function readinessLabel(value: string) {
  return READINESS_LABELS[value] ?? value
}
function statusLabel(value: string) {
  return STATUS_LABELS[value] ?? value
}
function formatQuantity(value: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value)
}
function formatAmount(value: number) {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', maximumFractionDigits: 0 }).format(value)
}
function rowKey(row: ProcurementRow) {
  return `${row.purchaseOrderNo}-${row.lineNo}`
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="采购与供应" :breadcrumbs="[{ label: '经营管理' }]" :count="`${purchaseOrdersTotal} 张采购订单`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="purchaseOrdersPending" @click="refreshPurchaseOrders">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="待到货明细" :value="pendingArrivalCount" hint="本页未到货，影响生产齐套" />
      <SectionCard description="部分收货" :value="partiallyReceivedCount" hint="本页未收齐，需跟进剩余" />
      <SectionCard description="未到数量" :value="formatQuantity(openQuantity)" hint="本页待到货总量" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="filters.keyword" class="h-9 w-56" placeholder="采购单 / 供应商 / 物料 / 工厂" aria-label="采购关键字" />
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="订单状态"><SelectProValue placeholder="全部状态" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部状态</SelectProItem>
            <SelectProItem value="Released">已下达</SelectProItem>
            <SelectProItem value="Closed">已关闭</SelectProItem>
            <SelectProItem value="Cancelled">已取消</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <DataTablePro
      :columns="columns"
      :rows="rows"
      :row-key="rowKey"
      :loading="purchaseOrdersPending"
      :searchable="false"
      :column-settings="false"
      empty-message="未找到采购供应明细。下达采购订单后会在这里跟进到货与收货。"
    >
      <template #cell-orderedQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.orderedQuantity) }}</span></template>
      <template #cell-receivedQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.receivedQuantity) }}</span></template>
      <template #cell-openQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.openQuantity) }}</span></template>
      <template #cell-status="{ row }"><StatusBadgePro :value="statusLabel(row.status)" /></template>
      <template #cell-receiptReadiness="{ row }"><StatusBadgePro :value="readinessLabel(row.receiptReadiness)" /></template>
      <template #cell-amount="{ row }"><span class="tabular-nums">{{ formatAmount(row.amount) }}</span></template>
    </DataTablePro>

    <DataTablePaginationPro
      v-model:page="page"
      :page-size="pageSize"
      :total-items="purchaseOrdersTotal"
      @update:page-size="(v) => (pageSize = String(v))"
    />
  </BusinessLayout>
</template>
