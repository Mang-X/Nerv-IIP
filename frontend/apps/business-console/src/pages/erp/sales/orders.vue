<script setup lang="ts">
import type { BusinessConsoleErpSalesOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpSalesOrders } from '@/composables/useBusinessErp'
import { useErpSiteCatalog } from '@/composables/useErpPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { usePagedList } from '@/composables/usePagedList'
import { useOrderUrgencies } from '@/composables/useOrderUrgency'
import {
  DEFAULT_URGENCY_DISPLAY_MODE,
  orderRowsByUrgency,
  type UrgencyDisplayMode,
} from '@/composables/useUrgencyDisplayMode'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import UrgencyDisplayModeSelect from '@/components/urgency/UrgencyDisplayModeSelect.vue'
import FulfillmentTimelineSheet from '@/components/fulfillment/FulfillmentTimelineSheet.vue'
import PartnerNameCell from '@/components/erp/PartnerNameCell.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, RouteIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { notifyError, notifySuccess } from '@/utils/notify'
import { firstQueryParam, formatAmount, pickerInvalidClass } from '../shared'

definePage({
  meta: { requiresAuth: true, title: '销售订单', requiredPermissions: ['business.erp.sales.read'] },
})

const orders = useErpSalesOrders()
// 履约工厂从工厂主数据里选，手输编码只会在提交时才发现敲错。
const { siteOptions, sitesPending } = useErpSiteCatalog()
// 客户列显名称：读面只回 customerCode，中文名在主数据业务伙伴里，前端按编码 join。
const { resolvePartner } = useBusinessPartnerNames()
const orderUrgencies = useOrderUrgencies(
  computed(() => orders.salesOrders.value.map((order) => order.salesOrderNo)),
)
const displayMode = shallowRef<UrgencyDisplayMode>(DEFAULT_URGENCY_DISPLAY_MODE)
// 排序独立于显示模式：默认按统一紧急度排序（等级→CR→预计延迟→due→等待）。
// 后端分页下仅对当前页行生效；跨页排序需后端支持（已知契约限制，本 PR 不实现）。
const orderedSalesOrders = computed(() =>
  orderRowsByUrgency(
    orders.salesOrders.value,
    (order) => order.salesOrderNo,
    orderUrgencies.byReference.value,
  ),
)
function refreshUrgency() {
  void orderUrgencies.refresh()
  orders.refreshSalesOrders()
}
const route = useRoute()
const { page, pageSize } = usePagedList(orders.filters, { resetOn: [() => orders.filters.keyword] })

watch(
  () => route.query.keyword,
  (keyword) => {
    orders.filters.keyword = firstQueryParam(keyword)
  },
  { immediate: true },
)

const columns: NvDataTableColumn<BusinessConsoleErpSalesOrderItem>[] = [
  {
    key: 'salesOrderNo',
    header: '销售单号',
    cellClass: 'font-medium',
    accessor: (r) => r.salesOrderNo ?? '-',
  },
  {
    key: 'customerCode',
    header: '客户',
    accessor: (r) => resolvePartner(r.customerCode) ?? r.customerCode ?? '-',
  },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'urgency', header: '紧急度', width: 'w-28' },
  {
    key: 'totalAmount',
    header: '金额',
    align: 'end',
    width: 'w-32',
    accessor: (r) => r.totalAmount ?? 0,
  },
  { key: 'fulfillment', header: '履约', align: 'end', width: 'w-28' },
]

const releasedCount = computed(
  () =>
    orders.salesOrders.value.filter((o) => (o.status ?? '').toLowerCase() === 'released').length,
)
const amount = computed(() =>
  orders.salesOrders.value.reduce((sum, order) => sum + (order.totalAmount ?? 0), 0),
)
// 金额只能按已取回的这一页加总，所以把口径写进副行而不是冒充全量。
const orderCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'released',
    label: '已释放订单',
    value: releasedCount.value,
    unit: '张',
    meta: '已下达到履约环节',
  },
  {
    key: 'amount',
    label: '订单金额',
    value: formatAmount(amount.value),
    meta: `当前列表 ${orders.salesOrders.value.length} 张订单合计`,
  },
])

