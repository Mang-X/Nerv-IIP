<script setup lang="ts">
import type {
  BusinessConsoleCreateRushWorkOrderRequest,
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvDataTableSort } from '@nerv-iip/ui'
import { mesWorkOrderStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { mesWorkOrderReleaseBlocker } from '@/composables/mes/workOrderRelease'
import {
  useBusinessMasterDataResources,
  useBusinessSkus,
} from '@/composables/useBusinessMasterData'
import {
  useMesOperationTasks,
  useMesWorkOrderTransformations,
  useMesWorkOrders,
  type MesWorkOrderTransformationResult,
} from '@/composables/useBusinessMes'
import WorkOrderTransformationDialog, {
  type MergeTransformationSubmit,
  type SplitTransformationSubmit,
  type WorkOrderTransformationState,
} from '@/components/mes/WorkOrderTransformationDialog.vue'
import {
  isTransformationConflict,
  type WorkOrderTransformationSource,
} from '@/composables/mes/workOrderTransformation'
import { toBaseUomBySku } from '@/composables/skuBaseUom'
import { useMesMaterialVersionCatalog } from '@/composables/useMesPickerCatalog'
import { useOrderUrgencies } from '@/composables/useOrderUrgency'
import {
  DEFAULT_URGENCY_DISPLAY_MODE,
  orderRowsByUrgency,
  type UrgencyDisplayMode,
} from '@/composables/useUrgencyDisplayMode'
import MesWorkScopeSelect from '@/components/mes/MesWorkScopeSelect.vue'
import ProductionReportDialog from '@/components/mes/ProductionReportDialog.vue'
import WorkOrderDetailSheet from '@/components/mes/WorkOrderDetailSheet.vue'
import ListScopeMeta from '@/components/business/ListScopeMeta.vue'
import type { ProductionReportContext } from '@/composables/mes/useProductionReportForm'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import UrgencyDisplayModeSelect from '@/components/urgency/UrgencyDisplayModeSelect.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvCheckbox,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import {
  CalendarCheckIcon,
  CalendarCogIcon,
  ClipboardCheckIcon,
  ExternalLinkIcon,
  EyeIcon,
  FactoryIcon,
  LockIcon,
  RefreshCwIcon,
} from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单与派工',
    requiredPermissions: ['business.mes.work-orders.read'],
  },
})

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
  workOrdersHasFailedResponse,
  workOrdersHasSuccessfulResponse,
  workOrdersLastUpdatedAt,
  workOrdersPending,
  workOrdersTotal,
  workOrderReadScope,
  workOrderReadScopeMessage,
  workOrderReadScopeReady,
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
const rushForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  skuId: '',
  productionVersionId: '',
  quantity: '1',
  dueUtc: toLocalDateTimeInput(new Date(Date.now() + 86_400_000)),
  workCenterId: '',
  operationTaskId: '',
  operationSequence: '10',
  durationMinutes: '60',
  idempotencyKey: newMesIdempotencyKey('rush-work-order'),
})

// 报工对象由所选工单行带出（工单 + 该工单的首道可报工序），弹窗不提供任何挑选入口。
const reportContext = shallowRef<ProductionReportContext | null>(null)

const listErrorMessage = computed(() => formatError(workOrdersError.value))
const workScopeKindLabels: Record<string, string> = {
  self: '本人',
  team: '班组',
  'work-center': '工作中心',
  workshop: '车间',
  organization: '组织',
}
const workOrderScopeLabel = computed(() => {
  const selectedScope = workOrderReadScope.value
  if (!selectedScope) return '当前主体授权工单范围未就绪'
  const kind = workScopeKindLabels[selectedScope.kind] ?? selectedScope.kind
  return `当前主体授权工单范围 · ${selectedScope.displayName || selectedScope.id}（${kind}）`
})
const workOrderEmptyExplanation = computed(() =>
  workOrderReadScopeReady.value
    ? '当前主体授权工单范围内暂无生产工单。'
    : workOrderReadScopeMessage.value || '尚未取得当前主体的授权工单范围，未发起查询。',
)

