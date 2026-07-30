<script setup lang="ts">
import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import { statusActionGate } from '@nerv-iip/business-core'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import WmsOperationalCandidateFilters from '@/components/wms/WmsOperationalCandidateFilters.vue'
import WmsReceivingQualityFlow from '@/components/wms/WmsReceivingQualityFlow.vue'
import ListScopeMeta from '@/components/business/ListScopeMeta.vue'
import { wmsStatusTone } from '@/data/businessLabels'
import { hasBusinessContext } from '@/composables/businessContextBinding'
import {
  isIndeterminateLifecycleWriteError,
  recoverLifecycleAction,
} from '@/composables/lifecycleAction'
import { usePendingWriteLeaveGuard } from '@/composables/usePendingWriteLeaveGuard'
import { createWmsIdempotencyKey, useWmsInboundOrders } from '@/composables/useBusinessWms'
import { useInventoryScopeDefaults } from '@/composables/useInventoryScope'
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
  wmsInboundOrderStatusFilterOptions,
  wmsInboundOrderStatusLabel,
  WMS_INBOUND_SOURCE_TYPE_OPTIONS,
  WMS_STATUS_ANY,
} from '@/data/wmsReference'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvCombobox,
  NvEntityPicker,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '收货入库',
    requiredPermissions: ['business.wms.receipts.read'],
  },
})

const {
  filters,
  inboundOrders,
  inventoryContext,
  inboundOrdersError,
  inboundOrdersPending,
  inboundOrdersTotal,
  refreshInboundOrders,
  completeInbound,
  completeInboundPending,
  createInbound,
  createInboundPending,
  receivingQualityGates,
  receivingQualityGatesPending,
  receivingQualityGatesError,
  supplierReturns,
  supplierReturnsPending,
  supplierReturnsError,
  refreshReceivingQuality,
  inboundOrdersLastUpdatedAt,
  inboundOrdersHasSuccessfulResponse,
  inboundOrdersHasFailedResponse,
} = useWmsInboundOrders({ workScopeRequired: true })
const {
  scopeKey,
  scopeOptions,
  selectedScopeLabel,
  hasSelection: inboundScopeReady,
  pending: workScopePending,
  error: workScopeError,
  refresh: refreshWorkScopes,
} = bindWmsWorkScopeFilters(filters, 'receipts')
const operationalCandidates = useWmsOperationalCandidates('receipt', filters)
const auth = useAuthStore()
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canManageReceipts = computed(() => permissionCodes.value.includes(P.wmsReceiptsManage))
const canReadQuality = computed(() =>
  permissionCodes.value.includes(P.qualityInspectionRecordsRead),
)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [
    () => filters.keyword,
    () => filters.status,
    () => filters.skuCode,
    () => filters.siteCode,
    () => filters.locationCode,
    () => filters.lotNo,
    () => filters.scopeKind,
    () => filters.scopeId,
  ],
})

const completeOpen = shallowRef(false)
const pendingOrder = shallowRef<InboundRow>()
const completeIntentKey = shallowRef('')
const completeIntentAttempted = shallowRef(false)
const completeIntentLocked = shallowRef(false)
usePendingWriteLeaveGuard(completeIntentLocked)

// 后端 WMS InboundOrderLine 要求 uomCode/正数 receivedQuantity/stagingLocationCode/qualityStatus/ownerType 均非空。
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
interface InboundLine {
  skuCode: string
  uomCode: string
  receivedQuantity: string
  stagingLocationCode: string
  lotNo: string
  qualityStatus: string
  ownerType: string
}
function emptyLine(): InboundLine {
  return {
    skuCode: '',
    uomCode: '',
    receivedQuantity: '',
    stagingLocationCode: '',
    lotNo: '',
    qualityStatus: 'available',
    ownerType: 'owned',
  }
}
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({
  inboundOrderNo: '',
  sourceDocumentType: '',
  sourceDocumentId: '',
  siteCode: '',
  lines: [emptyLine()] as InboundLine[],
})

