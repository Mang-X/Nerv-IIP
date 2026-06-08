<script setup lang="ts">
import type { BusinessConsoleErpRequestForQuotationItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useBusinessErp, useErpRequestsForQuotation } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Toolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref, shallowRef, watch } from 'vue'

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

// 采购漏斗：询价（RFQ）→ 采购订单 → 收货齐套
const { filters, purchaseOrders, purchaseOrdersPending, purchaseOrdersTotal, refreshPurchaseOrders } = useBusinessErp()
const keyword = ref('')
const statusFilter = ref('all')
const { page, pageSize } = usePagedList(filters, { resetOn: [keyword, statusFilter] })

const rfqs = useErpRequestsForQuotation()
const rfqsPaged = usePagedList(rfqs.filters, { resetOn: [() => rfqs.filters.status, () => rfqs.filters.keyword] })

const activeTab = shallowRef<'orders' | 'rfqs'>('orders')

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
const pendingArrivalCount = computed(() => rows.value.filter((r) => r.receiptReadiness === 'awaiting-arrival').length)
const partialCount = computed(() => rows.value.filter((r) => r.receiptReadiness === 'partially-received').length)
const openQuantity = computed(() => rows.value.reduce((total, r) => total + r.openQuantity, 0))

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
}, { immediate: true })
watchDebounced(keyword, (value) => {
  filters.keyword = value.trim() || undefined
}, { debounce: 300, maxWait: 1000 })

const rfqsError = computed(() => formatError(rfqs.error.value))

function refreshActive() {
  if (activeTab.value === 'orders') void refreshPurchaseOrders()
  else void rfqs.refresh()
}

const poColumns: DataTableColumn<ProcurementRow>[] = [
  { key: 'purchaseOrderNo', header: '采购单', cellClass: 'font-medium' },
  { key: 'supplierCode', header: '供应商' },
  { key: 'skuCode', header: '物料' },
  { key: 'orderedQuantity', header: '订单数量', align: 'end', accessor: (r) => formatQuantity(r.orderedQuantity) },
  { key: 'receivedQuantity', header: '已收数量', align: 'end', accessor: (r) => formatQuantity(r.receivedQuantity) },
  { key: 'openQuantity', header: '未到数量', align: 'end', accessor: (r) => formatQuantity(r.openQuantity) },
  { key: 'promisedDate', header: '预计到货', accessor: (r) => r.promisedDate || '无' },
  { key: 'status', header: '订单状态', width: 'w-24' },
  { key: 'receiptReadiness', header: '供应状态', width: 'w-24' },
  { key: 'amount', header: '金额', align: 'end', accessor: (r) => formatAmount(r.amount) },
]
const rfqColumns: DataTableColumn<BusinessConsoleErpRequestForQuotationItem>[] = [
  { key: 'rfqNo', header: '询价单号', cellClass: 'font-medium', accessor: (r) => r.rfqNo ?? '无' },
  { key: 'supplierCodes', header: '供应商', accessor: (r) => (r.supplierCodes ?? []).join('、') || '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'createdAtUtc', header: '创建时间', width: 'w-32', accessor: (r) => formatDate(r.createdAtUtc) },
]

function poRowKey(row: ProcurementRow) {
  return `${row.purchaseOrderNo}-${row.lineNo}`
}
function rfqRowKey(row: BusinessConsoleErpRequestForQuotationItem) {
  return row.rfqNo ?? '询价单'
}
function readinessLabel(value?: string) {
  const labels: Record<string, string> = {
    'awaiting-arrival': '待到货',
    'partially-received': '部分收货',
    received: '已收货',
    'no-lines': '无明细',
  }
  return value ? labels[value] ?? value : '无'
}
function statusLabel(value?: string) {
  const labels: Record<string, string> = {
    Released: '已下达', Closed: '已关闭', Cancelled: '已取消',
    released: '已下达', closed: '已关闭', cancelled: '已取消',
  }
  return value ? labels[value] ?? value : '无'
}
function formatQuantity(value: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value)
}
function formatAmount(value: number) {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', maximumFractionDigits: 0 }).format(value)
}
function formatDate(value?: string) {
  if (!value) return '无'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '无' : d.toLocaleDateString('zh-CN')
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="采购与供应" :breadcrumbs="[{ label: '经营管理' }]" :count="`${purchaseOrdersTotal} 张采购订单`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="purchaseOrdersPending" @click="refreshActive">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="本页待到货明细" :value="pendingArrivalCount" hint="awaiting" />
      <SectionCard description="本页部分收货" :value="partialCount" hint="partial" />
      <SectionCard description="本页未到数量" :value="formatQuantity(openQuantity)" hint="未交付合计" />
    </SectionCards>

    <Tabs v-model="activeTab">
      <TabsList>
        <TabsTrigger value="orders">采购订单</TabsTrigger>
        <TabsTrigger value="rfqs">询价单</TabsTrigger>
      </TabsList>

      <TabsContent value="orders" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Select v-model="statusFilter">
              <SelectTrigger class="h-9 w-32" aria-label="采购订单状态">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="Released">已下达</SelectItem>
                <SelectItem value="Closed">已关闭</SelectItem>
                <SelectItem value="Cancelled">已取消</SelectItem>
              </SelectContent>
            </Select>
            <Input v-model="keyword" class="h-9 w-56" placeholder="采购单、供应商、物料、工厂" aria-label="采购关键字" />
          </template>
        </Toolbar>
        <DataTable
          :columns="poColumns"
          :rows="rows"
          :row-key="poRowKey"
          :loading="purchaseOrdersPending"
          empty-message="未找到采购供应明细。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="statusLabel(row.status)" /></template>
          <template #cell-receiptReadiness="{ row }"><StatusBadge :value="readinessLabel(row.receiptReadiness)" /></template>
        </DataTable>
        <p class="text-xs text-muted-foreground">按采购订单分页，本页 {{ purchaseOrders.length }} 张订单展开为 {{ rows.length }} 条明细。</p>
        <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="purchaseOrdersTotal" />
      </TabsContent>

      <TabsContent value="rfqs" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="rfqs.filters.keyword" class="h-9 w-44" placeholder="询价单号/供应商（可选）" aria-label="询价关键字" />
            <Input v-model="rfqs.filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="询价状态" />
          </template>
        </Toolbar>
        <p v-if="rfqsError" class="text-sm text-destructive" role="alert">{{ rfqsError }}</p>
        <DataTable
          :columns="rfqColumns"
          :rows="rfqs.items.value"
          :row-key="rfqRowKey"
          :loading="rfqs.pending.value"
          empty-message="暂无询价单。向供应商发起询价后会出现在这里。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="rfqsPaged.page.value" v-model:page-size="rfqsPaged.pageSize.value" :total-items="rfqs.total.value" />
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