const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const skuOptions = computed(() => toResourceOptions(skus.value))

// ── 急单表单的四个选择器 ────────────────────────────────────────
// 物料 ▸ 生产版本 从属，工作中心 ▸ 工序任务 从属：上游变了清空下游。
const { productionVersionOptions, productionVersionsPending } = useMesMaterialVersionCatalog()
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

type ReleaseIntent = {
  idempotencyKey: string
  workOrderId: string
  workOrderLabel: string
}

const releaseIntent = shallowRef<ReleaseIntent | null>(null)
const releaseWarningsConfirmed = shallowRef(false)
const releasePreflightPending = shallowRef(false)
const releaseDialogOpen = computed({
  get: () => releaseIntent.value !== null,
  set: (open: boolean) => {
    if (!open && !releaseWorkOrderPending.value) clearReleaseIntent()
  },
})
const releaseIntentOrder = computed(() => {
  const intent = releaseIntent.value
  return intent
    ? (workOrders.value.find((order) => order.workOrderId === intent.workOrderId) ?? null)
    : null
})
const releaseValidationMessage = computed(() => {
  if (!releaseIntent.value) return ''
  if (!releaseIntentOrder.value) return '工单已不在当前主体授权工单范围，请刷新后重试。'
  return releaseBlocker(releaseIntentOrder.value) ?? ''
})
const canSubmitRelease = computed(
  () =>
    releaseIntent.value !== null &&
    releaseWarningsConfirmed.value &&
    !releaseValidationMessage.value &&
    !releaseWorkOrderPending.value,
)

function releaseBlocker(order: Parameters<typeof mesWorkOrderReleaseBlocker>[0]) {
  if (!canManageWorkOrders.value) return '没有工单下达权限'
  if (workOrderManageScopePending.value) return '正在确认主体授权工单范围'
  if (!workOrderManageScopeReady.value) {
    return workOrderManageScopeMessage.value || '主体授权工单范围未就绪'
  }
  return mesWorkOrderReleaseBlocker(order)
}

async function openReleaseDialog(order: Row) {
  if (releaseBlocker(order) || !order.workOrderId) return
  releasePreflightPending.value = true
  try {
    const latest = await readWorkOrderForRelease(order.workOrderId)
    const blocker = releaseBlocker(latest)
    if (blocker) throw new Error(blocker)
    releaseIntent.value = {
      idempotencyKey: newMesIdempotencyKey(`release-work-order-${order.workOrderId}`),
      workOrderId: order.workOrderId,
      workOrderLabel: order.workOrderNo || order.workOrderId,
    }
    releaseWarningsConfirmed.value = false
  } catch (error) {
    notifyOperationFailure(
      '工单下达前置检查失败',
      error,
      '未能在当前管理范围内确认工单下达条件，请检查授权范围和就绪状态后重试。',
    )
  } finally {
    releasePreflightPending.value = false
  }
}

function clearReleaseIntent() {
  releaseIntent.value = null
  releaseWarningsConfirmed.value = false
}

async function submitReleaseWorkOrder() {
  const intent = releaseIntent.value
  const order = releaseIntentOrder.value
  if (!intent || !order || !canSubmitRelease.value) return

  try {
    const response = await releaseWorkOrder(intent.workOrderId, {
      organizationId: filters.organizationId.trim(),
      environmentId: filters.environmentId.trim(),
      confirmWarnings: true,
      idempotencyKey: intent.idempotencyKey,
    })
    if (response?.data?.accepted !== true) {
      throw new Error('工单下达结果未确认，请刷新列表核实后再重试。')
    }

    // 服务端已确认受理后不再保留可重试入口，避免刷新失败导致重复提交；两个权威读面都显式刷新。
    clearReleaseIntent()
    const refreshResults = await Promise.allSettled([refreshWorkOrders(), refreshOperationTasks()])
    notifySuccess(`工单 ${intent.workOrderLabel} 已下达。`)
    const refreshFailure = refreshResults.find(
      (result): result is PromiseRejectedResult => result.status === 'rejected',
    )
    if (refreshFailure) {
      notifyOperationFailure(
        '工单已下达，但状态刷新失败',
        refreshFailure.reason,
        '工单已下达，但最新状态刷新失败，请手动刷新列表。',
      )
    }
  } catch (error) {
    notifyOperationFailure(
      '工单下达失败',
      error,
      '工单下达失败，请根据服务端原因检查就绪条件后重试。',
    )
  }
}