function openCreate() {
  createForm.inboundOrderNo = ''
  createForm.sourceDocumentType = ''
  createForm.sourceDocumentId = ''
  createForm.siteCode = filters.siteCode ?? ''
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
    !createForm.inboundOrderNo.trim() ||
    !createForm.sourceDocumentType.trim() ||
    !createForm.sourceDocumentId.trim() ||
    !createForm.siteCode.trim()
  ) {
    createError.value = '请填写入库单号、来源类型、来源单据与工厂。'
    return
  }
  const filled = createForm.lines.filter(
    (l) =>
      l.skuCode.trim() || l.uomCode.trim() || l.receivedQuantity || l.stagingLocationCode.trim(),
  )
  if (filled.length === 0) {
    createError.value = '至少填写一行明细。'
    return
  }
  for (const [i, l] of filled.entries()) {
    if (!l.skuCode.trim() || !l.uomCode.trim() || !l.stagingLocationCode.trim()) {
      createError.value = `第 ${i + 1} 行：物料、单位、暂存库位均必填。`
      return
    }
    if (!(Number(l.receivedQuantity) > 0)) {
      createError.value = `第 ${i + 1} 行：收货数量需为正数。`
      return
    }
  }
  const lines = filled.map((l, i) => ({
    lineNo: String(i + 1),
    skuCode: l.skuCode.trim(),
    uomCode: l.uomCode.trim(),
    receivedQuantity: Number(l.receivedQuantity),
    stagingLocationCode: l.stagingLocationCode.trim(),
    lotNo: l.lotNo.trim() || undefined,
    qualityStatus: l.qualityStatus,
    ownerType: l.ownerType,
  }))
  try {
    await createInbound({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      inboundOrderNo: createForm.inboundOrderNo.trim(),
      sourceDocumentType: createForm.sourceDocumentType.trim(),
      sourceDocumentId: createForm.sourceDocumentId.trim(),
      siteCode: createForm.siteCode.trim(),
      lines,
    })
    createOpen.value = false
    notifySuccess('入库单已创建')
  } catch (error) {
    notifyError(error, '创建入库单失败，请稍后重试。')
  }
}

function canComplete(row: InboundRow) {
  return statusActionGate({
    domain: 'wms-inbound',
    action: 'complete',
    facts: { status: row.status },
  }).executable
}
function openComplete(row: InboundRow) {
  if (completeIntentLocked.value) return
  pendingOrder.value = row
  completeIntentKey.value = createWmsIdempotencyKey()
  completeIntentAttempted.value = false
  completeIntentLocked.value = false
  completeOpen.value = true
}
function onCompleteOpenChange(open: boolean) {
  if (!open && completeIntentLocked.value) return
  completeOpen.value = open
}
async function confirmComplete() {
  const id = pendingOrder.value?.inboundOrderId
  if (!id) return
  try {
    await completeInbound(id, completeIntentKey.value, {
      attempt: completeIntentAttempted.value ? 'retry' : 'initial',
      onCommandAttempt: () => {
        completeIntentAttempted.value = true
      },
    })
    completeOpen.value = false
    completeIntentKey.value = ''
    completeIntentAttempted.value = false
    completeIntentLocked.value = false
    notifySuccess('入库单已完成')
  } catch (error) {
    if (
      await recoverLifecycleAction(error, {
        reset: () => {
          completeOpen.value = false
          pendingOrder.value = undefined
          completeIntentKey.value = ''
          completeIntentAttempted.value = false
          completeIntentLocked.value = false
        },
        refresh: refreshInboundOrders,
        notify: (message) => notifyError(message),
      })
    ) {
      return
    }
    completeIntentLocked.value =
      completeIntentAttempted.value && isIndeterminateLifecycleWriteError(error)
    notifyError(error, '完成入库失败，请稍后重试。')
  }
}

/**
 * 读错误只归列表区域。创建 / 完成入库的失败一律走 toast；质检门禁与退货的读错误由
 * 行内的 WmsReceivingQualityFlow 自己呈现。共用一个变量时，「提交失败」会伪装成
 * 「列表加载失败」，旁路取数失败也会把整张列表说成坏的。
 */
const listErrorMessage = computed(() =>
  inboundOrdersError.value
    ? `取不到入库单列表，当前收货情况无法判断：${formatError(inboundOrdersError.value)}`
    : '',
)
/**
 * 页头计数一律用服务端总数；上下文未就绪 / 读取中 / 读失败时显文字而不是 0——
 * 骨架还在转就断言「0 张入库单」，等于把加载中说成没有单据。
 */
