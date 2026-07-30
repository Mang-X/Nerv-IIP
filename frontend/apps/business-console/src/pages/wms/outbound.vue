<script setup lang="ts">
import type { BusinessConsoleWmsOutboundOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { statusActionGate } from '@nerv-iip/business-core'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  isIndeterminateLifecycleWriteError,
  recoverLifecycleAction,
} from '@/composables/lifecycleAction'
import { usePendingWriteLeaveGuard } from '@/composables/usePendingWriteLeaveGuard'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import WmsOperationalCandidateFilters from '@/components/wms/WmsOperationalCandidateFilters.vue'
import { wmsStatusTone } from '@/data/businessLabels'
import { hasBusinessContext } from '@/composables/businessContextBinding'
import { createWmsIdempotencyKey, useWmsOutboundOrders } from '@/composables/useBusinessWms'
import ListScopeMeta from '@/components/business/ListScopeMeta.vue'
import { useInventoryScopeCatalog } from '@/composables/useInventoryScope'
import { usePagedList } from '@/composables/usePagedList'
import { useWmsOperationalCandidates } from '@/composables/useWmsOperationalCandidates'
import { bindWmsWorkScopeFilters } from '@/composables/useWmsWorkScope'
import {
  useWarehouseCodeCatalog,
  WAREHOUSE_CATALOG_SOURCE_TEXT,
  WAREHOUSE_LOCATION_EMPTY_TEXT,
  WAREHOUSE_LOT_EMPTY_TEXT,
} from '@/composables/useWarehouseCodeCatalog'
import {
  wmsOutboundOrderStatusFilterOptions,
  wmsOutboundOrderStatusLabel,
  WMS_OUTBOUND_SOURCE_TYPE_OPTIONS,
  WMS_STATUS_ANY,
} from '@/data/wmsReference'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvCheckbox,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvEntityPicker,
  NvInput,
  NvSearchSelect,
  NvMetricStrip,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '出库发货',
    requiredPermissions: ['business.wms.shipments.read'],
  },
})

const {
  filters,
  outboundOrders,
  outboundOrdersError,
  outboundOrdersPending,
  outboundOrdersTotal,
  refreshOutboundOrders,
  completeOutbound,
  completeOutboundPending,
  createOutbound,
  createOutboundPending,
  createOutboundError,
  outboundOrdersLastUpdatedAt,
  outboundOrdersHasSuccessfulResponse,
  outboundOrdersHasFailedResponse,
} = useWmsOutboundOrders({ workScopeRequired: true })
const {
  scopeKey,
  scopeOptions,
  selectedScopeLabel,
  hasSelection: outboundScopeReady,
  pending: workScopePending,
  error: workScopeError,
  refresh: refreshWorkScopes,
} = bindWmsWorkScopeFilters(filters, 'shipments')
const operationalCandidates = useWmsOperationalCandidates('shipment', filters)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [
    () => filters.keyword,
    () => filters.status,
    () => filters.locationCode,
    () => filters.lotNo,
    () => filters.scopeKind,
    () => filters.scopeId,
  ],
})
// 物料 / 单位 / 工厂走主数据目录；库位与批次后端无读面，从既有台账与作业记录派生。
const { skuOptions, skusPending, siteOptions, sitesPending, resolveUomCode } =
  useInventoryScopeCatalog()
const { locationOptions, lotOptions, warehouseCatalogPending } = useWarehouseCodeCatalog()
// 状态是后端枚举而不是目录，用哨兵值表达「全部」。
const statusFilter = computed({
  get: () => filters.status || WMS_STATUS_ANY,
  set: (value: string) => {
    filters.status = value === WMS_STATUS_ANY ? undefined : value
  },
})
/**
 * 单位不是独立选择项：出库行的单位由物料的基本单位决定，手输只会写出查不到货的组合。
 * 选完物料就把单位带出来，行上只读展示。
 */