const rushVersionOptions = computed(() => productionVersionOptions(rushForm.skuId))
const rushOperationTaskOptions = computed(() => {
  const workCenter = rushForm.workCenterId.trim()
  return operationTasks.value
    .filter((task) => !!task.operationTaskId)
    .filter((task) => !workCenter || task.workCenterCode === workCenter)
    .map((task) => ({
      value: task.operationTaskId as string,
      label: task.operationTaskNo || task.operationCode || '未编号工序任务',
      hint: [task.workOrderNo, task.workCenterName ?? task.workCenterCode]
        .filter(Boolean)
        .join(' · '),
    }))
})

watch(
  () => rushForm.skuId,
  () => {
    rushForm.productionVersionId = ''
  },
)
watch(
  () => rushForm.workCenterId,
  () => {
    rushForm.operationTaskId = ''
  },
)

const visibleWorkOrders = computed(() => workOrders.value)

const canCreateRush = computed(
  () =>
    isNonEmpty(rushForm.organizationId) &&
    isNonEmpty(rushForm.environmentId) &&
    isNonEmpty(rushForm.skuId) &&
    toOptionalNumber(rushForm.quantity) !== undefined &&
    isNonEmpty(rushForm.dueUtc) &&
    isNonEmpty(rushForm.workCenterId) &&
    toOptionalNumber(rushForm.durationMinutes) !== undefined,
)

