import type {
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableSort } from '@nerv-iip/ui'
import {
  mesWorkOrderStatusOptions,
  useMesReferenceLabels,
} from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { useBusinessMasterDataResources, useBusinessSkus } from '@/composables/master-data'
import {
  useMesOperationTasks,
  useMesWorkOrderTransformations,
  useMesWorkOrders,
  type MesWorkOrderTransformationResult,
} from '@/composables/mes'
import {
  type MergeTransformationSubmit,
  type SplitTransformationSubmit,
  type WorkOrderTransformationState,
} from '@/components/mes/WorkOrderTransformationDialog.vue'
import {
  isTransformationConflict,
  type WorkOrderTransformationSource,
} from '@/composables/mes/workOrderTransformation'
import { toBaseUomBySku } from '@/composables/master-data/skuBaseUom'
import { useMesMaterialVersionCatalog } from '@/composables/mes/useMesPickerCatalog'
import { useOrderUrgencies } from '@/composables/urgency/useOrderUrgency'
import {
  DEFAULT_URGENCY_DISPLAY_MODE,
  orderRowsByUrgency,
  type UrgencyDisplayMode,
} from '@/composables/urgency/useUrgencyDisplayMode'
import { isSchedulableWorkbenchCandidate } from '@/composables/scheduling/useSchedulingWorkbench'
import {
  useCanScheduleSingleOrder,
  SINGLE_ORDER_SCHEDULING_DENIED_REASON,
} from '@/composables/scheduling/useSingleOrderScheduling'
import type { ProductionReportContext } from '@/composables/mes/useProductionReportForm'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { watchDebounced } from '@vueuse/core'
import { computed, ref, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useWorkOrderReleaseDialog } from './useWorkOrderReleaseDialog'
import { useRushWorkOrderDialog } from './useRushWorkOrderDialog'

import { columns, rowKey } from './columns'