function onLineSkuChange(line: { skuCode: string; uomCode: string }, skuCode: string) {
  line.skuCode = skuCode
  line.uomCode = skuCode ? resolveUomCode(skuCode) : ''
}

/**
 * 读错误只归列表区域。提交（创建 / 复核）失败一律走 toast，不并进这一条：
 * 两者共用一个变量时，「提交失败」会伪装成「列表加载失败」。
 */
const listErrorMessage = computed(() =>
  outboundOrdersError.value
    ? `取不到出库单列表，当前出库情况无法判断：${formatError(outboundOrdersError.value)}`
    : '',
)

// 后端 WMS OutboundOrderLine 要求 uomCode/正数 requestedQuantity/pickLocationCode/qualityStatus/ownerType 均非空。
const QUALITY_OPTIONS = [
  { label: '可用', value: 'available' },
  { label: '待检', value: 'inspection' },
  { label: '冻结', value: 'blocked' },
  { label: '不合格', value: 'rejected' },
]
const OWNER_OPTIONS = [
  { label: '自有', value: 'owned' },
  { label: '客户', value: 'customer' },
  { label: '供应商', value: 'supplier' },
  { label: '寄售', value: 'consignment' },
]
interface OutboundLine {
  skuCode: string
  uomCode: string
  requestedQuantity: string
  pickLocationCode: string
  lotNo: string
  qualityStatus: string
  ownerType: string
}
function emptyLine(): OutboundLine {
  return {
    skuCode: '',
    uomCode: '',
    requestedQuantity: '',
    pickLocationCode: '',
    lotNo: '',
    qualityStatus: 'available',
    ownerType: 'owned',
  }
}
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({
  outboundOrderNo: '',
  sourceDocumentType: '',
  sourceDocumentId: '',
  siteCode: '',
  lines: [emptyLine()] as OutboundLine[],
})

function openCreate() {
  createForm.outboundOrderNo = ''
  createForm.sourceDocumentType = ''
  createForm.sourceDocumentId = ''
  createForm.siteCode = ''
  createForm.lines = [emptyLine()]
  createError.value = ''
  createOpen.value = true
}
function addLine() {
  createForm.lines.push(emptyLine())
}
function removeLine(index: number) {
  createForm.lines.splice(index, 1)
  if (createForm.lines.length === 0) createForm.lines.push(emptyLine())
}
async function submitCreate() {
  if (
    !createForm.outboundOrderNo.trim() ||
    !createForm.sourceDocumentType.trim() ||
    !createForm.sourceDocumentId.trim() ||
    !createForm.siteCode.trim()
  ) {
    createError.value = '请填写出库单号、来源类型、来源单据与工厂。'
    return
  }
  const filled = createForm.lines.filter(
    (l) => l.skuCode.trim() || l.uomCode.trim() || l.requestedQuantity || l.pickLocationCode.trim(),
  )
  if (filled.length === 0) {
    createError.value = '至少填写一行明细。'
    return
  }
  for (const [i, l] of filled.entries()) {
    if (!l.skuCode.trim() || !l.uomCode.trim() || !l.pickLocationCode.trim()) {
      createError.value = `第 ${i + 1} 行：物料、单位、拣货库位均必填。`
      return
    }
    if (!(Number(l.requestedQuantity) > 0)) {
      createError.value = `第 ${i + 1} 行：需求数量需为正数。`
      return
    }
  }
  const lines = filled.map((l, i) => ({
    lineNo: String(i + 1),
    skuCode: l.skuCode.trim(),
    uomCode: l.uomCode.trim(),
    requestedQuantity: Number(l.requestedQuantity),
    pickLocationCode: l.pickLocationCode.trim(),
    lotNo: l.lotNo.trim() || undefined,
    qualityStatus: l.qualityStatus,
    ownerType: l.ownerType,
  }))
  try {
    await createOutbound({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      outboundOrderNo: createForm.outboundOrderNo.trim(),
      sourceDocumentType: createForm.sourceDocumentType.trim(),
      sourceDocumentId: createForm.sourceDocumentId.trim(),
      siteCode: createForm.siteCode.trim(),
      lines,
    })
    createOpen.value = false
    notifySuccess('出库单已创建')
  } catch (error) {
    notifyError(error, '创建出库单失败，请稍后重试。')
  }
}