// 业务范围是否选定走全站唯一判定，不在页面里另写一份——判定分叉了，
// 「还没查」和「真的 0 条」很快又会混回同一个渲染。
const contextReady = computed(() => hasBusinessContext(filters) && inboundScopeReady.value)
const headerCount = computed(() => {
  if (!contextReady.value) return '未选择业务范围'
  if (inboundOrdersError.value) return '入库单数取不到'
  if (inboundOrdersPending.value) return '加载中'
  return `${inboundOrdersTotal.value} 张入库单`
})
// 工厂给默认值、单位跟随物料——收货行的库存上下文只差「选哪个物料」。
const { skuOptions, skusPending, siteOptions, sitesPending, resolveUomCode } =
  useInventoryScopeDefaults(filters)
// 库位与批次后端无主数据读面，从真实台账与仓储作业记录派生可选项。
const { locationOptions, lotOptions, warehouseCatalogPending } = useWarehouseCodeCatalog()
// 状态是后端枚举而不是目录，用哨兵值表达「全部」。
const statusFilter = computed({
  get: () => filters.status || WMS_STATUS_ANY,
  set: (value: string) => {
    filters.status = value === WMS_STATUS_ANY ? undefined : value
  },
})
/** 单位随物料的基本单位带出，不给手输：手输单位只会写出查不到货的组合。 */
function onLineSkuChange(line: { skuCode: string; uomCode: string }, skuCode: string) {
  line.skuCode = skuCode
  line.uomCode = skuCode ? resolveUomCode(skuCode) : ''
}
/** 网关明确回「还没给够条件」时才引导选物料；其余情况按拿到的上下文照常呈现。 */
const contextScopeRequired = computed(
  () => (inventoryContext.value?.status ?? '').toLowerCase() === 'scope-required',
)
/**
 * 网关的库存上下文在缺物料/单位/工厂时回 `scope-required`——那是「还没给条件」，
 * 不是「取不到」。这里只把真正的取数失败当异常，缺条件走下面的选择引导。
 */
const contextUnavailable = computed(() => {
  const status = (inventoryContext.value?.status ?? '').toLowerCase()
  if (!inventoryContext.value || status === '' || status === 'ok' || status === 'available') {
    return false
  }
  return status !== 'scope-required'
})

function refreshAll() {
  void refreshWorkScopes()
  void refreshInboundOrders()
  void refreshReceivingQuality()
  void operationalCandidates.refresh()
}