export function useWorkOrdersPage() {
  type Row = BusinessConsoleMesWorkOrderItem

  const {
    createRushWorkOrder,
    createRushWorkOrderError,
    createRushWorkOrderPending,
    filters,
    refreshWorkOrders,
    releaseWorkOrder,
    releaseWorkOrderPending,
    workOrders,
    workOrdersError,
    workOrdersPending,
    workOrdersTotal,
    workOrderReadScope,
    workOrderReadScopeMessage,
    readWorkOrderForRelease,
    workOrderManageScopeMessage,
    workOrderManageScopePending,
    workOrderManageScopeReady,
    workOrderManageScope,
  } = useMesWorkOrders()
  const workOrderTransformations = useMesWorkOrderTransformations({
    filters,
    readScope: workOrderReadScope,
    manageScope: workOrderManageScope,
  })
  const orderUrgencies = useOrderUrgencies(
    computed(() => workOrders.value.map((order) => order.workOrderId)),
  )
  const displayMode = shallowRef<UrgencyDisplayMode>(DEFAULT_URGENCY_DISPLAY_MODE)
  function refreshUrgency() {
    void orderUrgencies.refresh()
    refreshWorkOrders()
  }

  const router = useRouter()
  const { skus } = useBusinessSkus()
  const baseUomBySku = toBaseUomBySku(skus)
  const { resolveSku, resolveWorkCenter } = useMesDisplayNames()
  const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')

  const rushSheetOpen = shallowRef(false)
  const reportSheetOpen = shallowRef(false)
  const sheetWorkOrderId = ref<string | null>(null)

  // --- Filters (live) ---
  const keyword = ref('')
  const statusFilter = ref('all')
  const workCenterFilter = ref('all')

  watch(statusFilter, (value) => {
    filters.status = value === 'all' ? undefined : value
  })
  watchDebounced(
    keyword,
    (value) => {
      filters.keyword = value.trim() || undefined
    },
    { debounce: 300, maxWait: 1000 },
  )
  watch(workCenterFilter, (value) => {
    filters.workCenterId = value === 'all' ? undefined : value
  })

  const statusOptions = mesWorkOrderStatusOptions
  const { statusLabel } = useMesReferenceLabels()
  const canScheduleSingleOrder = useCanScheduleSingleOrder()
  const scheduleTarget = shallowRef<Row | null>(null)
  const scheduleOpen = shallowRef(false)
  function scheduleDisabledReason(order: Row) {
    if (!isSchedulableWorkbenchCandidate(order)) {
      return order.productionVersionId
        ? '工单已处于终态，不能再排产。'
        : '工单没有生产版本，排程无法展开工艺路线。'
    }
    return canScheduleSingleOrder.value ? '' : SINGLE_ORDER_SCHEDULING_DENIED_REASON
  }
  function openSchedule(order: Row) {
    if (scheduleDisabledReason(order)) return
    scheduleTarget.value = order
    scheduleOpen.value = true
  }
  // 组织与环境不进表单：它们由壳层异步绑定到 filters，整页直开时表单初始化那一刻还是空串（#3858）。
  // 报工对象由所选工单行带出（工单 + 该工单的首道可报工序），弹窗不提供任何挑选入口。
  const reportContext = shallowRef<ProductionReportContext | null>(null)

  const listErrorMessage = computed(() => inlineErrorMessage(workOrdersError.value))

  const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))

  // ── 急单表单的四个选择器 ────────────────────────────────────────
  // 物料 ▸ 生产版本 从属，工作中心 ▸ 工序任务 从属：上游变了清空下游。
  const { productionVersionLabel, productionVersionOptions, productionVersionsPending } =
    useMesMaterialVersionCatalog()
  const { operationTasks, operationTasksPending, refreshOperationTasks } = useMesOperationTasks()

  const auth = useAuthStore()
  const canManageWorkOrders = computed(() =>
    (auth.principal?.permissionCodes ?? []).includes(P.mesWorkOrdersManage),
  )

  const selectedWorkOrderIds = ref<(string | number)[]>([])
  const mergeDialogOpen = ref(false)
  const mergeState = ref<WorkOrderTransformationState>('idle')
  const mergeError = ref('')
  const mergeResult = shallowRef<MesWorkOrderTransformationResult | null>(null)
  const mergeIdempotencyKey = ref('')
  const mergeSources = computed<
    Array<WorkOrderTransformationSource & { label?: string; skuLabel?: string }>
  >(() =>
    selectedWorkOrderIds.value
      .map((id) => workOrders.value.find((order) => rowKey(order) === id))
      .filter((order): order is Row & { workOrderId: string } => Boolean(order?.workOrderId))
      .map((order) => ({
        workOrderId: order.workOrderId,
        label: order.workOrderNo ?? order.workOrderId,
        skuLabel: resolveSku(order.skuCode ?? order.skuId),
        skuId: order.skuId,
        productionVersionId: order.productionVersionId,
        quantity: order.quantity,
        // PR-C 工单列表没有 UOM；使用既有 MasterData SKU 读面中的基本单位事实，不臆造单位。
        uomCode: baseUomBySku.value.get((order.skuCode ?? order.skuId)?.trim() ?? ''),
        status: order.status,
      })),
  )
  const mergeUnitUnavailable = computed(
    () =>
      selectedWorkOrderIds.value.length >= 2 &&
      mergeSources.value.some((source) => !source.uomCode?.trim()),
  )
  const mergeButtonDisabled = computed(
    () =>
      !canManageWorkOrders.value ||
      !workOrderManageScopeReady.value ||
      workOrderManageScopePending.value ||
      selectedWorkOrderIds.value.length < 2 ||
      mergeUnitUnavailable.value ||
      Boolean(mergeState.value === 'loading'),
  )
  const mergeButtonTitle = computed(() => {
    if (mergeUnitUnavailable.value) {
      return '选中的工单未返回单位信息，无法确认数量单位；请刷新列表后重试。'
    }
    if (mergeButtonDisabled.value) {
      return '请选择至少两个工单，并确认当前主体具有工单管理权限。'
    }
    return '将选中工单合并为新的工单'
  })
  const mergePending = computed(
    () => workOrderTransformations.mergeWorkOrdersPending.value || mergeState.value === 'loading',
  )

  function openMergeDialog() {
    if (mergeButtonDisabled.value) return
    mergeError.value = ''
    mergeState.value = 'idle'
    mergeResult.value = null
    mergeIdempotencyKey.value = newMesIdempotencyKey('merge-work-orders')
    mergeDialogOpen.value = true
  }

  async function submitMerge(payload: SplitTransformationSubmit | MergeTransformationSubmit) {
    if (!('sourceWorkOrderIds' in payload)) return
    mergeState.value = 'loading'
    mergeError.value = ''
    try {
      const result = await workOrderTransformations.mergeWorkOrders(payload)
      mergeResult.value = result
      if (result.readback) {
        mergeState.value = 'success'
        notifySuccess(`已合并 ${payload.sourceWorkOrderIds.length} 个工单，结果已回读。`)
        selectedWorkOrderIds.value = []
        await refreshWorkOrders()
      } else {
        mergeState.value = 'accepted'
        notifySuccess('合并请求已受理，但结果暂未回读；请在本窗口重试回读。')
      }
    } catch (error) {
      mergeState.value = isTransformationConflict(error) ? 'conflict' : 'error'
      mergeError.value = inlineErrorMessage(error, '合并工单失败，请刷新后重试。')
      notifyOperationFailure('合并工单失败', error, '合并工单失败，请刷新后重试。')
    }
  }

  async function retryMergeReadback() {
    const current = mergeResult.value
    if (!current) return
    mergeState.value = 'loading'
    try {
      const readback = await workOrderTransformations.readTransformation(
        current.mutation.transformationId,
      )
      mergeResult.value = { ...current, readback, readbackError: undefined }
      mergeState.value = 'success'
      selectedWorkOrderIds.value = []
      notifySuccess('合并结果已回读。')
      await refreshWorkOrders()
    } catch (error) {
      mergeResult.value = { ...current, readbackError: error }
      mergeState.value = 'accepted'
      notifyOperationFailure(
        '回读合并结果失败',
        error,
        '合并已受理，但结果暂不可用，请稍后重试回读。',
      )
    }
  }

  const {
    releaseIntent,
    releaseWarningsConfirmed,
    releasePreflightPending,
    clearReleaseIntent,
    releaseIntentOrder,
    releaseDialogOpen,
    releaseValidationMessage,
    releaseRetroactiveNotice,
    canSubmitRelease,
    releaseBlocker,
    openReleaseDialog,
    submitReleaseWorkOrder,
  } = useWorkOrderReleaseDialog(
    {
      filters,
      workOrders,
      releaseWorkOrder,
      releaseWorkOrderPending,
      readWorkOrderForRelease,
      workOrderManageScopePending,
      workOrderManageScopeReady,
      workOrderManageScopeMessage,
      refreshWorkOrders,
    },
    canManageWorkOrders,
    refreshOperationTasks,
  )

  const {
    rushForm,
    rushVersionOptions,
    rushOperationTaskOptions,
    canCreateRush,
    submitRushWorkOrder,
  } = useRushWorkOrderDialog(
    { filters, createRushWorkOrder, createRushWorkOrderError },
    { productionVersionOptions, operationTasks },
    rushSheetOpen,
    newMesIdempotencyKey,
  )

  const visibleWorkOrders = computed(() => workOrders.value)

  // --- Sort (page-owned, before pagination) ---
  // 默认无列排序：按统一紧急度（等级→CR→预计延迟→due→等待）排序；用户点列头后按列排序。
  // 后端分页，紧急度排序仅对当前页行生效；跨页排序需后端支持（已知契约限制）。
  const sort = ref<NvDataTableSort | null>(null)
  function sortValue(order: Row, key: string): string | number {
    if (key === 'quantity') return order.quantity ?? 0
    if (key === 'completedQuantity') return order.completedQuantity ?? 0
    if (key === 'dueUtc') return order.dueUtc ? new Date(order.dueUtc).getTime() : 0
    if (key === 'operationCount') return order.operationTasks?.length ?? 0
    return (order[key as keyof Row] as string | null) ?? ''
  }
  const sortedWorkOrders = computed(() => {
    if (!sort.value) {
      return orderRowsByUrgency(
        visibleWorkOrders.value,
        (order) => order.workOrderId,
        orderUrgencies.byReference.value,
      )
    }
    const { key, direction } = sort.value
    const factor = direction === 'asc' ? 1 : -1
    return [...visibleWorkOrders.value].sort((a, b) => {
      const av = sortValue(a, key)
      const bv = sortValue(b, key)
      if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * factor
      return String(av).localeCompare(String(bv), 'zh-Hans-CN') * factor
    })
  })

  // --- Pagination (server-driven: filters.skip/take, total from backend) ---
  const page = ref(1)
  const pageSize = ref('10')
  const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
  // 后端已分页和过滤，当前页内仅做展示排序，不再切片。
  const pagedWorkOrders = computed(() => sortedWorkOrders.value)
  watch([keyword, statusFilter, workCenterFilter, pageSize], () => {
    page.value = 1
  })
  watch(
    [page, pageSize],
    () => {
      filters.skip = (page.value - 1) * pageSizeNumber.value
      filters.take = pageSizeNumber.value
    },
    { immediate: true },
  )

  // 报工不再靠 URL query 跨页唤起：工序执行页与报工记录页都在本地行上直接打开同一个报工弹窗，
  // 上下文随行带出，避免跳到本页后只剩两个 ID、工作中心/物料/计划数量全丢。

  // 一个工单可能有多道工序：优先带出「还没做完」的第一道（executing/ready/queued 之类），
  // 都做完了才退回第一道，避免默认把报工记到已完成工序上。
  function reportableTask(order: Row) {
    const tasks = (order.operationTasks ?? []).filter((task) => task.operationTaskId)
    const done = new Set(['completed', 'closed', 'cancelled'])
    return tasks.find((task) => !done.has((task.status ?? '').toLowerCase())) ?? tasks[0]
  }
  function openReport(order: Row) {
    const task = reportableTask(order)
    if (!order.workOrderId || !task?.operationTaskId) return
    reportContext.value = {
      workOrderId: order.workOrderId,
      workOrderNo: order.workOrderNo,
      operationTaskId: task.operationTaskId,
      operationTaskNo: task.operationTaskNo,
      operationSequence: task.operationSequence,
      operationStatus: task.status,
      workCenterLabel:
        task.workCenterName ?? resolveWorkCenter(task.workCenterCode ?? task.workCenterId),
      skuLabel: resolveSku(order.skuCode ?? order.skuId),
      plannedQuantity: order.quantity,
    }
    reportSheetOpen.value = true
  }
  function canReportOrder(order: Row) {
    return Boolean(order.workOrderId && reportableTask(order)?.operationTaskId)
  }
  function openOrderDetail(order: Row) {
    if (!order.workOrderId) return
    void router.push({ path: `/mes/work-orders/${encodeURIComponent(order.workOrderId)}` })
  }
  // 行内抽屉：工单详情 / 工序 / 齐套 / 派工 / 状态流转一次看全，不跳页。
  function openOrderSheet(order: Row) {
    if (!order.workOrderId) return
    sheetWorkOrderId.value = order.workOrderId
  }
  // 抽屉里点「报工」把工序 id 抛回来，由本页共用的报工弹窗承接（抽屉不自己再开一个）。
  function openReportFromSheet(operationTaskId: string) {
    const order = workOrders.value.find((item) => item.workOrderId === sheetWorkOrderId.value)
    const task = (order?.operationTasks ?? []).find((t) => t.operationTaskId === operationTaskId)
    if (!order?.workOrderId || !task?.operationTaskId) return
    reportContext.value = {
      workOrderId: order.workOrderId,
      workOrderNo: order.workOrderNo,
      operationTaskId: task.operationTaskId,
      operationTaskNo: task.operationTaskNo,
      operationSequence: task.operationSequence,
      operationStatus: task.status,
      workCenterLabel:
        task.workCenterName ?? resolveWorkCenter(task.workCenterCode ?? task.workCenterId),
      skuLabel: resolveSku(order.skuCode ?? order.skuId),
      plannedQuantity: order.quantity,
    }
    sheetWorkOrderId.value = null
    reportSheetOpen.value = true
  }

  function resetFilters() {
    keyword.value = ''
    statusFilter.value = 'all'
    workCenterFilter.value = 'all'
  }

  function formatDateTime(value?: string | null) {
    if (!value) return '无'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
  }
  function formatQuantity(value?: number) {
    return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
  }
  const terminalWorkOrderStatuses = new Set([
    'completed',
    'closed',
    'cancelled',
    'scrapped',
    'split',
    'merged',
  ])
  function isTerminalWorkOrderStatus(status?: string | null) {
    return terminalWorkOrderStatuses.has(status?.toLowerCase() ?? '')
  }
  function newMesIdempotencyKey(scope: string) {
    return `${scope}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
  }
  function toResourceOptions(items: BusinessConsoleResourceItem[]) {
    return items
      .filter((item) => item.active !== false && item.code)
      .map((item) => ({
        label: item.displayName ? `${item.displayName} (${item.code})` : item.code!,
        value: item.code!,
      }))
  }

  return {
    canCreateRush,
    canReportOrder,
    canSubmitRelease,
    clearReleaseIntent,
    columns,
    createRushWorkOrderPending,
    displayMode,
    formatDateTime,
    formatQuantity,
    isTerminalWorkOrderStatus,
    keyword,
    listErrorMessage,
    mergeButtonDisabled,
    mergeButtonTitle,
    mergeDialogOpen,
    mergeError,
    mergeIdempotencyKey,
    mergePending,
    mergeResult,
    mergeSources,
    mergeState,
    mergeUnitUnavailable,
    openMergeDialog,
    openOrderDetail,
    openOrderSheet,
    openReleaseDialog,
    openReport,
    openReportFromSheet,
    openSchedule,
    operationTasksPending,
    orderUrgencies,
    page,
    pageSize,
    pagedWorkOrders,
    productionVersionLabel,
    productionVersionsPending,
    refreshUrgency,
    refreshWorkOrders,
    releaseBlocker,
    releaseDialogOpen,
    releaseIntent,
    releaseIntentOrder,
    releasePreflightPending,
    releaseRetroactiveNotice,
    releaseValidationMessage,
    releaseWarningsConfirmed,
    releaseWorkOrderPending,
    reportContext,
    reportSheetOpen,
    resetFilters,
    resolveSku,
    resolveWorkCenter,
    retryMergeReadback,
    router,
    rowKey,
    rushForm,
    rushOperationTaskOptions,
    rushSheetOpen,
    rushVersionOptions,
    scheduleDisabledReason,
    scheduleOpen,
    scheduleTarget,
    selectedWorkOrderIds,
    sheetWorkOrderId,
    sort,
    statusFilter,
    statusLabel,
    statusOptions,
    submitMerge,
    submitReleaseWorkOrder,
    submitRushWorkOrder,
    workCenterFilter,
    workCenterOptions,
    workOrderReadScopeMessage,
    workOrdersError,
    workOrdersPending,
    workOrdersTotal,
  }
}