const reviewOpen = shallowRef(false)
const pendingOrder = shallowRef<OutboundRow>()
const reviewIntentKey = shallowRef('')
const reviewIntentAttempted = shallowRef(false)
const reviewIntentLocked = shallowRef(false)
usePendingWriteLeaveGuard(reviewIntentLocked)
const reviewFrozenPayload = shallowRef<{ packReviewNo: string; passed: boolean }>()
const form = reactive({ packReviewNo: '', passed: true })
const formError = shallowRef('')
watch(
  () => `${form.packReviewNo}\u0000${form.passed}`,
  () => {
    if (!reviewIntentAttempted.value || reviewIntentLocked.value) return
    reviewIntentKey.value = createWmsIdempotencyKey()
    reviewIntentAttempted.value = false
    reviewFrozenPayload.value = undefined
    formError.value = ''
  },
)

function canComplete(row: OutboundRow) {
  return statusActionGate({
    domain: 'wms-outbound',
    action: 'complete',
    facts: { status: row.status },
  }).executable
}
function openReview(row: OutboundRow) {
  pendingOrder.value = row
  reviewIntentKey.value = createWmsIdempotencyKey()
  reviewIntentAttempted.value = false
  reviewIntentLocked.value = false
  reviewFrozenPayload.value = undefined
  form.packReviewNo = ''
  form.passed = true
  formError.value = ''
  reviewOpen.value = true
}
function onReviewOpenChange(open: boolean) {
  if (!open && reviewIntentLocked.value) return
  reviewOpen.value = open
}
async function submitReview() {
  const id = pendingOrder.value?.outboundOrderId
  if (!id) return
  if (!form.packReviewNo.trim()) {
    formError.value = '请输入复核单号。'
    return
  }
  try {
    const payload = reviewFrozenPayload.value ?? {
      packReviewNo: form.packReviewNo.trim(),
      passed: form.passed,
    }
    reviewFrozenPayload.value = payload
    await completeOutbound(id, payload, reviewIntentKey.value, {
      attempt: reviewIntentAttempted.value ? 'retry' : 'initial',
      onCommandAttempt: () => {
        reviewIntentAttempted.value = true
      },
    })
    reviewOpen.value = false
    reviewIntentKey.value = ''
    reviewIntentAttempted.value = false
    reviewIntentLocked.value = false
    reviewFrozenPayload.value = undefined
    notifySuccess('出库复核已提交')
  } catch (error) {
    if (
      await recoverLifecycleAction(error, {
        reset: () => {
          reviewOpen.value = false
          pendingOrder.value = undefined
          form.packReviewNo = ''
          reviewIntentKey.value = ''
          reviewIntentAttempted.value = false
          reviewIntentLocked.value = false
          reviewFrozenPayload.value = undefined
        },
        refresh: refreshOutboundOrders,
        notify: (message) => notifyError(message),
      })
    ) {
      return
    }
    reviewIntentLocked.value =
      reviewIntentAttempted.value && isIndeterminateLifecycleWriteError(error)
    formError.value = reviewIntentLocked.value
      ? '提交结果未知，当前内容已锁定；仅可按原内容重试。'
      : ''
    notifyError(error, '提交出库复核失败，请稍后重试。')
  }
}

