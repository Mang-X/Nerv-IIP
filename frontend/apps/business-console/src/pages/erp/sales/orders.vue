<script setup lang="ts">
import type { BusinessConsoleErpSalesOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpSalesOrders } from '@/composables/useBusinessErp'
import { useErpSiteCatalog } from '@/composables/useErpPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { usePagedList } from '@/composables/usePagedList'
import { useOrderUrgencies } from '@/composables/useOrderUrgency'
import { buildKpiTrend } from '@/utils/kpiTrend'
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
import { LockOpenIcon, PlusIcon, RefreshCwIcon, RouteIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  UNAVAILABLE_TEXT,
  erpReadState,
  firstQueryParam,
  formatAmount,
  formatDate,
  pickerInvalidClass,
  readCount,
} from '../shared'

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

/** 这张订单的承诺交期，取自紧急度读面（同一批已取回的数据），没有就是没有，不编。 */
function dueUtcOf(row: BusinessConsoleErpSalesOrderItem) {
  if (!row.salesOrderNo) return undefined
  return (
    orderUrgencies.byReference.value.get(row.salesOrderNo)?.timeCriticality?.dueUtc ?? undefined
  )
}
/** 已逾期的交期标红：销售看这一列就是为了先发现哪张要爆。 */
function isOverdue(row: BusinessConsoleErpSalesOrderItem) {
  const due = dueUtcOf(row)
  if (!due) return false
  const parsed = Date.parse(due)
  return Number.isFinite(parsed) && parsed < Date.now()
}

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
  // 交期是销售员看订单的第一眼信息，此前整列缺失（GH#1292 第 1 项）。
  // 销售订单读面本身不带交期，但紧急度读面（本页已在取）带 timeCriticality.dueUtc，
  // 正是这张订单的承诺交期——直接用它，不新增契约。
  {
    key: 'dueUtc',
    header: '交期',
    width: 'w-32',
    accessor: (r) => dueUtcOf(r),
  },
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
/**
 * 数字口径（GH#1292 第 2 项）：按状态分档与金额合计**只有当前页能算**，一律带「本页」前缀，
 * 副行再写清是哪一页的多少张，绝不和页头的服务端总数混成同一口径被读成全局 KPI。
 * 全量口径只有一处：页头 `count`（服务端总数）。
 */
const readState = computed(() =>
  erpReadState({
    noun: '销售订单',
    unit: '张',
    ready: orders.ready.value,
    pending: orders.salesOrdersPending.value,
    error: orders.salesOrdersError.value,
    total: orders.salesOrdersTotal.value,
    filtered: Boolean(orders.filters.keyword || orders.filters.status),
    emptyHint: '还没有销售订单。批准报价后可在这里生成订单。',
  }),
)

/** 「本页第 N 页 · 共 M 张」——所有分页口径指标共用的同一句副行，读者一眼知道这数字管多大范围。 */
const pageScopeMeta = computed(
  () =>
    `第 ${page.value} 页 ${orders.salesOrders.value.length} 张 · 全部 ${orders.salesOrdersTotal.value} 张`,
)

const orderCells = computed<NvMetricStripCell[]>(() => {
  // 销售订单读面不带下单/释放时间戳（列表列的交期来自紧急度读面，不是本行的发生时间），
  // 按天分桶无从算起，两张卡都由当前真值反推形状——末点仍落在卡片数字上。
  const released = readState.value.trustworthy
    ? buildKpiTrend('erp.sales.releasedOrders', releasedCount.value, { kind: 'count' })
    : undefined
  const orderAmount = readState.value.trustworthy
    ? buildKpiTrend('erp.sales.orderAmount', amount.value, { kind: 'amount' })
    : undefined

  return [
    {
      key: 'released',
      label: '本页已释放订单',
      value: readCount(readState.value, releasedCount.value),
      unit: readState.value.trustworthy ? '张' : '',
      meta: readState.value.trustworthy
        ? `已下达到履约环节 · ${pageScopeMeta.value}`
        : readState.value.emptyMessage,
      delta: released?.delta,
      series: released?.series,
      seriesLabels: released?.seriesLabels,
      seriesUnit: '张',
    },
    {
      key: 'amount',
      label: '本页订单金额',
      // 取不到订单时显 `—`：¥0.00 会被读成"这一批确实没金额"。
      value: readState.value.trustworthy ? formatAmount(amount.value) : UNAVAILABLE_TEXT,
      meta: readState.value.trustworthy ? pageScopeMeta.value : readState.value.emptyMessage,
      delta: orderAmount?.delta,
      series: orderAmount?.series,
      seriesLabels: orderAmount?.seriesLabels,
    },
  ]
})

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

// 信用解冻复核：仅 credit-held 行出现入口；提交后走审批中心，审批通过订单恢复「已下达」。
const creditHoldOpen = shallowRef(false)
const creditHoldOrder = shallowRef<BusinessConsoleErpSalesOrderItem | null>(null)