// --- Sort (page-owned, before pagination) ---
// 默认无列排序：按统一紧急度（等级→CR→预计延迟→due→等待）排序；用户点列头后按列排序。
// 后端分页，紧急度排序仅对当前页行生效；跨页排序需后端支持（已知契约限制）。
const sort = ref<NvDataTableSort | null>(null)
function sortValue(order: Row, key: string): string | number {
  if (key === 'quantity') return order.quantity ?? 0
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

const columns: NvDataTableColumn<Row>[] = [
  { key: 'workOrderId', header: '工单', sortable: true, cellClass: 'font-medium' },
  { key: 'status', header: '状态', sortable: true, width: 'w-24' },
  { key: 'urgency', header: '紧急度', width: 'w-28' },
  {
    key: 'quantity',
    header: '数量',
    align: 'end',
    sortable: true,
    width: 'w-24',
    accessor: (r) => r.quantity ?? 0,
  },
  {
    key: 'dueUtc',
    header: '交期',
    sortable: true,
    width: 'w-44',
    accessor: (r) => (r.dueUtc ? new Date(r.dueUtc).getTime() : 0),
  },
  {
    key: 'operationCount',
    header: '工序',
    sortable: true,
    accessor: (r) => r.operationTasks?.length ?? 0,
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function rowKey(order: Row) {
  return order.workOrderId ?? `${order.skuId ?? 'wo'}-${order.dueUtc ?? ''}`
}

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

async function submitRushWorkOrder() {
  if (!canCreateRush.value) return
  const body: BusinessConsoleCreateRushWorkOrderRequest = {
    organizationId: rushForm.organizationId.trim(),
    environmentId: rushForm.environmentId.trim(),
    skuId: rushForm.skuId.trim(),
    productionVersionId: optionalText(rushForm.productionVersionId),
    quantity: toOptionalNumber(rushForm.quantity),
    dueUtc: toIsoFromLocalInput(rushForm.dueUtc),
    workCenterId: rushForm.workCenterId.trim(),
    operationTaskId: optionalText(rushForm.operationTaskId),
    operationSequence: toOptionalInteger(rushForm.operationSequence),
    durationMinutes: toOptionalInteger(rushForm.durationMinutes),
    idempotencyKey: rushForm.idempotencyKey,
  }
  try {
    const response = await createRushWorkOrder(body)
    const affected = response?.data?.affectedWorkOrderIds ?? []
    // 排程反馈是结果、不是常驻说明：随成功 toast 一次说清，弹窗即关，不在表单里堆一块「反馈区」。
    notifySuccess(
      `已创建急单 ${response?.data?.workOrderId ?? ''}` +
        (affected.length ? ` · 重排影响 ${affected.length} 个在制工单` : ''),
    )
    rushForm.idempotencyKey = newMesIdempotencyKey('rush-work-order')
    rushSheetOpen.value = false
  } catch (error) {
    notifyOperationFailure(
      '创建急单失败',
      createRushWorkOrderError.value ?? error,
      '创建急单失败，请稍后重试。',
    )
  }
}

function resetFilters() {
  keyword.value = ''
  statusFilter.value = 'all'
  workCenterFilter.value = 'all'
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function toOptionalInteger(value: string) {
  const parsed = Number.parseInt(value, 10)
  return Number.isFinite(parsed) ? parsed : undefined
}
function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}
function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatQuantity(value?: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatStatus(value?: string | null) {
  const map: Record<string, string> = {
    blocked: '阻塞',
    closed: '已关闭',
    completed: '已完成',
    created: '新建',
    hold: '暂停',
    inprogress: '执行中',
    queued: '排队中',
    ready: '可开工',
    released: '已下达',
    running: '执行中',
    started: '已开工',
  }
  return value ? (map[value.toLowerCase()] ?? value) : '未知'
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
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工单与派工"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${workOrdersTotal} 个工单`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" @click="router.push('/mes/plans')">
          <CalendarCheckIcon aria-hidden="true" />
          生产计划
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" @click="rushSheetOpen = true">
          <FactoryIcon aria-hidden="true" />
          创建急单
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="workOrdersPending"
          @click="refreshWorkOrders"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <ListScopeMeta
      :scope="workOrderScopeLabel"
      source="生产工单服务（服务端按当前主体与所选授权工单范围过滤）"
      :loaded="workOrders.length"
      :total="workOrdersTotal"
      :updated-at="workOrdersLastUpdatedAt"
      :empty="
        !workOrderReadScopeReady ||
        (workOrdersHasSuccessfulResponse && !workOrdersError && workOrders.length === 0)
      "
      :failed="workOrdersHasFailedResponse || Boolean(workOrdersError)"
      failure-explanation="生产工单服务未成功返回，请重试。"
      :empty-explanation="workOrderEmptyExplanation"
    />
    <p
      v-if="workOrderReadScopeMessage"
      class="text-sm text-destructive"
      role="alert"
      data-testid="work-order-read-scope-message"
    >
      {{ workOrderReadScopeMessage }}
    </p>

    <NvToolbar v-model:search="keyword" search-placeholder="搜索工单、物料、生产版本">
      <template #filters>
        <MesWorkScopeSelect permission-code="business.mes.work-orders.read" />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="工单状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in statusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="workCenterFilter">
          <NvSelectTrigger class="h-9 w-40" aria-label="工作中心"
            ><NvSelectValue placeholder="全部工作中心"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部工作中心</NvSelectItem>
            <NvSelectItem
              v-for="option in workCenterOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
      <template #actions>
        <UrgencyDisplayModeSelect v-model="displayMode" />
        <NvButton type="button" variant="ghost" size="sm" @click="resetFilters">重置</NvButton>
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="workOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedWorkOrders"
      :row-key="rowKey"
      :client-sort="false"
      :loading="workOrdersPending"
      :error="workOrdersError"
      :error-message="listErrorMessage"
      empty-message="当前筛选下没有工单。正常生产请先进入生产计划转工单，急单只处理临时插单。"
      :searchable="false"
      :column-settings="false"
      selectable
      v-model:selected="selectedWorkOrderIds"
      @retry="refreshWorkOrders"
    >
      <template #bulk-actions>
        <NvButton
          type="button"
          size="sm"
          variant="outline"
          :disabled="mergeButtonDisabled"
          :title="mergeButtonTitle"
          data-testid="open-merge-work-orders"
          @click="openMergeDialog"
        >
          合并选中工单
        </NvButton>
        <span
          v-if="mergeUnitUnavailable"
          class="text-xs text-destructive"
          role="alert"
          data-testid="merge-unit-unavailable"
        >
          选中的工单未返回单位信息，无法确认数量单位；请刷新列表后重试。
        </span>
      </template>
      <template #cell-workOrderId="{ row }">
        <RouterLink
          v-if="row.workOrderId"
          :to="`/mes/work-orders/${encodeURIComponent(row.workOrderId)}`"
          class="flex flex-col gap-0.5 text-left"
        >
          <span class="font-medium text-brand underline-offset-4 hover:underline">{{
            row.workOrderNo ?? row.workOrderId
          }}</span>
          <span class="text-xs text-muted-foreground">{{
            resolveSku(row.skuCode ?? row.skuId) ?? '无'
          }}</span>
        </RouterLink>
        <div v-else class="flex flex-col gap-0.5">
          <span class="font-medium text-muted-foreground">无编号</span>
          <span class="text-xs text-muted-foreground">{{
            resolveSku(row.skuCode ?? row.skuId) ?? '无'
          }}</span>
        </div>
      </template>
      <template #cell-status="{ row }">
        <div class="flex items-center gap-1.5">
          <NvStatusBadge :value="row.status" />
          <!-- 质量保留锁定标记：与工单生命周期状态无关，来源为活跃 quality hold（#886）。 -->
          <LockIcon
            v-if="row.hasActiveQualityHold"
            class="size-3.5 text-destructive"
            aria-label="存在有效质量保留"
            title="存在有效质量保留，需处理后才能放行或开工"
          />
        </div>
      </template>
      <template #cell-urgency="{ row }">
        <OrderUrgencyBadge
          :order-reference="row.workOrderId ?? ''"
          :mode="displayMode"
          :urgency="
            row.workOrderId ? orderUrgencies.byReference.value.get(row.workOrderId) : undefined
          "
          :source-unavailable="orderUrgencies.error?.value != null"
          @refresh="refreshUrgency"
        />
      </template>
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template
      >
      <template #cell-dueUtc="{ row }">{{ formatDateTime(row.dueUtc) }}</template>
      <template #cell-operationCount="{ row }">
        <div class="grid gap-1">
          <!-- 一行四段斜杠拼接读不出主次：第一行给「第几道 · 在哪做」，第二行给单号与状态。 -->
          <div
            v-for="task in row.operationTasks ?? []"
            :key="task.operationTaskId ?? `${row.workOrderId}-${task.operationSequence}`"
            class="grid gap-0.5"
          >
            <span class="text-xs font-medium text-foreground">
              第 {{ task.operationSequence ?? '—' }} 道 ·
              {{
                task.workCenterName ??
                resolveWorkCenter(task.workCenterCode ?? task.workCenterId) ??
                '未指定工作中心'
              }}
            </span>
            <span class="text-xs text-muted-foreground">
              {{ task.operationTaskNo ?? task.operationTaskId ?? '未生成任务' }} ·
              {{ formatStatus(task.status) }}
            </span>
          </div>
          <span v-if="!row.operationTasks?.length" class="text-xs text-muted-foreground"
            >暂无工序任务</span
          >
        </div>
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`工单操作 ${row.workOrderId ?? ''}`">
          <!-- 详情 / 工序 / 齐套 / 派工 / 状态流转全部收进行内抽屉，不再逐个跳页丢上下文。 -->
          <NvDropdownMenuItem :disabled="!row.workOrderId" @click="openOrderSheet(row)">
            <EyeIcon aria-hidden="true" />
            工单详情与工序
          </NvDropdownMenuItem>
          <NvDropdownMenuItem :disabled="!canReportOrder(row)" @click="openReport(row)">
            <ClipboardCheckIcon aria-hidden="true" />
            {{ canReportOrder(row) ? '生产报工' : '暂无工序，不能报工' }}
          </NvDropdownMenuItem>
          <NvDropdownMenuItem
            :aria-label="`下达工单 ${row.workOrderNo || row.workOrderId || ''}`"
            :disabled="releasePreflightPending || Boolean(releaseBlocker(row))"
            :title="
              releasePreflightPending ? '正在确认下达条件' : (releaseBlocker(row) ?? '下达当前工单')
            "
            @click="openReleaseDialog(row)"
          >
            <FactoryIcon aria-hidden="true" />
            {{ releaseBlocker(row) ? `不能下达：${releaseBlocker(row)}` : '下达工单' }}
          </NvDropdownMenuItem>
          <NvDropdownMenuSeparator />
          <NvDropdownMenuItem :disabled="!row.workOrderId" @click="openOrderDetail(row)">
            <ExternalLinkIcon aria-hidden="true" />
            打开完整详情页
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <WorkOrderTransformationDialog
      v-model:open="mergeDialogOpen"
      mode="merge"
      :state="mergeState"
      :pending="mergePending"
      :sources="mergeSources"
      :result="mergeResult"
      :error-message="mergeError"
      :idempotency-key="mergeIdempotencyKey"
      @submit="submitMerge"
      @retry-readback="retryMergeReadback"
    />

    <NvDialog v-model:open="releaseDialogOpen">
      <NvDialogContent class="sm:max-w-lg">
        <NvDialogHeader>
          <NvDialogTitle>确认下达工单</NvDialogTitle>
          <NvDialogDescription>
            下达后，服务端会再次校验物料、设备、质量与工序就绪条件；未通过时不会视为成功。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitReleaseWorkOrder">
          <dl v-if="releaseIntentOrder" class="grid gap-2 rounded-md border p-3 text-sm">
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">工单</dt>
              <dd class="font-medium">{{ releaseIntent?.workOrderLabel }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">当前状态</dt>
              <dd>{{ formatStatus(releaseIntentOrder.status) }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">生产版本</dt>
              <dd>{{ releaseIntentOrder.productionVersionId }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">工序任务</dt>
              <dd>{{ releaseIntentOrder.operationTasks?.length ?? 0 }} 道</dd>
            </div>
          </dl>
          <p
            v-if="releaseValidationMessage"
            class="text-sm text-destructive"
            role="alert"
            data-testid="release-validation-message"
          >
            {{ releaseValidationMessage }}
          </p>
          <label class="flex items-start gap-2 text-sm">
            <NvCheckbox
              v-model="releaseWarningsConfirmed"
              :disabled="releaseWorkOrderPending"
              aria-label="确认已核对工单下达警告"
            />
            <span>我已核对当前工单、生产版本与工序信息，并确认继续执行服务端就绪检查。</span>
          </label>
          <NvDialogFooter>
            <NvButton
              type="button"
              variant="outline"
              :disabled="releaseWorkOrderPending"
              @click="clearReleaseIntent"
            >
              取消
            </NvButton>
            <NvButton type="submit" :disabled="!canSubmitRelease">
              <Spinner v-if="releaseWorkOrderPending" aria-hidden="true" />
              确认下达
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="rushSheetOpen">
      <NvDialogContent class="sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>创建急单</NvDialogTitle>
          <NvDialogDescription class="sr-only"
            >临时插单，填写物料、数量、交期与工作中心后提交。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitRushWorkOrder">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="rush-sku"
                >物料 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvEntityPicker
                id="rush-sku"
                v-model="rushForm.skuId"
                :options="skuOptions"
                title="选择物料"
                placeholder="选择物料"
                source-text="数据来自基础数据物料主数据"
                empty-text="暂无物料，请先在基础数据维护"
                aria-label="物料"
                clearable
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-version">生产版本</NvFieldLabel>
              <NvEntityPicker
                id="rush-version"
                v-model="rushForm.productionVersionId"
                :options="rushVersionOptions"
                title="选择生产版本"
                :placeholder="rushForm.skuId ? '可留空，按生效日自动解析' : '先选物料'"
                :disabled="!rushForm.skuId"
                source-text="仅列所选物料的生产版本"
                empty-text="该物料暂无生产版本，请先在工程数据维护"
                :loading="productionVersionsPending"
                aria-label="生产版本"
                clearable
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-quantity"
                >数量 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="rush-quantity"
                v-model="rushForm.quantity"
                inputmode="decimal"
                required
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-due"
                >交期 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput id="rush-due" v-model="rushForm.dueUtc" required type="datetime-local" />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-work-center"
                >工作中心 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvEntityPicker
                id="rush-work-center"
                v-model="rushForm.workCenterId"
                :options="workCenterOptions"
                title="选择工作中心"
                placeholder="选择工作中心"
                source-text="数据来自基础数据工作中心主数据"
                empty-text="暂无工作中心，请先在基础数据维护"
                aria-label="工作中心"
                clearable
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-operation-task">工序任务</NvFieldLabel>
              <NvEntityPicker
                id="rush-operation-task"
                v-model="rushForm.operationTaskId"
                :options="rushOperationTaskOptions"
                title="选择工序任务"
                :placeholder="rushForm.workCenterId ? '可留空' : '先选工作中心'"
                :disabled="!rushForm.workCenterId"
                source-text="仅列所选工作中心的在办工序任务"
                empty-text="该工作中心暂无在办工序任务"
                :loading="operationTasksPending"
                aria-label="工序任务"
                clearable
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-operation-sequence">工序序号</NvFieldLabel>
              <NvInput
                id="rush-operation-sequence"
                v-model="rushForm.operationSequence"
                inputmode="numeric"
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rush-duration"
                >工时分钟 <span class="text-destructive">*</span></NvFieldLabel
              >
              <NvInput
                id="rush-duration"
                v-model="rushForm.durationMinutes"
                inputmode="numeric"
                required
                type="number"
              />
            </NvField>
          </NvFieldGroup>

          <NvDialogFooter class="sm:justify-between">
            <NvButton size="sm" type="button" variant="ghost" as-child>
              <RouterLink to="/scheduling"
                ><CalendarCogIcon aria-hidden="true" />排产工作台</RouterLink
              >
            </NvButton>
            <div class="flex gap-2">
              <NvButton type="button" variant="outline" @click="rushSheetOpen = false"
                >取消</NvButton
              >
              <NvButton type="submit" :disabled="createRushWorkOrderPending || !canCreateRush">
                <Spinner v-if="createRushWorkOrderPending" aria-hidden="true" />
                <FactoryIcon v-else aria-hidden="true" />
                创建急单
              </NvButton>
            </div>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <!-- 报工「带出式录入」样板：上下文只能由所选工单行带出，弹窗自身不提供工单/工序挑选入口。 -->
    <ProductionReportDialog
      v-model:open="reportSheetOpen"
      :context="reportContext"
      @reported="refreshWorkOrders"
    />

    <WorkOrderDetailSheet v-model:work-order-id="sheetWorkOrderId" @report="openReportFromSheet" />
  </BusinessLayout>
</template>