type InboundRow = BusinessConsoleWmsInboundOrderItem
const columns: NvDataTableColumn<InboundRow>[] = [
  {
    key: 'inboundOrderNo',
    header: '入库单号',
    cellClass: 'font-medium',
    accessor: (r) => r.inboundOrderNo ?? '无',
  },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'quality', header: '质检门禁', width: 'min-w-[22rem]' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

function rowKey(row: InboundRow) {
  return row.inboundOrderId ?? row.inboundOrderNo ?? '入库单'
}
/**
 * 入库单状态说人话。后端回的是 PascalCase 枚举（`PendingQualityCheck` /
 * `InventoryPostingFailed`），UI 包通用状态表只按小写整串查，多词状态一律查不到、
 * 直接把英文印到界面上。
 */
function statusLabel(value?: string | null) {
  return wmsInboundOrderStatusLabel(value)
}
function scanRecordRoute(row: InboundRow) {
  return {
    path: '/barcode/scans',
    query: {
      sourceWorkflow: 'wms.receiving',
      sourceDocumentId: row.inboundOrderNo ?? row.inboundOrderId ?? undefined,
    },
  }
}
function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="收货入库" :breadcrumbs="[{ label: '仓储作业' }]" :count="headerCount">
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="inboundOrdersPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建入库单
        </NvButton>
      </template>
    </NvPageHeader>

    <ListScopeMeta
      :scope="selectedScopeLabel || 'WMS 作业范围未就绪'"
      source="WMS 收货作业范围目录"
      :loaded="inboundOrders.length"
      :total="inboundOrdersTotal"
      :updated-at="inboundOrdersLastUpdatedAt"
      :empty="
        inboundOrdersHasSuccessfulResponse && !inboundOrdersError && inboundOrders.length === 0
      "
      :failed="
        inboundOrdersHasFailedResponse || Boolean(inboundOrdersError) || Boolean(workScopeError)
      "
      failure-explanation="WMS 收货作业范围或入库单未成功返回，请重试。"
      :empty-explanation="
        inboundScopeReady ? '当前作业范围没有收货单。' : '作业范围目录未就绪，未发起查询。'
      "
    />

    <p v-if="contextUnavailable" class="text-sm text-warning" role="status">
      没有权限或库存服务暂不可用，本页只显示入库单本身。
    </p>

    <!-- 库存上下文要「物料 × 单位 × 工厂」才成立；缺物料时给选择入口，不摆技术味提示。 -->
    <section
      v-if="contextScopeRequired"
      class="grid content-start justify-items-start gap-3 rounded-md border border-dashed border-border p-6"
    >
      <h2 class="text-sm font-semibold">选择物料，带出收货行的库存可用量</h2>
      <p class="text-sm text-muted-foreground">
        入库单列表不受影响；选定物料后这里显示该物料在
        {{ filters.siteCode || '当前工厂' }} 的现存量、可用量与预留占用。
      </p>
      <NvEntityPicker
        v-model="filters.skuCode"
        class="w-64"
        :options="skuOptions"
        title="选择物料"
        placeholder="选择物料"
        source-text="数据来自基础数据物料主数据"
        empty-text="暂无物料主数据，请先在基础数据维护物料"
        :loading="skusPending"
        aria-label="选择物料带出库存可用量"
      />
    </section>
    <WmsInventoryContextPanel
      v-else
      :context="inventoryContext"
      gap-message="本页暂不显示该收货行的库存可用量，请到库存可用量页按物料与工厂查看。"
    />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.keyword"
          class="w-56"
          placeholder="搜索入库单号、来源单据或物料"
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
        <NvEntityPicker
          v-model="filters.skuCode"
          class="w-56"
          :options="skuOptions"
          title="选择物料"
          placeholder="选择物料"
          source-text="数据来自基础数据物料主数据"
          empty-text="暂无物料主数据，请先在基础数据维护物料"
          :loading="skusPending"
          clearable
          aria-label="物料"
        />
        <NvEntityPicker
          v-model="filters.siteCode"
          class="w-36"
          :options="siteOptions"
          title="选择工厂"
          placeholder="工厂"
          source-text="数据来自基础数据工厂主数据"
          empty-text="暂无工厂主数据，请先在基础数据维护工厂"
          :loading="sitesPending"
          clearable
          aria-label="工厂"
        />
        <WmsOperationalCandidateFilters
          v-model:location-code="filters.locationCode"
          v-model:lot-no="filters.lotNo"
          :location-options="operationalCandidates.locationOptions.value"
          :lot-options="operationalCandidates.lotOptions.value"
          :pending="operationalCandidates.pending.value"
          :ready="operationalCandidates.ready.value"
          :error="operationalCandidates.error.value"
          v-model:search-keyword="operationalCandidates.searchKeyword.value"
          :source-label="operationalCandidates.sourceLabel.value"
          :as-of-utc="operationalCandidates.asOfUtc.value"
          :freshness-utc="operationalCandidates.freshnessUtc.value"
          :truncated="operationalCandidates.truncated.value"
          @retry="operationalCandidates.refresh"
        />
        <NvSearchSelect
          v-model="statusFilter"
          class="w-36"
          :options="wmsInboundOrderStatusFilterOptions"
          placeholder="全部状态"
          aria-label="入库单状态"
        />
      </template>
    </NvToolbar>

    <!-- 读失败 / 未选组织环境都由表格自己的三态呈现，绝不退化成「暂无入库单」。 -->
    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="inboundOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="inboundOrders"
      :row-key="rowKey"
      :loading="inboundOrdersPending"
      :error="inboundOrdersError"
      :error-message="listErrorMessage"
      :awaiting-scope="!contextReady"
      awaiting-scope-message="请先在顶部选择业务范围，再查看入库单。"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无入库单。收货作业产生入库单后会出现在这里。"
      @retry="refreshAll"
    >
      <template #cell-status="{ row }"
        ><NvStatusBadge
          :value="row.status"
          :label="statusLabel(row.status)"
          :tone="wmsStatusTone(row.status)"
      /></template>
      <template #cell-quality="{ row }">
        <WmsReceivingQualityFlow
          v-if="row.inboundOrderNo"
          :inbound-order-id="row.inboundOrderId"
          :inbound-order-no="row.inboundOrderNo"
          :gates="receivingQualityGates"
          :supplier-returns="supplierReturns"
          :quality-gate-status="row.qualityGateStatus"
          :is-released-for-putaway="row.isReleasedForPutaway"
          :can-manage-putaway="canManageReceipts"
          :can-read-quality="canReadQuality"
          :loading="receivingQualityGatesPending || supplierReturnsPending"
          :error="receivingQualityGatesError || supplierReturnsError"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-2">
          <NvButton size="sm" type="button" variant="ghost" as-child>
            <RouterLink :to="scanRecordRoute(row)">扫码记录</RouterLink>
          </NvButton>
          <NvButton
            size="sm"
            type="button"
            variant="outline"
            :aria-label="`完成入库 ${row.inboundOrderNo ?? ''}`"
            :disabled="!canComplete(row) || !row.inboundOrderId"
            @click="openComplete(row)"
          >
            完成入库
          </NvButton>
        </div>
      </template>
    </NvDataTable>

    <NvAlertDialog :open="completeOpen" @update:open="onCompleteOpenChange">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>完成入库</NvAlertDialogTitle>
          <!-- 破坏性/不可逆确认：保留一行「会发生什么」，这是决策信息而非说明书。 -->
          <NvAlertDialogDescription>
            确认完成入库单 {{ pendingOrder?.inboundOrderNo ?? '' }}？完成后按已收货明细过账入库。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel :disabled="completeInboundPending || completeIntentLocked"
            >取消</NvAlertDialogCancel
          >
          <NvButton type="button" :disabled="completeInboundPending" @click="confirmComplete"
            >完成入库</NvButton
          >
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
        <NvDialogHeader>
          <NvDialogTitle>新建入库单</NvDialogTitle>
          <!-- 界面上不再写说明书；仅供读屏播报对象范围。 -->
          <NvDialogDescription class="sr-only">收货入库单的单头与收货明细。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="wms-in-no">入库单号</NvFieldLabel>
              <NvInput id="wms-in-no" v-model="createForm.inboundOrderNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-in-site">工厂</NvFieldLabel>
              <NvEntityPicker
                id="wms-in-site"
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
              <NvFieldLabel for="wms-in-srctype">来源类型</NvFieldLabel>
              <NvSearchSelect
                id="wms-in-srctype"
                v-model="createForm.sourceDocumentType"
                :options="WMS_INBOUND_SOURCE_TYPE_OPTIONS"
                placeholder="选择来源类型"
                aria-label="来源类型"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-in-srcid">来源单据</NvFieldLabel>
              <NvInput id="wms-in-srcid" v-model="createForm.sourceDocumentId" autocomplete="off" />
            </NvField>
          </NvFieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium">收货明细</span>
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
                v-model="line.receivedQuantity"
                class="h-9 w-24"
                type="number"
                min="0"
                step="any"
                placeholder="收货数量*"
                :aria-label="`第 ${index + 1} 行收货数量`"
              />
              <NvEntityPicker
                v-model="line.stagingLocationCode"
                class="w-36"
                :options="locationOptions"
                title="选择暂存库位"
                placeholder="暂存库位*"
                :source-text="WAREHOUSE_CATALOG_SOURCE_TEXT"
                :empty-text="WAREHOUSE_LOCATION_EMPTY_TEXT"
                :loading="warehouseCatalogPending"
                clearable
                :aria-label="`第 ${index + 1} 行暂存库位`"
              />
              <!-- 收货批次可能是本次新到货的新批次号，因此保留录入能力、只做既有批次建议。 -->
              <NvCombobox
                v-model="line.lotNo"
                class="w-36"
                :suggestions="lotOptions"
                placeholder="批次"
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
            <NvButton type="submit" :disabled="createInboundPending">创建入库单</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