// 复核对象由所选行带出，全部只读展示，不做成可编辑/只读输入框。
const reviewContextItems = computed(() => [
  { label: '出库单号', value: pendingOrder.value?.outboundOrderNo },
  {
    label: '创建时间',
    value: pendingOrder.value?.createdAtUtc
      ? formatDateTime(pendingOrder.value.createdAtUtc)
      : undefined,
  },
])
/**
 * 数字口径：页头与「出库单」KPI 一律用**服务端总数**；按状态分档只有当前页能算，
 * 因此一律带「本页」前缀，绝不和总数混在同一口径里。
 * 读不到数（上下文未就绪 / 读取中 / 读失败）时显 `—`，不断言 0——0 是结论，不是缺省值。
 */
// 业务范围是否选定走全站唯一判定，不在页面里另写一份——判定分叉了，
// 「还没查」和「真的 0 条」很快又会混回同一个渲染。
const contextReady = computed(() => hasBusinessContext(filters) && outboundScopeReady.value)
const listReady = computed(
  () => contextReady.value && !outboundOrdersError.value && !outboundOrdersPending.value,
)
const headerCount = computed(() => {
  if (!contextReady.value) return '未选择业务范围'
  if (outboundOrdersError.value) return '出库单数取不到'
  if (outboundOrdersPending.value) return '加载中'
  return `${outboundOrdersTotal.value} 张出库单`
})
const pageOpenCount = computed(
  () => outboundOrders.value.filter((r) => (r.status ?? '').toLowerCase() !== 'completed').length,
)
const metricCells = computed<NvMetricStripCell[]>(() => {
  if (!listReady.value) {
    return [
      { key: 'total', label: '出库单', value: '—' },
      { key: 'open', label: '本页待拣货复核发运', value: '—' },
      { key: 'completed', label: '本页已完成', value: '—' },
    ]
  }
  return [
    { key: 'total', label: '出库单', value: outboundOrdersTotal.value, unit: '张' },
    {
      key: 'open',
      label: '本页待拣货复核发运',
      value: pageOpenCount.value,
      unit: '张',
      valueTone: pageOpenCount.value > 0 ? 'warning' : undefined,
    },
    {
      key: 'completed',
      label: '本页已完成',
      value: outboundOrders.value.length - pageOpenCount.value,
      unit: '张',
      valueTone: 'success',
    },
  ]
})