function isCreditHeld(row: BusinessConsoleErpSalesOrderItem) {
  return (row.status ?? '').toLowerCase() === 'credit-held'
}

function openCreditHoldDialog(row: BusinessConsoleErpSalesOrderItem) {
  creditHoldOrder.value = row
  creditHoldOpen.value = true
}

async function submitCreditHoldRelease() {
  const salesOrderNo = creditHoldOrder.value?.salesOrderNo
  if (!salesOrderNo) return
  try {
    await orders.releaseCreditHold({ salesOrderNo })
    creditHoldOpen.value = false
    notifySuccess('已提交信用解冻复核，审批通过后订单将恢复为已下达（released）')
  } catch (error) {
    notifyOperationFailure(
      '提交信用解冻复核失败',
      orders.releaseCreditHoldError.value ?? error,
      '提交信用解冻复核失败，请稍后重试。',
    )
  }
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
    const result = await orders.createSalesOrder({
      quotationNo: form.quotationNo.trim(),
      siteCode: form.siteCode.trim(),
      salesOrderNo: form.salesOrderNo.trim() || undefined,
    })
    open.value = false
    // 已转出报价重复转订单：后端幂等返回既有订单号（不新建、不产生新需求），这里明确告知用户复用了哪张单。
    if (result?.reusedExistingOrder) {
      notifySuccess(`该报价已转出，已为你带回既有订单 ${result.salesOrderNo ?? ''}`.trim())
    } else {
      notifySuccess(
        result?.salesOrderNo ? `销售订单 ${result.salesOrderNo} 已创建` : '销售订单已创建',
      )
    }
  } catch (error) {
    notifyOperationFailure(
      '创建销售订单失败',
      orders.createSalesOrderError.value ?? error,
      '创建销售订单失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售订单"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="readState.count"
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
      :empty-message="readState.emptyMessage"
      :error="readState.error"
      :error-message="readState.errorMessage"
      :awaiting-scope="readState.awaitingScope"
      :awaiting-scope-message="readState.awaitingScopeMessage"
      @retry="orders.refreshSalesOrders"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-customerCode="{ row }">
        <PartnerNameCell :code="row.customerCode" />
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
      <template #cell-dueUtc="{ row }">
        <span class="tabular-nums" :class="isOverdue(row) ? 'text-destructive' : undefined">{{
          formatDate(dueUtcOf(row))
        }}</span>
      </template>
      <template #cell-urgency="{ row }">
        <OrderUrgencyBadge
          :order-reference="row.salesOrderNo ?? ''"
          :mode="displayMode"
          :urgency="
            row.salesOrderNo ? orderUrgencies.byReference.value.get(row.salesOrderNo) : undefined
          "
          :source-unavailable="orderUrgencies.error?.value != null"
          @refresh="refreshUrgency"
        />
      </template>
      <template #cell-totalAmount="{ row }"
        ><span class="tabular-nums">{{ formatAmount(row.totalAmount) }}</span></template
      >
      <template #cell-fulfillment="{ row }">
        <div class="flex items-center justify-end gap-1">
          <NvButton
            v-if="isCreditHeld(row)"
            size="sm"
            variant="ghost"
            type="button"
            :disabled="!row.salesOrderNo"
            @click="openCreditHoldDialog(row)"
          >
            <LockOpenIcon aria-hidden="true" />
            解冻复核
          </NvButton>
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
        </div>
      </template>
    </NvDataTable>

    <FulfillmentTimelineSheet v-model:open="timelineOpen" :order="timelineOrder" />

    <NvDialog v-model:open="creditHoldOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>提交信用解冻复核</NvDialogTitle>
          <NvDialogDescription>
            订单 {{ creditHoldOrder?.salesOrderNo ?? '-' }}（客户
            {{
              resolvePartner(creditHoldOrder?.customerCode) ?? creditHoldOrder?.customerCode ?? '-'
            }}）因超出客户信用额度被冻结。
          </NvDialogDescription>
        </NvDialogHeader>
        <p class="text-sm text-muted-foreground">
          提交后将以你的账号发起「信用解冻」审批，由审批中心的信用复核人（厂长/管理员）裁决；
          审批通过后订单自动恢复为「已下达（released）」，可继续发货履约。
        </p>
        <NvDialogFooter>
          <NvDialogClose as-child>
            <NvButton type="button" variant="outline">取消</NvButton>
          </NvDialogClose>
          <NvButton
            type="button"
            :disabled="orders.releaseCreditHoldPending.value"
            @click="submitCreditHoldRelease"
          >
            <Spinner v-if="orders.releaseCreditHoldPending.value" aria-hidden="true" />
            提交解冻复核
          </NvButton>
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>

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
