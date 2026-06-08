<script setup lang="ts">
import type {
  BusinessConsoleErpDeliveryOrderItem,
  BusinessConsoleErpOpportunityItem,
  BusinessConsoleErpQuotationItem,
  BusinessConsoleErpSalesOrderItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import {
  useErpDeliveryOrders,
  useErpOpportunities,
  useErpQuotations,
  useErpSalesOrders,
} from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '销售管理' } })

// 销售漏斗：商机 → 报价 → 销售订单 → 发货单（同一业务域，按页内 Tabs 组织）
const orders = useErpSalesOrders()
const ordersPaged = usePagedList(orders.filters, { resetOn: [() => orders.filters.status, () => orders.filters.keyword] })

const quotations = useErpQuotations()
const quotationsPaged = usePagedList(quotations.filters, { resetOn: [() => quotations.filters.status, () => quotations.filters.keyword] })

const opportunities = useErpOpportunities()
const opportunitiesPaged = usePagedList(opportunities.filters, { resetOn: [() => opportunities.filters.status, () => opportunities.filters.keyword] })

const deliveries = useErpDeliveryOrders()
const deliveriesPaged = usePagedList(deliveries.filters, { resetOn: [() => deliveries.filters.status, () => deliveries.filters.keyword] })

const activeTab = shallowRef<'orders' | 'quotations' | 'opportunities' | 'deliveries'>('orders')

const ordersError = computed(() => formatError(orders.salesOrdersError.value ?? createSalesOrderError.value))
const quotationsError = computed(() => formatError(quotations.error.value))
const opportunitiesError = computed(() => formatError(opportunities.error.value))
const deliveriesError = computed(() => formatError(deliveries.error.value))

function refreshActive() {
  if (activeTab.value === 'orders') void orders.refreshSalesOrders()
  else if (activeTab.value === 'quotations') void quotations.refresh()
  else if (activeTab.value === 'opportunities') void opportunities.refresh()
  else void deliveries.refresh()
}

