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
  DropdownMenuItem,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '销售管理' } })

// 销售漏斗：商机 → 报价（含审批） → 销售订单 → 发货单（同一业务域，页内 Tabs 组织）
const opportunities = useErpOpportunities()
const opportunitiesPaged = usePagedList(opportunities.filters, {
  resetOn: [() => opportunities.filters.status, () => opportunities.filters.keyword],
})

const quotations = useErpQuotations()
const quotationsPaged = usePagedList(quotations.filters, {
  resetOn: [() => quotations.filters.status, () => quotations.filters.keyword],
})

const orders = useErpSalesOrders()
const ordersPaged = usePagedList(orders.filters, {
  resetOn: [() => orders.filters.status, () => orders.filters.keyword],
})

const deliveries = useErpDeliveryOrders()
const deliveriesPaged = usePagedList(deliveries.filters, {
  resetOn: [() => deliveries.filters.status, () => deliveries.filters.keyword],
})

const activeTab = shallowRef<'opportunities' | 'quotations' | 'orders' | 'deliveries'>('opportunities')

// reka-ui SelectItem 不接受空字符串 value，用 'all' 作「全部」哨兵并映射回 undefined。
function statusProxy(getStatus: () => string | undefined, setStatus: (value: string | undefined) => void) {
  return computed({
    get: () => getStatus() || 'all',
    set: (value: string) => setStatus(value === 'all' ? undefined : value),
  })
}
const opportunityStatus = statusProxy(() => opportunities.filters.status, (v) => { opportunities.filters.status = v })
const quotationStatus = statusProxy(() => quotations.filters.status, (v) => { quotations.filters.status = v })
const orderStatus = statusProxy(() => orders.filters.status, (v) => { orders.filters.status = v })
const deliveryStatus = statusProxy(() => deliveries.filters.status, (v) => { deliveries.filters.status = v })

// 语义 KPI（可行动数，非机械计数）：基于本页可见行的漏斗推进信号。
const PENDING_QUOTATION = new Set(['submitted', 'pending', 'pending-approval', 'awaiting-approval'])
const IN_TRANSIT_DELIVERY = new Set(['released', 'shipping', 'in-transit', 'intransit'])
const OPEN_OPPORTUNITY = new Set(['open', 'opened', 'qualifying', 'negotiating', 'in-progress'])

const pendingApprovalQuotations = computed(() =>
  quotations.items.value.filter((q) => PENDING_QUOTATION.has((q.status ?? '').toLowerCase())).length,
)
const inTransitDeliveries = computed(() =>
  deliveries.items.value.filter((d) => IN_TRANSIT_DELIVERY.has((d.status ?? '').toLowerCase())).length,
)
const activeOpportunities = computed(() =>
  opportunities.items.value.filter((o) => OPEN_OPPORTUNITY.has((o.status ?? '').toLowerCase())).length,
)
const ordersAmount = computed(() => orders.salesOrders.value.reduce((sum, o) => sum + (o.totalAmount ?? 0), 0))

const opportunitiesError = computed(() => formatError(opportunities.error.value ?? opportunities.openOpportunityError.value))
const quotationsError = computed(() => formatError(quotations.error.value ?? quotations.approveQuotationError.value ?? quotations.createQuotationError.value))
const ordersError = computed(() => formatError(orders.salesOrdersError.value ?? orders.createSalesOrderError.value))
const deliveriesError = computed(() => formatError(deliveries.error.value))

function refreshActive() {
  if (activeTab.value === 'opportunities') void opportunities.refresh()
  else if (activeTab.value === 'quotations') void quotations.refresh()
  else if (activeTab.value === 'orders') void orders.refreshSalesOrders()
  else void deliveries.refresh()
}

