<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useBusinessErp } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useRoute } from 'vue-router'
import {
  ButtonPro,
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
import { computed, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '采购与供应', requiredPermissions: ['business.erp.procurement.read'] } })

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

interface PurchaseRequisitionRow {
  requisitionNo: string
  suggestionId: string
  skuCode: string
  uomCode: string
  siteCode: string
  quantity: number
  requiredDate: string
  status: string
}

const route = useRoute()
const {
  filters,
  purchaseRequisitions,
  purchaseRequisitionsPending,
  purchaseRequisitionsTotal,
  purchaseOrders,
  purchaseOrdersPending,
  purchaseOrdersTotal,
  refreshProcurementDocuments,
} = useBusinessErp()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.purchaseRequisitionStatus, () => filters.purchaseOrderStatus, () => filters.keyword],
})

function firstQueryParam(value: unknown) {
  if (Array.isArray(value)) return value[0] ? String(value[0]) : undefined
  return value ? String(value) : undefined
}

watch(
  () => route.query.keyword,
  (keyword) => {
    filters.keyword = firstQueryParam(keyword)
  },
  { immediate: true },
)

// reka-ui SelectItem 不接受空字符串 value，用 'all' 作「全部」哨兵并映射回 undefined。
const requisitionStatusFilter = computed({
  get: () => filters.purchaseRequisitionStatus || 'all',
  set: (value: string) => { filters.purchaseRequisitionStatus = value === 'all' ? undefined : value },
})

const orderStatusFilter = computed({
  get: () => filters.purchaseOrderStatus || 'all',
  set: (value: string) => { filters.purchaseOrderStatus = value === 'all' ? undefined : value },
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

const requisitionRows = computed<PurchaseRequisitionRow[]>(() =>
  purchaseRequisitions.value.map((requisition) => ({
    requisitionNo: requisition.requisitionNo ?? '',
    suggestionId: requisition.suggestionId ?? '',
    skuCode: requisition.skuCode ?? '',
    uomCode: requisition.uomCode ?? '',
    siteCode: requisition.siteCode ?? '',
    quantity: Number(requisition.quantity),
    requiredDate: String(requisition.requiredDate ?? ''),
    status: requisition.status ?? '',
  })),
)

// 语义 KPI（可行动数）：齐套判断关心待到货 / 部分收货与未到数量，而非机械行数。
const pendingArrivalCount = computed(() => rows.value.filter((r) => r.receiptReadiness === 'awaiting-arrival').length)
const partiallyReceivedCount = computed(() => rows.value.filter((r) => r.receiptReadiness === 'partially-received').length)
const openQuantity = computed(() => rows.value.reduce((total, r) => total + r.openQuantity, 0))
const openRequisitionCount = computed(() => requisitionRows.value.filter((r) => r.status === 'Open').length)

const requisitionColumns: DataTableProColumn<PurchaseRequisitionRow>[] = [
  { key: 'requisitionNo', header: '采购申请', cellClass: 'font-medium' },
  { key: 'skuCode', header: '物料' },
  { key: 'quantity', header: '申请数量', align: 'end', width: 'w-28' },
  { key: 'uomCode', header: '单位', width: 'w-20' },
  { key: 'requiredDate', header: '需求日期', width: 'w-32' },
  { key: 'siteCode', header: '工厂', width: 'w-28' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'suggestionId', header: 'MRP 建议', width: 'w-40' },
]

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
  Open: '待转单',
  Converted: '已转单',
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
function requisitionRowKey(row: PurchaseRequisitionRow) {
  return row.requisitionNo
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="采购与供应"
      :breadcrumbs="[{ label: '经营管理' }]"
      :count="`${purchaseRequisitionsTotal} 张采购申请 / ${purchaseOrdersTotal} 张采购订单`"
    >
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="purchaseRequisitionsPending || purchaseOrdersPending" @click="refreshProcurementDocuments">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="待转采购申请" :value="openRequisitionCount" hint="本页 MRP 已接受申请" />
      <SectionCard description="待到货明细" :value="pendingArrivalCount" hint="本页未到货，影响生产齐套" />
      <SectionCard description="部分收货" :value="partiallyReceivedCount" hint="本页未收齐，需跟进剩余" />
      <SectionCard description="未到数量" :value="formatQuantity(openQuantity)" hint="本页待到货总量" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="filters.keyword" class="h-9 w-64" placeholder="采购申请 / 采购单 / 供应商 / 物料 / 工厂" aria-label="采购关键字" />
        <SelectPro v-model="requisitionStatusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="申请状态"><SelectProValue placeholder="申请状态" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部申请</SelectProItem>
            <SelectProItem value="Open">待转单</SelectProItem>
            <SelectProItem value="Converted">已转单</SelectProItem>
            <SelectProItem value="Cancelled">已取消</SelectProItem>
          </SelectProContent>
        </SelectPro>
        <SelectPro v-model="orderStatusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="订单状态"><SelectProValue placeholder="订单状态" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部订单</SelectProItem>
            <SelectProItem value="Released">已下达</SelectProItem>
            <SelectProItem value="Closed">已关闭</SelectProItem>
            <SelectProItem value="Cancelled">已取消</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <DataTablePro
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="purchaseRequisitionsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="requisitionColumns"
      :rows="requisitionRows"
      :row-key="requisitionRowKey"
      :loading="purchaseRequisitionsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="未找到采购申请。接受采购类 MRP 建议后会在这里显示。"
    >
      <template #cell-quantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template>
      <template #cell-status="{ row }"><StatusBadgePro :value="statusLabel(row.status)" /></template>
    </DataTablePro>

    <DataTablePro
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="purchaseOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
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

  </BusinessLayout>
</template>