type OutboundRow = BusinessConsoleWmsOutboundOrderItem
const columns: NvDataTableColumn<OutboundRow>[] = [
  {
    key: 'outboundOrderNo',
    header: '出库单号',
    cellClass: 'font-medium',
    accessor: (r) => r.outboundOrderNo ?? '无',
  },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

function rowKey(row: OutboundRow) {
  return row.outboundOrderId ?? row.outboundOrderNo ?? '出库单'
}
/**
 * 出库单状态说人话。后端回的是 PascalCase 枚举（`InventoryPostingPending` /
 * `InventoryPostingFailed`），UI 包通用状态表按小写整串查不到，会把英文印到界面上。
 */
function statusLabel(value?: string | null) {
  return wmsOutboundOrderStatusLabel(value)
}
function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

function refreshAll() {
  void refreshWorkScopes()
  void refreshOutboundOrders()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="出库发货" :breadcrumbs="[{ label: '仓储作业' }]" :count="headerCount">
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="outboundOrdersPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建出库单
        </NvButton>
      </template>
    </NvPageHeader>

    <ListScopeMeta
      :scope="selectedScopeLabel || 'WMS 作业范围未就绪'"
      source="WMS 发货作业范围目录"
      :loaded="outboundOrders.length"
      :total="outboundOrdersTotal"
      :updated-at="outboundOrdersLastUpdatedAt"
      :empty="
        outboundOrdersHasSuccessfulResponse && !outboundOrdersError && outboundOrders.length === 0
      "
      :failed="
        outboundOrdersHasFailedResponse || Boolean(outboundOrdersError) || Boolean(workScopeError)
      "
      failure-explanation="WMS 发货作业范围或出库单未成功返回，请重试。"
      :empty-explanation="
        outboundScopeReady ? '当前作业范围没有出库单。' : '作业范围目录未就绪，未发起查询。'
      "
    />

    <NvMetricStrip :cells="metricCells" />

    <WmsInventoryContextPanel
      title="库存明细"
      gap-message="逐行的批次、序列号与预留冻结明细请从对应拣货任务进入库存页查看。"
    />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.keyword"
          class="w-56"
          placeholder="搜索出库单号、来源单据或物料"
          aria-label="关键字搜索"
        />
        <NvSearchSelect
          v-model="scopeKey"
          class="w-56"
          :options="scopeOptions"
          :loading="workScopePending"
          placeholder="选择作业范围"
          aria-label="作业范围"
        />
        <WmsOperationalCandidateFilters
          v-model:location-code="filters.locationCode"
          v-model:lot-no="filters.lotNo"
          :location-options="operationalCandidates.locationOptions.value"
          :lot-options="operationalCandidates.lotOptions.value"
          :pending="operationalCandidates.pending.value"
          :source-label="operationalCandidates.sourceLabel.value"
          :source-kind="operationalCandidates.sourceKind.value"
          :as-of-utc="operationalCandidates.asOfUtc.value"
          :freshness-utc="operationalCandidates.freshnessUtc.value"
          :truncated="operationalCandidates.truncated.value"
        />
        <NvSearchSelect
          v-model="statusFilter"
          class="w-36"
          :options="wmsOutboundOrderStatusFilterOptions"
          placeholder="全部状态"
          aria-label="出库单状态"
        />
      </template>
    </NvToolbar>

    <!-- 读失败 / 未选组织环境都由表格自己的三态呈现，绝不退化成「暂无出库单」。 -->
    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="outboundOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="outboundOrders"
      :row-key="rowKey"
      :loading="outboundOrdersPending"
      :error="outboundOrdersError"
      :error-message="listErrorMessage"
      :awaiting-scope="!contextReady"
      awaiting-scope-message="请先在顶部选择业务范围，再查看出库单。"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无出库单。发货作业产生出库单后会出现在这里。"
      @retry="refreshOutboundOrders"
    >
      <template #cell-status="{ row }"
        ><NvStatusBadge
          :value="row.status"
          :label="statusLabel(row.status)"
          :tone="wmsStatusTone(row.status)"
      /></template>
      <template #cell-actions="{ row }">
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :aria-label="`完成复核 ${row.outboundOrderNo ?? ''}`"
          :disabled="!canComplete(row) || !row.outboundOrderId"
          @click="openReview(row)"
        >
          完成复核
        </NvButton>
      </template>
    </NvDataTable>

    <NvDialog :open="reviewOpen" @update:open="onReviewOpenChange">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>出库复核</NvDialogTitle>
          <!-- 复核对象已在下方只读区完整呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            出库单 {{ pendingOrder?.outboundOrderNo ?? '' }} 的发货前复核。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitReview">
          <CarriedContextSummary label="复核对象" :items="reviewContextItems" />
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="wms-pack-review-no">复核单号</NvFieldLabel>
              <NvInput
                id="wms-pack-review-no"
                v-model="form.packReviewNo"
                :disabled="reviewIntentLocked"
                :aria-invalid="Boolean(formError)"
                autocomplete="off"
              />
              <NvFieldError v-if="formError" :errors="[formError]" />
            </NvField>
            <NvField
              orientation="horizontal"
              class="items-center justify-between rounded-lg border p-3"
            >
              <NvFieldLabel for="wms-pack-passed">复核通过</NvFieldLabel>
              <NvCheckbox
                id="wms-pack-passed"
                v-model="form.passed"
                :disabled="reviewIntentLocked"
              />
            </NvField>
          </NvFieldGroup>
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline" :disabled="reviewIntentLocked">
                取消
              </NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="completeOutboundPending">
              {{ reviewIntentLocked ? '按原内容重试' : '提交复核' }}
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
        <NvDialogHeader>
          <NvDialogTitle>新建出库单</NvDialogTitle>
          <!-- 界面上不再写说明书；仅供读屏播报对象范围。 -->
          <NvDialogDescription class="sr-only">出库发货单的单头与发货明细。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="wms-out-no">出库单号</NvFieldLabel>
              <NvInput id="wms-out-no" v-model="createForm.outboundOrderNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-out-site">工厂</NvFieldLabel>
              <NvEntityPicker
                id="wms-out-site"
                v-model="createForm.siteCode"
                :options="siteOptions"
                title="选择工厂"
                placeholder="选择工厂"
                source-text="数据来自基础数据工厂主数据"
                empty-text="暂无工厂主数据，请先在基础数据维护工厂"
                :loading="sitesPending"
                clearable
                aria-label="工厂"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-out-srctype">来源类型</NvFieldLabel>
              <NvSearchSelect
                id="wms-out-srctype"
                v-model="createForm.sourceDocumentType"
                :options="WMS_OUTBOUND_SOURCE_TYPE_OPTIONS"
                placeholder="选择来源类型"
                aria-label="来源类型"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-out-srcid">来源单据</NvFieldLabel>
              <NvInput
                id="wms-out-srcid"
                v-model="createForm.sourceDocumentId"
                autocomplete="off"
              />
            </NvField>
          </NvFieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium">发货明细</span>
              <NvButton type="button" size="sm" variant="outline" @click="addLine">
                <PlusIcon aria-hidden="true" />
                添加行
              </NvButton>
            </div>
            <div
              v-for="(line, index) in createForm.lines"
              :key="index"
              class="flex flex-wrap items-end gap-2 rounded-md border p-2"
            >
              <NvEntityPicker
                :model-value="line.skuCode"
                class="w-44"
                :options="skuOptions"
                title="选择物料"
                placeholder="物料*"
                source-text="数据来自基础数据物料主数据"
                empty-text="暂无物料主数据，请先在基础数据维护物料"
                :loading="skusPending"
                :aria-label="`第 ${index + 1} 行物料`"
                @update:model-value="(value: string) => onLineSkuChange(line, value)"
              />
              <!-- 单位随物料的基本单位带出，不给手输：手输单位只会写出查不到货的组合。 -->
              <span
                class="inline-flex h-9 items-center rounded-md border border-input px-2.5 text-sm text-muted-foreground"
                :aria-label="`第 ${index + 1} 行单位`"
                >{{ line.uomCode || '单位' }}</span
              >
              <NvInput
                v-model="line.requestedQuantity"
                class="h-9 w-24"
                type="number"
                min="0"
                step="any"
                placeholder="需求数量*"
                :aria-label="`第 ${index + 1} 行需求数量`"
              />
              <NvEntityPicker
                v-model="line.pickLocationCode"
                class="w-36"
                :options="locationOptions"
                title="选择拣货库位"
                placeholder="拣货库位*"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                :aria-label="`第 ${index + 1} 行拣货库位`"
              />
              <NvEntityPicker
                v-model="line.lotNo"
                class="w-36"
                :options="lotOptions"
                title="选择批次"
                placeholder="批次"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOT_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                :aria-label="`第 ${index + 1} 行批次`"
              />
              <NvSelect v-model="line.qualityStatus">
                <NvSelectTrigger class="h-9 w-24" :aria-label="`第 ${index + 1} 行质量状态`"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in QUALITY_OPTIONS" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <NvSelect v-model="line.ownerType">
                <NvSelectTrigger class="h-9 w-24" :aria-label="`第 ${index + 1} 行货主类型`"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in OWNER_OPTIONS" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <NvButton
                type="button"
                size="icon-sm"
                variant="ghost"
                :aria-label="`删除第 ${index + 1} 行`"
                @click="removeLine(index)"
              >
                <Trash2Icon class="size-4" aria-hidden="true" />
              </NvButton>
            </div>
          </div>

          <NvFieldError v-if="createError" :errors="[createError]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createOutboundPending">创建出库单</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