const orderColumns: DataTableColumn<BusinessConsoleErpSalesOrderItem>[] = [
  { key: 'salesOrderNo', header: '销售单号', cellClass: 'font-medium', accessor: (r) => r.salesOrderNo ?? '无' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'totalAmount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.totalAmount ?? 0 },
]
const quotationColumns: DataTableColumn<BusinessConsoleErpQuotationItem>[] = [
  { key: 'quotationNo', header: '报价单号', cellClass: 'font-medium', accessor: (r) => r.quotationNo ?? '无' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'expiresOn', header: '有效期至', width: 'w-32', accessor: (r) => formatDate(r.expiresOn) },
  { key: 'totalAmount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.totalAmount ?? 0 },
]
const opportunityColumns: DataTableColumn<BusinessConsoleErpOpportunityItem>[] = [
  { key: 'opportunityNo', header: '商机编号', cellClass: 'font-medium', accessor: (r) => r.opportunityNo ?? '无' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '无' },
  { key: 'topic', header: '主题', accessor: (r) => r.topic ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'openedAtUtc', header: '创建时间', width: 'w-32', accessor: (r) => formatDate(r.openedAtUtc) },
]
const deliveryColumns: DataTableColumn<BusinessConsoleErpDeliveryOrderItem>[] = [
  { key: 'deliveryOrderNo', header: '发货单号', cellClass: 'font-medium', accessor: (r) => r.deliveryOrderNo ?? '无' },
  { key: 'salesOrderNo', header: '关联销售单', accessor: (r) => r.salesOrderNo ?? '无' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'releasedAtUtc', header: '发货时间', width: 'w-32', accessor: (r) => formatDate(r.releasedAtUtc) },
]

const ordersAmount = computed(() => orders.salesOrders.value.reduce((sum, o) => sum + (o.totalAmount ?? 0), 0))

const { createSalesOrder, createSalesOrderPending, createSalesOrderError } = orders
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({ quotationNo: '', salesOrderNo: '' })

function openCreate() {
  createForm.quotationNo = ''
  createForm.salesOrderNo = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.quotationNo.trim()) {
    createError.value = '请输入报价单号（销售订单由已批准报价转换生成）。'
    return
  }
  try {
    await createSalesOrder({
      quotationNo: createForm.quotationNo.trim(),
      salesOrderNo: createForm.salesOrderNo.trim() || undefined,
    })
    createOpen.value = false
    toast.success('销售订单已创建')
  } catch {
    // 失败信息由页面错误区呈现。
  }
}

function formatAmount(value: number) {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', maximumFractionDigits: 2 }).format(value)
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
    <PageHeader title="销售管理" :breadcrumbs="[{ label: '经营管理' }]" :count="`${orders.salesOrdersTotal.value} 张销售订单`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" @click="refreshActive">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Button size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建销售订单
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="商机" :value="opportunities.total.value" hint="线索/跟进" />
      <SectionCard description="报价单" :value="quotations.total.value" hint="待转订单" />
      <SectionCard description="销售订单" :value="orders.salesOrdersTotal.value" hint="本页金额合计" :sub="formatAmount(ordersAmount)" />
      <SectionCard description="发货单" :value="deliveries.total.value" hint="履约出货" />
    </SectionCards>

    <Tabs v-model="activeTab">
      <TabsList>
        <TabsTrigger value="orders">销售订单</TabsTrigger>
        <TabsTrigger value="quotations">报价单</TabsTrigger>
        <TabsTrigger value="opportunities">商机</TabsTrigger>
        <TabsTrigger value="deliveries">发货单</TabsTrigger>
      </TabsList>

      <TabsContent value="orders" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="orders.filters.keyword" class="h-9 w-44" placeholder="销售单号/客户（可选）" aria-label="销售订单关键字" />
            <Input v-model="orders.filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="销售订单状态" />
          </template>
        </Toolbar>
        <p v-if="ordersError" class="text-sm text-destructive" role="alert">{{ ordersError }}</p>
        <DataTable
          :columns="orderColumns"
          :rows="orders.salesOrders.value"
          :row-key="(r: BusinessConsoleErpSalesOrderItem) => r.salesOrderNo ?? '销售订单'"
          :loading="orders.salesOrdersPending.value"
          empty-message="暂无销售订单。报价批准并转换后会出现在这里。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
          <template #cell-totalAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalAmount ?? 0) }}</span></template>
        </DataTable>
        <DataTablePagination v-model:page="ordersPaged.page.value" v-model:page-size="ordersPaged.pageSize.value" :total-items="orders.salesOrdersTotal.value" />
      </TabsContent>

      <TabsContent value="quotations" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="quotations.filters.keyword" class="h-9 w-44" placeholder="报价单号/客户（可选）" aria-label="报价单关键字" />
            <Input v-model="quotations.filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="报价单状态" />
          </template>
        </Toolbar>
        <p v-if="quotationsError" class="text-sm text-destructive" role="alert">{{ quotationsError }}</p>
        <DataTable
          :columns="quotationColumns"
          :rows="quotations.items.value"
          :row-key="(r: BusinessConsoleErpQuotationItem) => r.quotationNo ?? '报价单'"
          :loading="quotations.pending.value"
          empty-message="暂无报价单。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
          <template #cell-totalAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalAmount ?? 0) }}</span></template>
        </DataTable>
        <DataTablePagination v-model:page="quotationsPaged.page.value" v-model:page-size="quotationsPaged.pageSize.value" :total-items="quotations.total.value" />
      </TabsContent>

      <TabsContent value="opportunities" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="opportunities.filters.keyword" class="h-9 w-44" placeholder="商机编号/客户（可选）" aria-label="商机关键字" />
            <Input v-model="opportunities.filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="商机状态" />
          </template>
        </Toolbar>
        <p v-if="opportunitiesError" class="text-sm text-destructive" role="alert">{{ opportunitiesError }}</p>
        <DataTable
          :columns="opportunityColumns"
          :rows="opportunities.items.value"
          :row-key="(r: BusinessConsoleErpOpportunityItem) => r.opportunityNo ?? '商机'"
          :loading="opportunities.pending.value"
          empty-message="暂无商机。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="opportunitiesPaged.page.value" v-model:page-size="opportunitiesPaged.pageSize.value" :total-items="opportunities.total.value" />
      </TabsContent>

      <TabsContent value="deliveries" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="deliveries.filters.keyword" class="h-9 w-44" placeholder="发货单号/客户（可选）" aria-label="发货单关键字" />
            <Input v-model="deliveries.filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="发货单状态" />
          </template>
        </Toolbar>
        <p v-if="deliveriesError" class="text-sm text-destructive" role="alert">{{ deliveriesError }}</p>
        <DataTable
          :columns="deliveryColumns"
          :rows="deliveries.items.value"
          :row-key="(r: BusinessConsoleErpDeliveryOrderItem) => r.deliveryOrderNo ?? '发货单'"
          :loading="deliveries.pending.value"
          empty-message="暂无发货单。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="deliveriesPaged.page.value" v-model:page-size="deliveriesPaged.pageSize.value" :total-items="deliveries.total.value" />
      </TabsContent>
    </Tabs>

    <Dialog v-model:open="createOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>新建销售订单</DialogTitle>
          <DialogDescription>由已批准的报价转换生成销售订单，订单明细沿用报价。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <FieldGroup>
            <Field>
              <FieldLabel for="erp-so-quotation">报价单号</FieldLabel>
              <Input id="erp-so-quotation" v-model="createForm.quotationNo" autocomplete="off" />
              <FieldError v-if="createError" :errors="[createError]" />
            </Field>
            <Field>
              <FieldLabel for="erp-so-no">销售单号（可选）</FieldLabel>
              <Input id="erp-so-no" v-model="createForm.salesOrderNo" autocomplete="off" placeholder="留空由系统编号" />
            </Field>
          </FieldGroup>
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="createSalesOrderPending">创建销售订单</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