const open = shallowRef(false)
const form = reactive({ quotationNo: '', salesOrderNo: '', siteCode: '' })
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  quotationNo: !form.quotationNo.trim(),
  siteCode: !form.siteCode.trim(),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

// 履约追踪 Sheet：行内入口按订单打开时间线。
const timelineOpen = shallowRef(false)
const timelineOrder = shallowRef<BusinessConsoleErpSalesOrderItem | null>(null)

function openTimeline(row: BusinessConsoleErpSalesOrderItem) {
  timelineOrder.value = row
  timelineOpen.value = true
}

function openDialog() {
  form.quotationNo = ''
  form.salesOrderNo = ''
  form.siteCode = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  try {
    await orders.createSalesOrder({
      quotationNo: form.quotationNo.trim(),
      siteCode: form.siteCode.trim(),
      salesOrderNo: form.salesOrderNo.trim() || undefined,
    })
    open.value = false
    notifySuccess('销售订单已创建')
  } catch (error) {
    notifyError(orders.createSalesOrderError.value ?? error, '创建销售订单失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售订单"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="`${orders.salesOrdersTotal.value} 张订单`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="orders.salesOrdersPending.value"
          @click="orders.refreshSalesOrders"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openDialog">
          <PlusIcon aria-hidden="true" />
          新建订单
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="orderCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="orders.filters.keyword"
          class="h-9 w-64"
          placeholder="销售单号 / 客户"
          aria-label="销售订单关键字"
        />
      </template>
      <template #actions>
        <UrgencyDisplayModeSelect v-model="displayMode" />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="orders.salesOrdersTotal.value"
      :columns="columns"
      :rows="orderedSalesOrders"
      :row-key="(r: BusinessConsoleErpSalesOrderItem) => r.salesOrderNo ?? '销售订单'"
      :loading="orders.salesOrdersPending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无销售订单。批准报价后可在这里生成订单。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-customerCode="{ row }">
        <PartnerNameCell :code="row.customerCode" />
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
      <template #cell-urgency="{ row }">
        <OrderUrgencyBadge
          :order-reference="row.salesOrderNo ?? ''"
          :mode="displayMode"
          :urgency="
            row.salesOrderNo ? orderUrgencies.byReference.value.get(row.salesOrderNo) : undefined
          "
          @refresh="refreshUrgency"
        />
      </template>
      <template #cell-totalAmount="{ row }"
        ><span class="tabular-nums">{{ formatAmount(row.totalAmount) }}</span></template
      >
      <template #cell-fulfillment="{ row }">
        <NvButton
          size="sm"
          variant="ghost"
          type="button"
          :disabled="!row.salesOrderNo"
          @click="openTimeline(row)"
        >
          <RouteIcon aria-hidden="true" />
          履约追踪
        </NvButton>
      </template>
    </NvDataTable>

    <FulfillmentTimelineSheet v-model:open="timelineOpen" :order="timelineOrder" />

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建销售订单</NvDialogTitle>
          <NvDialogDescription class="sr-only">由已批准的销售报价转订单。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="erp-so-quotation">
                已批准报价单号 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-so-quotation"
                v-model="form.quotationNo"
                autocomplete="off"
                :data-invalid="showErrors && invalid.quotationNo ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-so-site">
                履约工厂 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-so-site"
                v-model="form.siteCode"
                :options="siteOptions"
                title="选择履约工厂"
                placeholder="选择履约工厂"
                source-text="数据来自基础数据工厂主数据"
                empty-text="暂无工厂，请先在「基础数据 · 工厂」维护"
                :loading="sitesPending"
                aria-label="履约工厂"
                :class="pickerInvalidClass(showErrors && invalid.siteCode)"
              />
            </NvField>
            <NvField
              ><NvFieldLabel for="erp-so-no">销售单号（留空自动编号）</NvFieldLabel
              ><NvInput id="erp-so-no" v-model="form.salesOrderNo" autocomplete="off"
            /></NvField>
          </NvFieldGroup>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请填写已批准报价单号并选择履约工厂。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="orders.createSalesOrderPending.value">
              <Spinner v-if="orders.createSalesOrderPending.value" aria-hidden="true" />
              创建订单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