const opportunityColumns: DataTableColumn<BusinessConsoleErpOpportunityItem>[] = [
  { key: 'opportunityNo', header: '商机编号', cellClass: 'font-medium', accessor: (r) => r.opportunityNo ?? '—' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '—' },
  { key: 'topic', header: '主题', accessor: (r) => r.topic ?? '—' },
  { key: 'status', header: '阶段', width: 'w-28' },
  { key: 'openedAtUtc', header: '创建时间', width: 'w-40', accessor: (r) => formatDateTime(r.openedAtUtc) },
]
const quotationColumns: DataTableColumn<BusinessConsoleErpQuotationItem>[] = [
  { key: 'quotationNo', header: '报价单号', cellClass: 'font-medium', accessor: (r) => r.quotationNo ?? '—' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '—' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'expiresOn', header: '有效期至', width: 'w-32', accessor: (r) => formatDate(r.expiresOn) },
  { key: 'totalAmount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.totalAmount ?? 0 },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]
const orderColumns: DataTableColumn<BusinessConsoleErpSalesOrderItem>[] = [
  { key: 'salesOrderNo', header: '销售单号', cellClass: 'font-medium', accessor: (r) => r.salesOrderNo ?? '—' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '—' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'totalAmount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.totalAmount ?? 0 },
]
const deliveryColumns: DataTableColumn<BusinessConsoleErpDeliveryOrderItem>[] = [
  { key: 'deliveryOrderNo', header: '发货单号', cellClass: 'font-medium', accessor: (r) => r.deliveryOrderNo ?? '—' },
  { key: 'salesOrderNo', header: '关联销售单', accessor: (r) => r.salesOrderNo ?? '—' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '—' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'releasedAtUtc', header: '发货时间', width: 'w-40', accessor: (r) => formatDateTime(r.releasedAtUtc) },
]

// ---- 开立商机 ----
const opportunityOpen = shallowRef(false)
const opportunityForm = reactive({ customerCode: '', topic: '' })
const opportunityFormError = shallowRef('')
function openOpportunityDialog() {
  opportunityForm.customerCode = ''
  opportunityForm.topic = ''
  opportunityFormError.value = ''
  opportunityOpen.value = true
}
async function submitOpportunity() {
  if (!opportunityForm.customerCode.trim() || !opportunityForm.topic.trim()) {
    opportunityFormError.value = '请填写客户与商机主题。'
    return
  }
  try {
    await opportunities.openOpportunity({
      customerCode: opportunityForm.customerCode.trim(),
      topic: opportunityForm.topic.trim(),
    })
    opportunityOpen.value = false
    toast.success('商机已开立')
  } catch {
    opportunityFormError.value = formatError(opportunities.openOpportunityError.value) || '开立失败，请稍后重试。'
  }
}

// ---- 创建报价（单明细行，覆盖最常见报价场景）----
const quotationOpen = shallowRef(false)
const quotationForm = reactive({
  customerCode: '',
  expiresOn: '',
  skuCode: '',
  quantity: '1',
  unitPrice: '0',
  requiredDate: '',
})
const quotationFormError = shallowRef('')
function openQuotationDialog() {
  quotationForm.customerCode = ''
  quotationForm.expiresOn = ''
  quotationForm.skuCode = ''
  quotationForm.quantity = '1'
  quotationForm.unitPrice = '0'
  quotationForm.requiredDate = ''
  quotationFormError.value = ''
  quotationOpen.value = true
}
async function submitQuotation() {
  const quantity = Number(quotationForm.quantity)
  const unitPrice = Number(quotationForm.unitPrice)
  if (!quotationForm.customerCode.trim() || !quotationForm.expiresOn || !quotationForm.skuCode.trim() || !quotationForm.requiredDate) {
    quotationFormError.value = '请填写客户、有效期、物料与需求日期。'
    return
  }
  if (!(quantity > 0) || !(unitPrice >= 0)) {
    quotationFormError.value = '数量需为正数、单价不可为负。'
    return
  }
  try {
    await quotations.createQuotation({
      customerCode: quotationForm.customerCode.trim(),
      expiresOn: quotationForm.expiresOn,
      lines: [{
        lineNo: '10',
        skuCode: quotationForm.skuCode.trim(),
        uomCode: 'EA',
        quantity,
        unitPrice,
        requiredDate: quotationForm.requiredDate,
      }],
    })
    quotationOpen.value = false
    toast.success('报价单已创建')
  } catch {
    quotationFormError.value = formatError(quotations.createQuotationError.value) || '创建失败，请稍后重试。'
  }
}

async function approveQuotation(row: BusinessConsoleErpQuotationItem) {
  if (!row.quotationNo) return
  try {
    await quotations.approveQuotation(row.quotationNo)
    toast.success(`报价单 ${row.quotationNo} 已审批`)
  } catch {
    toast.error(formatError(quotations.approveQuotationError.value) || '审批失败，请稍后重试。')
  }
}

// ---- 新建销售订单（由已批准报价转换）----
const orderOpen = shallowRef(false)
const orderForm = reactive({ quotationNo: '', salesOrderNo: '' })
const orderFormError = shallowRef('')
function openOrderDialog() {
  orderForm.quotationNo = ''
  orderForm.salesOrderNo = ''
  orderFormError.value = ''
  orderOpen.value = true
}
async function submitOrder() {
  if (!orderForm.quotationNo.trim()) {
    orderFormError.value = '请输入报价单号（销售订单由已批准报价转换生成）。'
    return
  }
  try {
    await orders.createSalesOrder({
      quotationNo: orderForm.quotationNo.trim(),
      salesOrderNo: orderForm.salesOrderNo.trim() || undefined,
    })
    orderOpen.value = false
    toast.success('销售订单已创建')
  } catch {
    orderFormError.value = formatError(orders.createSalesOrderError.value) || '创建失败，请稍后重试。'
  }
}

function formatAmount(value?: number | null) {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency: 'CNY', maximumFractionDigits: 2 }).format(value ?? 0)
}
function formatDate(value?: string | null) {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleDateString('zh-CN')
}
function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString('zh-CN')
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
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="待审报价" :value="pendingApprovalQuotations" hint="本页提交待审，需销售经理审批" />
      <SectionCard description="在途发货" :value="inTransitDeliveries" hint="本页已发货待签收" />
      <SectionCard description="跟进中商机" :value="activeOpportunities" hint="本页在跟进，待转报价" />
      <SectionCard description="本页订单金额" :value="formatAmount(ordersAmount)" hint="本页销售订单合计" />
    </SectionCards>

    <Tabs v-model="activeTab">
      <TabsList>
        <TabsTrigger value="opportunities">商机</TabsTrigger>
        <TabsTrigger value="quotations">报价单</TabsTrigger>
        <TabsTrigger value="orders">销售订单</TabsTrigger>
        <TabsTrigger value="deliveries">发货单</TabsTrigger>
      </TabsList>

      <TabsContent value="opportunities" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="opportunities.filters.keyword" class="h-9 w-48" placeholder="商机编号 / 客户" aria-label="商机关键字" />
            <Select v-model="opportunityStatus">
              <SelectTrigger class="h-9 w-32" aria-label="商机阶段"><SelectValue placeholder="全部阶段" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部阶段</SelectItem>
                <SelectItem value="open">跟进中</SelectItem>
                <SelectItem value="won">已赢单</SelectItem>
                <SelectItem value="lost">已丢单</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button size="sm" type="button" @click="openOpportunityDialog">
              <PlusIcon aria-hidden="true" />
              开立商机
            </Button>
          </template>
        </Toolbar>
        <p v-if="opportunitiesError" class="text-sm text-destructive" role="alert">{{ opportunitiesError }}</p>
        <DataTable
          :columns="opportunityColumns"
          :rows="opportunities.items.value"
          :row-key="(r: BusinessConsoleErpOpportunityItem) => r.opportunityNo ?? '商机'"
          :loading="opportunities.pending.value"
          empty-message="暂无商机。从这里开立线索，转化为报价与销售订单。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="opportunitiesPaged.page.value" v-model:page-size="opportunitiesPaged.pageSize.value" :total-items="opportunities.total.value" />
      </TabsContent>

      <TabsContent value="quotations" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="quotations.filters.keyword" class="h-9 w-48" placeholder="报价单号 / 客户" aria-label="报价单关键字" />
            <Select v-model="quotationStatus">
              <SelectTrigger class="h-9 w-32" aria-label="报价单状态"><SelectValue placeholder="全部状态" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="draft">草稿</SelectItem>
                <SelectItem value="submitted">待审批</SelectItem>
                <SelectItem value="approved">已批准</SelectItem>
                <SelectItem value="rejected">已驳回</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button size="sm" type="button" @click="openQuotationDialog">
              <PlusIcon aria-hidden="true" />
              新建报价
            </Button>
          </template>
        </Toolbar>
        <p v-if="quotationsError" class="text-sm text-destructive" role="alert">{{ quotationsError }}</p>
        <DataTable
          :columns="quotationColumns"
          :rows="quotations.items.value"
          :row-key="(r: BusinessConsoleErpQuotationItem) => r.quotationNo ?? '报价单'"
          :loading="quotations.pending.value"
          empty-message="暂无报价单。报价批准后即可转为销售订单。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
          <template #cell-totalAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalAmount) }}</span></template>
          <template #cell-actions="{ row }">
            <RowActions :label="`报价单操作 ${row.quotationNo ?? ''}`">
              <DropdownMenuItem :disabled="quotations.approveQuotationPending.value" @click="approveQuotation(row)">
                <CheckCircle2Icon aria-hidden="true" />
                审批通过
              </DropdownMenuItem>
            </RowActions>
          </template>
        </DataTable>
        <DataTablePagination v-model:page="quotationsPaged.page.value" v-model:page-size="quotationsPaged.pageSize.value" :total-items="quotations.total.value" />
      </TabsContent>

      <TabsContent value="orders" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="orders.filters.keyword" class="h-9 w-48" placeholder="销售单号 / 客户" aria-label="销售订单关键字" />
            <Select v-model="orderStatus">
              <SelectTrigger class="h-9 w-32" aria-label="销售订单状态"><SelectValue placeholder="全部状态" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="open">未发货</SelectItem>
                <SelectItem value="delivering">发货中</SelectItem>
                <SelectItem value="closed">已完成</SelectItem>
              </SelectContent>
            </Select>
          </template>
          <template #actions>
            <Button size="sm" type="button" @click="openOrderDialog">
              <PlusIcon aria-hidden="true" />
              新建销售订单
            </Button>
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
          <template #cell-totalAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalAmount) }}</span></template>
        </DataTable>
        <DataTablePagination v-model:page="ordersPaged.page.value" v-model:page-size="ordersPaged.pageSize.value" :total-items="orders.salesOrdersTotal.value" />
      </TabsContent>

      <TabsContent value="deliveries" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="deliveries.filters.keyword" class="h-9 w-48" placeholder="发货单号 / 客户" aria-label="发货单关键字" />
            <Select v-model="deliveryStatus">
              <SelectTrigger class="h-9 w-32" aria-label="发货单状态"><SelectValue placeholder="全部状态" /></SelectTrigger>
              <SelectContent>
                <SelectItem value="all">全部状态</SelectItem>
                <SelectItem value="released">已发货</SelectItem>
                <SelectItem value="delivered">已签收</SelectItem>
              </SelectContent>
            </Select>
          </template>
        </Toolbar>
        <p v-if="deliveriesError" class="text-sm text-destructive" role="alert">{{ deliveriesError }}</p>
        <DataTable
          :columns="deliveryColumns"
          :rows="deliveries.items.value"
          :row-key="(r: BusinessConsoleErpDeliveryOrderItem) => r.deliveryOrderNo ?? '发货单'"
          :loading="deliveries.pending.value"
          empty-message="暂无发货单。销售订单履约出货后会在这里生成。"
        >
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="deliveriesPaged.page.value" v-model:page-size="deliveriesPaged.pageSize.value" :total-items="deliveries.total.value" />
      </TabsContent>
    </Tabs>

    <Dialog v-model:open="opportunityOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>开立商机</DialogTitle>
          <DialogDescription>登记客户线索与商机主题，进入销售漏斗跟进。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitOpportunity">
          <FieldGroup>
            <Field>
              <FieldLabel for="erp-opp-customer">客户</FieldLabel>
              <Input id="erp-opp-customer" v-model="opportunityForm.customerCode" autocomplete="off" placeholder="如 CUST-HENGJING" />
            </Field>
            <Field>
              <FieldLabel for="erp-opp-topic">商机主题</FieldLabel>
              <Input id="erp-opp-topic" v-model="opportunityForm.topic" autocomplete="off" placeholder="如 智能网关G200 批量采购意向" />
            </Field>
          </FieldGroup>
          <FieldError v-if="opportunityFormError" :errors="[opportunityFormError]" />
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="opportunities.openOpportunityPending.value">
              <Spinner v-if="opportunities.openOpportunityPending.value" aria-hidden="true" />
              开立商机
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="quotationOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>新建报价</DialogTitle>
          <DialogDescription>面向客户报价，提交后经审批转为销售订单。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitQuotation">
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="erp-quo-customer">客户</FieldLabel>
              <Input id="erp-quo-customer" v-model="quotationForm.customerCode" autocomplete="off" placeholder="如 CUST-HENGJING" />
            </Field>
            <Field>
              <FieldLabel for="erp-quo-expires">有效期至</FieldLabel>
              <Input id="erp-quo-expires" v-model="quotationForm.expiresOn" type="date" />
            </Field>
            <Field>
              <FieldLabel for="erp-quo-sku">物料</FieldLabel>
              <Input id="erp-quo-sku" v-model="quotationForm.skuCode" autocomplete="off" placeholder="如 智能网关G200 的物料编码" />
            </Field>
            <Field>
              <FieldLabel for="erp-quo-required">需求日期</FieldLabel>
              <Input id="erp-quo-required" v-model="quotationForm.requiredDate" type="date" />
            </Field>
            <Field>
              <FieldLabel for="erp-quo-qty">数量</FieldLabel>
              <Input id="erp-quo-qty" v-model="quotationForm.quantity" type="number" min="1" step="1" />
            </Field>
            <Field>
              <FieldLabel for="erp-quo-price">单价（元）</FieldLabel>
              <Input id="erp-quo-price" v-model="quotationForm.unitPrice" type="number" min="0" step="0.01" />
            </Field>
          </FieldGroup>
          <FieldError v-if="quotationFormError" :errors="[quotationFormError]" />
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="quotations.createQuotationPending.value">
              <Spinner v-if="quotations.createQuotationPending.value" aria-hidden="true" />
              创建报价
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="orderOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>新建销售订单</DialogTitle>
          <DialogDescription>由已批准的报价转换生成销售订单，订单明细沿用报价。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitOrder">
          <FieldGroup>
            <Field>
              <FieldLabel for="erp-so-quotation">报价单号</FieldLabel>
              <Input id="erp-so-quotation" v-model="orderForm.quotationNo" autocomplete="off" placeholder="已批准的报价单号" />
            </Field>
            <Field>
              <FieldLabel for="erp-so-no">销售单号（可选）</FieldLabel>
              <Input id="erp-so-no" v-model="orderForm.salesOrderNo" autocomplete="off" placeholder="留空由系统编号" />
            </Field>
          </FieldGroup>
          <FieldError v-if="orderFormError" :errors="[orderFormError]" />
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="orders.createSalesOrderPending.value">
              <Spinner v-if="orders.createSalesOrderPending.value" aria-hidden="true" />
              创建销售订单
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
