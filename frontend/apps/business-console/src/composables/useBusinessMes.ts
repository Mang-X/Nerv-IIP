import {
  acceptBusinessConsoleMesShiftHandoverMutationOptions,
  assignBusinessConsoleMesDispatchTaskMutationOptions,
  cancelBusinessConsoleMesWorkOrder,
  completeBusinessConsoleMesOperationTask,
  confirmBusinessConsoleMesDowntimeRecoveryMutationOptions,
  convertBusinessConsoleMesPlanToWorkOrderMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions,
  forceReleaseBusinessConsoleMesQualityHoldMutationOptions,
  getBusinessConsoleMesQualityHoldTimelineQueryOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  createBusinessConsoleMesShiftHandoverMutationOptions,
  createBusinessConsoleSopFileDownloadGrantMutationOptions,
  getBusinessConsoleMesBatchTraceabilityQueryOptions,
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions,
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  getBusinessConsoleMesFoundationReadinessQueryOptions,
  getBusinessConsoleMesMaterialReadinessQueryOptions,
  getBusinessConsoleMesOverviewQueryOptions,
  getBusinessConsoleMesProductionPlanReadinessQueryOptions,
  getBusinessConsoleMesProductionReportQueryOptions,
  getBusinessConsoleMesWipSummaryQueryOptions,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequests,
  listBusinessConsoleMesMaterialIssueRequests,
  listBusinessConsoleMesDispatchTasksQueryOptions,
  listBusinessConsoleMesDowntimeEventsQueryOptions,
  listBusinessConsoleMesCapacityImpactsQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesOperationTasks,
  listBusinessConsoleMesProductionPlansQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesReceivableProducedLotsQueryOptions,
  listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions,
  promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  listBusinessConsoleMesRelatedQualityItemsQueryOptions,
  listBusinessConsoleMesScheduleResultsQueryOptions,
  listBusinessConsoleMesShiftHandoversQueryOptions,
  pauseBusinessConsoleMesOperationTask,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesDefectMutationOptions,
  recordBusinessConsoleMesDowntimeEventMutationOptions,
  recordBusinessConsoleMesProductionReport,
  releaseBusinessConsoleMesWorkOrderMutationOptions,
  resumeBusinessConsoleMesOperationTask,
  reverseBusinessConsoleMesProductionReportMutationOptions,
  runBusinessConsoleMesScheduleMutationOptions,
  startBusinessConsoleMesOperationTask,
  type BusinessConsoleMesCapacityImpactListEnvelope,
  type BusinessConsoleMesCapacityImpactRow,
  type BusinessConsoleMesCreateMaterialIssueRequest,
  type BusinessConsoleMesCreateReceiptRequest,
  type BusinessConsoleMesDispatchTaskListEnvelope,
  type BusinessConsoleMesDispatchTaskRow,
  type BusinessConsoleMesDowntimeEventListEnvelope,
  type BusinessConsoleMesDowntimeEventRow,
  type BusinessConsoleMesFoundationReadinessEnvelope,
  type BusinessConsoleMesMaterialIssueRequestListEnvelope,
  type BusinessConsoleMesMaterialIssueRequestRow,
  type BusinessConsoleMesMaterialReadinessEnvelope,
  type BusinessConsoleCurrentSopDocumentItem,
  type BusinessConsoleCurrentSopDocumentsEnvelope,
  type BusinessConsoleSopFileDownloadGrantEnvelope,
  type BusinessConsoleSopFileDownloadGrantResponse,
  type BusinessConsoleMesOperationTaskActionRequest,
  type BusinessConsoleMesOperationTaskListEnvelope,
  type BusinessConsoleMesOperationTaskRow,
  type BusinessConsoleMesOverviewEnvelope,
  type BusinessConsoleMesProductionPlanListEnvelope,
  type BusinessConsoleMesProductionPlanRow,
  type BusinessConsoleMesProductionReportListEnvelope,
  type BusinessConsoleMesReceivableProducedLotListEnvelope,
  type BusinessConsoleMesReceivableProducedLotRow,
  type BusinessConsoleMesProductionReportDetailEnvelope,
  type BusinessConsoleMesProductionReportDetailResponse,
  type BusinessConsoleMesProductionReportRow,
  type BusinessConsoleMesTelemetryCandidateRow,
  type BusinessConsoleMesRecordDefectRequest,
  type BusinessConsoleMesRecordDowntimeEventRequest,
  type BusinessConsoleMesRelatedQualityItemListEnvelope,
  type BusinessConsoleMesRelatedQualityItemRow,
  type BusinessConsoleMesReceiptRequestListEnvelope,
  type BusinessConsoleMesReceiptRequestRow,
  type BusinessConsoleMesQualityHoldTimelineItem,
  type BusinessConsoleMesWorkOrderQualityHoldSummary,
  type BusinessConsoleMesCreateShiftHandoverRequest,
  type BusinessConsoleMesShiftHandoverListEnvelope,
  type BusinessConsoleMesShiftHandoverRow,
  type BusinessConsoleMesTraceabilityEnvelope,
  type BusinessConsoleMesTraceabilityResponse,
  type BusinessConsoleCreateRushWorkOrderRequest,
  type BusinessConsoleMesScheduleEnvelope,
  type BusinessConsoleMesScheduleResult,
  type BusinessConsoleMesScheduleResultListEnvelope,
  type BusinessConsoleMesScheduleResultRow,
  type BusinessConsoleMesWipSummaryEnvelope,
  type BusinessConsoleMesWipSummaryRow,
  type BusinessConsoleMesWorkOrderDetailEnvelope,
  type BusinessConsoleMesWorkOrderDetailResponse,
  type BusinessConsoleMesWorkOrderItem,
  type BusinessConsoleMesWorkOrderListEnvelope,
  type BusinessConsoleRecordProductionReportRequest,
  type BusinessConsoleRunScheduleRequest,
  type ListBusinessConsoleMesWorkOrdersData,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
  type BusinessContextFields,
} from './businessContextBinding'
import { businessReadState } from './businessReadState'
import { executeLifecycleAction, LifecycleStateChangedError } from './lifecycleAction'

const DEFAULT_TAKE = 100
// 取消补偿预览按此页大小完整分页，直到取全该工单的全部关联单据（取消 handler 处理全部）。
const CANCEL_PREVIEW_PAGE_SIZE = 200

// 逐页拉取直到最后一页（返回不足一页即结束），累计全部行。任一页 fetch 失败（throwOnError:true）会向上抛，
// 由 useQuery.error 捕获，从而让补偿预览走失败门禁（不允许在不完整数据上确认取消）。
async function fetchAllCompensationItems<TRow>(
  fetchPage: (skip: number, take: number) => Promise<TRow[]>,
): Promise<TRow[]> {
  const items: TRow[] = []
  let skip = 0
  for (;;) {
    const page = await fetchPage(skip, CANCEL_PREVIEW_PAGE_SIZE)
    items.push(...page)
    if (page.length < CANCEL_PREVIEW_PAGE_SIZE) {
      break
    }
    skip += page.length
  }
  return items
}

type MesListStatus = NonNullable<
  NonNullable<ListBusinessConsoleMesWorkOrdersData['query']>['status']
>

export interface MesReadinessReasonDisplay {
  code: string
  label: string
  nextStep: string
}

const mesReadinessReasonDisplays: Record<string, MesReadinessReasonDisplay> = {
  QUALITY_PLAN_MISSING: {
    code: 'QUALITY_PLAN_MISSING',
    label: '检验方案缺失',
    nextStep: '维护并启用 SKU 与工序检验方案后重新检查',
  },
  QUALITY_HOLD_ACTIVE: {
    code: 'QUALITY_HOLD_ACTIVE',
    label: '质量冻结中',
    nextStep: '处理质量冻结、NCR 或放行状态后再执行',
  },
  EQUIPMENT_UNAVAILABLE: {
    code: 'EQUIPMENT_UNAVAILABLE',
    label: '设备不可用',
    nextStep: '处理报警/停机或改派可用设备',
  },
  EQUIPMENT_MAINTENANCE_CONFLICT: {
    code: 'EQUIPMENT_MAINTENANCE_CONFLICT',
    label: '维修占用冲突',
    nextStep: '调整维修窗口、等待释放或选择替代设备',
  },
  SOURCE_SERVICE_UNAVAILABLE: {
    code: 'SOURCE_SERVICE_UNAVAILABLE',
    label: '来源服务不可用',
    nextStep: '稍后重试或联系管理员检查来源服务',
  },
}

export function describeMesReadinessReason(reason: string): MesReadinessReasonDisplay {
  const trimmedReason = reason.trim()
  const code = trimmedReason.split(':', 1)[0]?.trim() || trimmedReason
  return (
    mesReadinessReasonDisplays[code] ?? {
      code,
      label: trimmedReason,
      nextStep: '查看阻塞详情并按来源业务页面处理',
    }
  )
}

export interface MesListFilters {
  organizationId: string
  environmentId: string
  status?: string
  keyword?: string
  workCenterId?: string
  shiftId?: string
  deviceAssetId?: string
  /** 受派工人（派工看板按人筛选负荷用；facade 侧仅派工列表支持）。 */
  assignedUserId?: string
  source?: string
  readinessStatus?: string
  skip: number
  take: number
}

export interface MesFoundationReadinessFilters {
  organizationId: string
  environmentId: string
  siteCode?: string
  lineCode?: string
  workCenterCode?: string
  skuId?: string
  productionVersionId?: string
  plannedStartUtc?: string
  plannedEndUtc?: string
}

export interface MesWorkOrderContext {
  organizationId: string
  environmentId: string
  workOrderId: string
}

export interface MesContextFilters extends BusinessContextFields {
  workOrderId?: string
}

export interface MesTraceabilityFilters extends MesContextFilters {
  workOrderId: string
  batchOrSerial: string
  materialLotId: string
  mode: 'work-order' | 'batch' | 'material-lot'
}

function defaultFilters(initialTake = DEFAULT_TAKE): MesListFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: initialTake,
    }),
  )
}

function defaultContext(): MesContextFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
    }),
  )
}

function defaultFoundationFilters(): MesFoundationReadinessFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
    }),
  )
}

function defaultWorkOrderContext(): MesWorkOrderContext {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      workOrderId: '',
    }),
  )
}

function defaultTraceabilityFilters(): MesTraceabilityFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      workOrderId: '',
      batchOrSerial: '',
      materialLotId: '',
      mode: 'work-order',
    }),
  )
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function toContextQuery(filters: MesContextFilters | MesWorkOrderContext) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
  }
}

function isNonEmpty(value: string | undefined) {
  return value !== undefined && value.trim().length > 0
}

function toFoundationQuery(filters: MesFoundationReadinessFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('siteCode', filters.siteCode),
    ...optionalQuery('lineCode', filters.lineCode),
    ...optionalQuery('workCenterCode', filters.workCenterCode),
    ...optionalQuery('skuId', filters.skuId),
    ...optionalQuery('productionVersionId', filters.productionVersionId),
    ...optionalQuery('plannedStartUtc', filters.plannedStartUtc),
    ...optionalQuery('plannedEndUtc', filters.plannedEndUtc),
  }
}

function toListQuery(filters: MesListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('status', filters.status as MesListStatus | undefined),
    ...optionalQuery('keyword', filters.keyword),
    ...optionalQuery('workCenterId', filters.workCenterId),
    ...optionalQuery('shiftId', filters.shiftId),
    ...optionalQuery('deviceAssetId', filters.deviceAssetId),
    ...optionalQuery('source', filters.source),
    ...optionalQuery('readinessStatus', filters.readinessStatus),
    skip: filters.skip,
    take: filters.take,
  }
}

function toListQueryWithoutStatus(filters: MesListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('keyword', filters.keyword),
    ...optionalQuery('workCenterId', filters.workCenterId),
    ...optionalQuery('shiftId', filters.shiftId),
    ...optionalQuery('deviceAssetId', filters.deviceAssetId),
    skip: filters.skip,
    take: filters.take,
  }
}

function unwrapData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function envelopeItems<
  TItem,
  TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null },
>(envelope: TEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

// 注意：加载中 / 失败 / 查询未启用时信封都是 undefined，这里一律回 0——所以 `xxxTotal` 单独用
// 是**没有语义的**：0 既可能是「真的一条都没有」，也可能是「压根没取到」。页面要区分这两者，
// 必须配套读同名的 `xxxState`（见 ./businessReadState），只有 `ready` 时 0 才算数。
function envelopeTotal<TEnvelope extends { success?: boolean; data?: { total?: number } | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

function listItems(envelope: BusinessConsoleMesWorkOrderListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function unwrapSchedule(
  envelope: BusinessConsoleMesScheduleEnvelope | undefined,
): BusinessConsoleMesScheduleResult | undefined {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && part._id === id
    })
  }
}

function ignoreBackgroundError(_error: unknown) {}

// 传输层幂等键。完工入库重试与质量 hold 强制释放的重复保护由后端状态机兜底
// （重试仅 InventoryPostingFailed 可发起、强制释放仅 Active hold 生效），此处每次动作取新键即可。
// 导出供创建完工入库按「登记会话」播种键（页面持有键、成功后轮换，见 mes/receipts.vue）。
export function makeIdempotencyKey(prefix: string): string {
  const cryptoApi = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') {
    return `${prefix}-${cryptoApi.randomUUID()}`
  }
  return `${prefix}-${Math.random().toString(36).slice(2)}`
}

function invalidateMesQueries(queryCache: ReturnType<typeof useQueryCache>, ids: string[]) {
  return Promise.all(
    ids.map((id) =>
      queryCache.invalidateQueries({
        predicate: isBusinessQuery(id),
      }),
    ),
  )
}

function invalidateWorkOrders(queryCache: ReturnType<typeof useQueryCache>) {
  return invalidateMesQueries(queryCache, ['listBusinessConsoleMesWorkOrders'])
}

async function readMesOperationLifecycleRequest(
  operationTaskId: string,
  context: MesContextFilters,
  action: 'start' | 'pause' | 'resume' | 'complete' | 'report-complete',
) {
  const workOrderId = context.workOrderId?.trim()
  if (!workOrderId) return undefined

  let skip = 0
  while (true) {
    const response = await listBusinessConsoleMesOperationTasks({
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        workOrderId,
        skip,
        take: DEFAULT_TAKE,
      },
      throwOnError: false,
    })
    if (response.error !== undefined) throw response.error
    if (!response.data?.success) throw response.data ?? new Error('读取工序最新状态失败')
    const items = response.data.data?.items ?? []
    const item = items.find((candidate) => candidate.operationTaskId === operationTaskId)
    if (item) {
      return {
        domain: 'mes-operation-task' as const,
        action,
        facts: { status: item.status },
      }
    }
    const total = response.data.data?.total ?? 0
    if (items.length === 0 || skip + items.length >= total) return undefined
    skip += items.length
  }
}

export interface UseMesWorkOrdersOptions {
  initialTake?: number
}

/**
 * 生产报工（写）单独成钩：报工弹窗从工单列表 / 工序执行两处行内打开，不应为了一个写操作再拉一份工单列表。
 * 业务上下文（组织 / 环境）由本钩内部绑定并补齐到请求体，调用方只传报工对象与数量。
 */
export function useMesProductionReporting() {
  const context = defaultContext()
  const queryCache = useQueryCache()
  const issuedReportCompleteIntents = new Set<string>()
  const recordProductionReportPending = shallowRef(false)
  const recordProductionReportError = shallowRef<unknown>()
  const refreshProductionReportQueries = () =>
    invalidateMesQueries(queryCache, [
      'getBusinessConsoleMesOverview',
      'getBusinessConsoleMesWipSummary',
      'listBusinessConsoleMesProductionReports',
      'listBusinessConsoleMesWorkOrders',
      'listBusinessConsoleMesOperationTasks',
    ])

  async function recordProductionReportAction(
    body: BusinessConsoleRecordProductionReportRequest,
    options: { onCommandAttempt?: () => void } = {},
  ) {
    recordProductionReportPending.value = true
    recordProductionReportError.value = undefined
    try {
      const idempotencyKey = body.idempotencyKey?.trim()
      const workOrderId = body.workOrderId?.trim()
      const operationTaskId = body.operationTaskId?.trim()
      const reportCompleteIntent =
        body.completesOperation && idempotencyKey && workOrderId && operationTaskId
          ? `${workOrderId}\u0000${operationTaskId}\u0000${idempotencyKey}`
          : undefined
      const isIssuedReplay =
        reportCompleteIntent !== undefined && issuedReportCompleteIntents.has(reportCompleteIntent)
      const command = () => {
        if (reportCompleteIntent) issuedReportCompleteIntents.add(reportCompleteIntent)
        options.onCommandAttempt?.()
        return recordBusinessConsoleMesProductionReport({
          body: {
            organizationId: context.organizationId,
            environmentId: context.environmentId,
            ...body,
          },
          throwOnError: false,
        })
      }
      const result = body.completesOperation
        ? isIssuedReplay
          ? await command().then((response) => {
              if (response.response?.status === 409) {
                throw new LifecycleStateChangedError('conflict')
              }
              if (response.error !== undefined) throw response.error
              if (response.data?.success === false) throw response.data
              return response.data
            })
          : await executeLifecycleAction({
              readLatest: () =>
                readMesOperationLifecycleRequest(
                  body.operationTaskId ?? '',
                  {
                    organizationId: context.organizationId,
                    environmentId: context.environmentId,
                    workOrderId: body.workOrderId,
                  },
                  'report-complete',
                ),
              command,
            })
        : await command().then((response) => {
            if (response.error !== undefined) throw response.error
            if (response.data?.success === false) throw response.data
            return response.data
          })
      await refreshProductionReportQueries()
      return result
    } catch (error) {
      recordProductionReportError.value = error
      throw error
    } finally {
      recordProductionReportPending.value = false
    }
  }

  return {
    recordProductionReport: recordProductionReportAction,
    recordProductionReportError,
    recordProductionReportPending,
    refreshProductionReportState: refreshProductionReportQueries,
  }
}

export function useMesWorkOrders(options: UseMesWorkOrdersOptions = {}) {
  const filters = defaultFilters(options.initialTake)
  const queryCache = useQueryCache()

  const workOrdersQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesWorkOrdersQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )

  const createRushMutation = useMutation({
    ...createBusinessConsoleMesRushWorkOrderMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'getBusinessConsoleMesOverview',
        'listBusinessConsoleMesOperationTasks',
        'getBusinessConsoleMesWipSummary',
        'listBusinessConsoleMesWorkOrders',
      ]).catch(ignoreBackgroundError)
    },
  })

  // 报工写操作与工单列表解耦，统一走 useMesProductionReporting（报工弹窗在工序执行页也用同一个）。
  const reporting = useMesProductionReporting()

  const releaseMutation = useMutation({
    ...releaseBusinessConsoleMesWorkOrderMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'getBusinessConsoleMesWorkOrderDetail',
        'listBusinessConsoleMesWorkOrders',
        'listBusinessConsoleMesDispatchTasks',
        'listBusinessConsoleMesOperationTasks',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    createRushWorkOrder: (body: BusinessConsoleCreateRushWorkOrderRequest) =>
      createRushMutation.mutateAsync({ body }),
    createRushWorkOrderError: createRushMutation.error,
    createRushWorkOrderPending: createRushMutation.isLoading,
    filters,
    recordProductionReport: reporting.recordProductionReport,
    recordProductionReportError: reporting.recordProductionReportError,
    recordProductionReportPending: reporting.recordProductionReportPending,
    refreshWorkOrders: () => refetchWithBusinessContext(filters, workOrdersQuery),
    releaseWorkOrder: (
      workOrderId: string,
      body: {
        organizationId: string
        environmentId: string
        confirmWarnings: boolean
        idempotencyKey: string
      },
    ) =>
      releaseMutation.mutateAsync({
        path: { workOrderId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    releaseWorkOrderError: releaseMutation.error,
    releaseWorkOrderPending: releaseMutation.isLoading,
    workOrders: computed<BusinessConsoleMesWorkOrderItem[]>(() =>
      listItems(workOrdersQuery.data.value),
    ),
    workOrdersError: workOrdersQuery.error,
    workOrdersPending: workOrdersQuery.isLoading,
    workOrdersState: businessReadState(workOrdersQuery, () => hasBusinessContext(filters)),
    workOrdersTotal: computed(() => envelopeTotal(workOrdersQuery.data.value)),
  }
}

export function useMesProductionPlans() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const plansQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesProductionPlansQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )

  const convertMutation = useMutation({
    ...convertBusinessConsoleMesPlanToWorkOrderMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesProductionPlans',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesOverview',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    convertPlanToWorkOrder: (
      productionPlanId: string,
      body: {
        organizationId: string
        environmentId: string
        workOrderId?: string
        workCenterId?: string
        dueUtc?: string
        idempotencyKey: string
      },
    ) =>
      convertMutation.mutateAsync({
        path: { productionPlanId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    convertPlanToWorkOrderError: convertMutation.error,
    convertPlanToWorkOrderPending: convertMutation.isLoading,
    filters,
    productionPlans: computed<BusinessConsoleMesProductionPlanRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesProductionPlanRow,
        BusinessConsoleMesProductionPlanListEnvelope
      >(plansQuery.data.value),
    ),
    productionPlansError: plansQuery.error,
    productionPlansPending: plansQuery.isLoading,
    productionPlansState: businessReadState(plansQuery, () => hasBusinessContext(filters)),
    productionPlansTotal: computed(() => envelopeTotal(plansQuery.data.value)),
    refreshProductionPlans: () => refetchWithBusinessContext(filters, plansQuery),
  }
}

export function useMesProductionPlanReadiness(productionPlanId = '') {
  const filters = bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      productionPlanId,
    }),
  )
  const readinessEnabled = computed(
    () => hasBusinessContext(filters) && isNonEmpty(filters.productionPlanId),
  )

  const readinessQuery = useQuery(() => ({
    ...getBusinessConsoleMesProductionPlanReadinessQueryOptions({
      path: { productionPlanId: filters.productionPlanId },
      query: toContextQuery(filters),
    }),
    enabled: readinessEnabled.value,
  }))

  return {
    filters,
    planReadiness: computed(() =>
      unwrapData<
        NonNullable<BusinessConsoleMesFoundationReadinessEnvelope['data']>,
        BusinessConsoleMesFoundationReadinessEnvelope
      >(readinessQuery.data.value),
    ),
    planReadinessError: readinessQuery.error,
    planReadinessPending: readinessQuery.isLoading,
    planReadinessState: businessReadState(readinessQuery, () => readinessEnabled.value),
    refreshPlanReadiness: () =>
      readinessEnabled.value ? readinessQuery.refetch() : Promise.resolve(),
  }
}

export function useMesFoundationReadiness() {
  const filters = defaultFoundationFilters()

  const readinessQuery = useQuery(() =>
    withBusinessContextEnabled(
      getBusinessConsoleMesFoundationReadinessQueryOptions({
        query: toFoundationQuery(filters),
      }),
      filters,
    ),
  )

  return {
    filters,
    readiness: computed(() =>
      unwrapData<
        NonNullable<BusinessConsoleMesFoundationReadinessEnvelope['data']>,
        BusinessConsoleMesFoundationReadinessEnvelope
      >(readinessQuery.data.value),
    ),
    readinessError: readinessQuery.error,
    readinessPending: readinessQuery.isLoading,
    readinessState: businessReadState(readinessQuery, () => hasBusinessContext(filters)),
    refreshReadiness: () => refetchWithBusinessContext(filters, readinessQuery),
  }
}

export function useMesOverview() {
  const filters = defaultContext()

  const overviewQuery = useQuery(() =>
    withBusinessContextEnabled(
      getBusinessConsoleMesOverviewQueryOptions({
        query: toContextQuery(filters),
      }),
      filters,
    ),
  )

  const overview = computed(() =>
    unwrapData<
      NonNullable<BusinessConsoleMesOverviewEnvelope['data']>,
      BusinessConsoleMesOverviewEnvelope
    >(overviewQuery.data.value),
  )

  return {
    blockers: computed(() => overview.value?.blockers ?? []),
    counts: computed(() => overview.value?.counts ?? []),
    filters,
    overview,
    overviewError: overviewQuery.error,
    overviewPending: overviewQuery.isLoading,
    // 驾驶舱据此区分「取不到」与「真的没有阻塞」——没有它，读面 500 会被渲染成「现场无阻塞」。
    overviewState: businessReadState(overviewQuery, () => hasBusinessContext(filters)),
    pendingWork: computed(() => overview.value?.pendingWork ?? []),
    refreshOverview: () => refetchWithBusinessContext(filters, overviewQuery),
  }
}

export function useMesWorkOrderDetail() {
  const filters = defaultWorkOrderContext()
  const queryCache = useQueryCache()
  const detailEnabled = computed(
    () => hasBusinessContext(filters) && isNonEmpty(filters.workOrderId),
  )

  // 完工入库请求预览只在打开「取消工单」补偿预览时才拉取，避免每次进详情页多打一次列表请求。
  const cancelPreviewRequested = shallowRef(false)
  const receiptPreviewEnabled = computed(() => detailEnabled.value && cancelPreviewRequested.value)

  const detailQuery = useQuery(() => ({
    ...getBusinessConsoleMesWorkOrderDetailQueryOptions({
      path: { workOrderId: filters.workOrderId },
      query: toContextQuery(filters),
    }),
    enabled: detailEnabled.value,
  }))

  const materialQuery = useQuery(() => ({
    ...getBusinessConsoleMesMaterialReadinessQueryOptions({
      path: { workOrderId: filters.workOrderId },
      query: toContextQuery(filters),
    }),
    enabled: detailEnabled.value,
  }))

  // 服务端按 workOrderId 过滤（facade/底层 MES 均支持）+ 完整分页取全。取消 handler 会处理该工单的全部
  // 关联单据，预览与 toast 也必须取全，故不能固定 take/只取一页——否则单工单 >一页 时仍会少算。
  const receiptQuery = useQuery(() => {
    const scope = {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      workOrderId: filters.workOrderId,
    }
    return {
      key: listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions({
        query: { ...scope, skip: 0, take: CANCEL_PREVIEW_PAGE_SIZE },
      }).key,
      query: async () => {
        const items = await fetchAllCompensationItems<BusinessConsoleMesReceiptRequestRow>(
          async (skip, take) => {
            const { data } = await listBusinessConsoleMesFinishedGoodsReceiptRequests({
              query: { ...scope, skip, take },
              throwOnError: true,
            })
            // throwOnError 只处理非 2xx；服务可能以 HTTP 200 返回 success:false 信封。此时必须抛错，
            // 让 useQuery.error → cancelPreviewError 生效并禁用确认，而非合成空页 + success:true 放行破坏性取消。
            if (data?.success !== true || !data.data) {
              throw new Error(data?.message ?? '完工入库补偿预览请求失败')
            }
            return data.data.items ?? []
          },
        )
        return {
          success: true,
          data: { items, total: items.length },
        } as BusinessConsoleMesReceiptRequestListEnvelope
      },
      enabled: receiptPreviewEnabled.value,
    }
  })

  // 领料申请是取消补偿的权威来源：取消 handler 遍历本工单的领料申请——已收料→退料指引，未收料→释放，
  // 与齐套快照（material_requirements，仅在有已发布 MBOM 时才有）解耦，无 MBOM 的工单也能正确汇总。
  const materialIssueQuery = useQuery(() => {
    const scope = {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      workOrderId: filters.workOrderId,
    }
    return {
      key: listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
        query: { ...scope, skip: 0, take: CANCEL_PREVIEW_PAGE_SIZE },
      }).key,
      query: async () => {
        const items = await fetchAllCompensationItems<BusinessConsoleMesMaterialIssueRequestRow>(
          async (skip, take) => {
            const { data } = await listBusinessConsoleMesMaterialIssueRequests({
              query: { ...scope, skip, take },
              throwOnError: true,
            })
            // 同上：HTTP 200 + success:false 也必须抛错，避免在失败/空预览上放行破坏性取消。
            if (data?.success !== true || !data.data) {
              throw new Error(data?.message ?? '领料补偿预览请求失败')
            }
            return data.data.items ?? []
          },
        )
        return {
          success: true,
          data: { items, total: items.length },
        } as BusinessConsoleMesMaterialIssueRequestListEnvelope
      },
      enabled: receiptPreviewEnabled.value,
    }
  })

  const cancelWorkOrderPending = shallowRef(false)
  const cancelWorkOrderError = shallowRef<unknown>()
  const refreshCancelledWorkOrderQueries = () =>
    invalidateMesQueries(queryCache, [
      // 本域：取消改动工单及其派生读模型（详情/列表/概览/在制/工序/派工/齐套/领料/完工入库）
      'getBusinessConsoleMesWorkOrderDetail',
      'listBusinessConsoleMesWorkOrders',
      'getBusinessConsoleMesOverview',
      'getBusinessConsoleMesWipSummary',
      'listBusinessConsoleMesOperationTasks',
      'listBusinessConsoleMesDispatchTasks',
      'getBusinessConsoleMesMaterialReadiness',
      'listBusinessConsoleMesMaterialIssueRequests',
      'listBusinessConsoleMesFinishedGoodsReceiptRequests',
      // 跨域（A1 §4.2 跨域刷新首个落地）：预留释放后库存可用量恢复，库存可用量读面必须失效
      'getBusinessConsoleInventoryAvailability',
    ])

  async function cancelWorkOrder(reason: string) {
    cancelWorkOrderPending.value = true
    cancelWorkOrderError.value = undefined
    try {
      const result = await executeLifecycleAction({
        readLatest: async () => {
          const query = getBusinessConsoleMesWorkOrderDetailQueryOptions({
            path: { workOrderId: filters.workOrderId },
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
            },
          })
          const response = await query.query({
            signal: new AbortController().signal,
          } as Parameters<typeof query.query>[0])
          const item = response?.success ? response.data : undefined
          return item
            ? {
                domain: 'mes-work-order' as const,
                action: 'cancel' as const,
                facts: { status: item.status },
              }
            : undefined
        },
        command: () =>
          cancelBusinessConsoleMesWorkOrder({
            path: { workOrderId: filters.workOrderId },
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
            },
            body: { reason },
            throwOnError: false,
          }),
      })
      await refreshCancelledWorkOrderQueries()
      return result
    } catch (error) {
      cancelWorkOrderError.value = error
      throw error
    } finally {
      cancelWorkOrderPending.value = false
    }
  }

  return {
    activateCancelPreview: () => {
      cancelPreviewRequested.value = true
    },
    cancelWorkOrder,
    cancelWorkOrderError,
    cancelWorkOrderPending,
    // 补偿预览两项查询的加载/失败/就绪态，供破坏性确认按钮门禁：两项都成功拿到数据前禁用确认，失败可重试。
    cancelPreviewPending: computed(
      () =>
        receiptPreviewEnabled.value &&
        (receiptQuery.isLoading.value || materialIssueQuery.isLoading.value),
    ),
    cancelPreviewError: computed(() => receiptQuery.error.value ?? materialIssueQuery.error.value),
    cancelPreviewReady: computed(
      () =>
        receiptPreviewEnabled.value &&
        !receiptQuery.isLoading.value &&
        !materialIssueQuery.isLoading.value &&
        receiptQuery.error.value == null &&
        materialIssueQuery.error.value == null &&
        receiptQuery.data.value !== undefined &&
        materialIssueQuery.data.value !== undefined,
    ),
    retryCancelPreview: () => {
      void receiptQuery.refetch()
      void materialIssueQuery.refetch()
    },
    detail: computed<BusinessConsoleMesWorkOrderDetailResponse | undefined>(() =>
      unwrapData<
        BusinessConsoleMesWorkOrderDetailResponse,
        BusinessConsoleMesWorkOrderDetailEnvelope
      >(detailQuery.data.value),
    ),
    detailError: detailQuery.error,
    detailPending: detailQuery.isLoading,
    detailState: businessReadState(detailQuery, () => detailEnabled.value),
    filters,
    // 按关联单据前端汇总：该工单下未终结的完工入库请求（后端暂无取消预览端点，PR 已注明降级实现）
    finishedGoodsReceiptRequests: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesReceiptRequestRow,
        BusinessConsoleMesReceiptRequestListEnvelope
      >(receiptQuery.data.value).filter((row) => row.workOrderId === filters.workOrderId),
    ),
    // 本工单的领料申请（补偿预览的预留释放/退料指引权威来源，PR 已注明降级实现）
    materialIssueRequests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesMaterialIssueRequestRow,
        BusinessConsoleMesMaterialIssueRequestListEnvelope
      >(materialIssueQuery.data.value).filter((row) => row.workOrderId === filters.workOrderId),
    ),
    materialReadiness: computed(() =>
      unwrapData<
        NonNullable<BusinessConsoleMesMaterialReadinessEnvelope['data']>,
        BusinessConsoleMesMaterialReadinessEnvelope
      >(materialQuery.data.value),
    ),
    materialReadinessError: materialQuery.error,
    materialReadinessPending: materialQuery.isLoading,
    materialReadinessState: businessReadState(materialQuery, () => detailEnabled.value),
    refreshDetail: () => (detailEnabled.value ? detailQuery.refetch() : Promise.resolve()),
    refreshMaterialReadiness: () =>
      detailEnabled.value ? materialQuery.refetch() : Promise.resolve(),
  }
}

export function useMesOperationTasks() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const operationTasksQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesOperationTasksQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )
  function operationActionBody(
    operationTaskId: string,
    context: MesContextFilters,
    body: BusinessConsoleMesOperationTaskActionRequest,
  ) {
    return {
      path: { operationTaskId },
      query: { organizationId: context.organizationId, environmentId: context.environmentId },
      body,
    }
  }

  async function runOperationAction(
    action: 'start' | 'pause' | 'resume' | 'complete',
    operationTaskId: string,
    context: MesContextFilters,
    body: BusinessConsoleMesOperationTaskActionRequest,
  ) {
    const request = operationActionBody(operationTaskId, context, body)
    const result = await executeLifecycleAction({
      readLatest: () => readMesOperationLifecycleRequest(operationTaskId, context, action),
      command: () => {
        const options = { ...request, throwOnError: false as const }
        switch (action) {
          case 'start':
            return startBusinessConsoleMesOperationTask(options)
          case 'pause':
            return pauseBusinessConsoleMesOperationTask(options)
          case 'resume':
            return resumeBusinessConsoleMesOperationTask(options)
          case 'complete':
            return completeBusinessConsoleMesOperationTask(options)
        }
      },
    })
    await invalidateMesQueries(queryCache, [
      'listBusinessConsoleMesOperationTasks',
      'getBusinessConsoleMesWipSummary',
    ])
    return result
  }

  return {
    filters,
    completeOperationTask: (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => runOperationAction('complete', operationTaskId, context, body),
    operationTasks: computed<BusinessConsoleMesOperationTaskRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesOperationTaskRow,
        BusinessConsoleMesOperationTaskListEnvelope
      >(operationTasksQuery.data.value),
    ),
    operationTasksError: operationTasksQuery.error,
    operationTasksPending: operationTasksQuery.isLoading,
    operationTasksState: businessReadState(operationTasksQuery, () => hasBusinessContext(filters)),
    operationTasksTotal: computed(() => envelopeTotal(operationTasksQuery.data.value)),
    pauseOperationTask: (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => runOperationAction('pause', operationTaskId, context, body),
    refreshOperationTasks: () => refetchWithBusinessContext(filters, operationTasksQuery),
    resumeOperationTask: (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => runOperationAction('resume', operationTaskId, context, body),
    startOperationTask: (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => runOperationAction('start', operationTaskId, context, body),
  }
}

export interface MesCurrentOperationSopFilters extends BusinessContextFields {
  operationCode?: string
  workCenterCode?: string | null
  routingCode?: string | null
  routingRevision?: string | null
  asOfDate?: string | null
}

export function useMesCurrentOperationSops() {
  const filters = bindBusinessContext(
    reactive<MesCurrentOperationSopFilters>({
      organizationId: '',
      environmentId: '',
      operationCode: '',
      workCenterCode: '',
      routingCode: '',
      routingRevision: '',
      asOfDate: '',
    }),
  )

  const enabled = computed(
    () => hasBusinessContext(filters) && Boolean(filters.operationCode?.trim()),
  )
  const sopsQuery = useQuery(() => ({
    ...getBusinessConsoleMesCurrentOperationSopsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        operationCode: filters.operationCode?.trim() ?? '',
        ...optionalQuery('workCenterCode', filters.workCenterCode?.trim()),
        ...optionalQuery('routingCode', filters.routingCode?.trim()),
        ...optionalQuery('routingRevision', filters.routingRevision?.trim()),
        ...optionalQuery('asOfDate', filters.asOfDate?.trim()),
      },
    }),
    enabled: enabled.value,
  }))
  const downloadGrantMutation = useMutation(
    createBusinessConsoleSopFileDownloadGrantMutationOptions(),
  )

  async function createSopFileDownloadGrant(
    fileId: string,
  ): Promise<BusinessConsoleSopFileDownloadGrantResponse | null> {
    const envelope = await downloadGrantMutation.mutateAsync({
      path: { fileId },
      body: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    })
    return (
      unwrapData<
        BusinessConsoleSopFileDownloadGrantResponse,
        BusinessConsoleSopFileDownloadGrantEnvelope
      >(envelope as BusinessConsoleSopFileDownloadGrantEnvelope) ?? null
    )
  }

  return {
    filters,
    currentSops: computed<BusinessConsoleCurrentSopDocumentItem[]>(() =>
      envelopeItems<
        BusinessConsoleCurrentSopDocumentItem,
        BusinessConsoleCurrentSopDocumentsEnvelope
      >(sopsQuery.data.value as BusinessConsoleCurrentSopDocumentsEnvelope | undefined),
    ),
    currentSopsError: sopsQuery.error,
    currentSopsPending: sopsQuery.isLoading,
    currentSopsState: businessReadState(sopsQuery, () => enabled.value),
    refreshCurrentSops: () => (enabled.value ? sopsQuery.refetch() : Promise.resolve()),
    createSopFileDownloadGrant,
  }
}

export function useMesMaterialIssueRequests() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const requestsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )

  const createRequestMutation = useMutation({
    ...createBusinessConsoleMesMaterialIssueRequestMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesMaterialIssueRequests',
        'getBusinessConsoleMesMaterialReadiness',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    createMaterialIssueRequest: (
      workOrderId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesCreateMaterialIssueRequest,
    ) =>
      createRequestMutation.mutateAsync({
        path: { workOrderId },
        query: { organizationId: context.organizationId, environmentId: context.environmentId },
        body,
      }),
    createMaterialIssueRequestPending: createRequestMutation.isLoading,
    filters,
    materialIssueRequests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesMaterialIssueRequestRow,
        BusinessConsoleMesMaterialIssueRequestListEnvelope
      >(requestsQuery.data.value),
    ),
    materialIssueRequestsError: requestsQuery.error,
    materialIssueRequestsPending: requestsQuery.isLoading,
    materialIssueRequestsState: businessReadState(requestsQuery, () => hasBusinessContext(filters)),
    materialIssueRequestsTotal: computed(() => envelopeTotal(requestsQuery.data.value)),
    refreshMaterialIssueRequests: () => refetchWithBusinessContext(filters, requestsQuery),
  }
}

export function useMesDispatchTasks() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const dispatchQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesDispatchTasksQueryOptions({
        // 派工列表是唯一支持按受派工人过滤的 MES 列表，所以不走通用 toListQuery。
        query: {
          ...toListQuery(filters),
          ...optionalQuery('assignedUserId', filters.assignedUserId),
        },
      }),
      filters,
    ),
  )
  const assignMutation = useMutation({
    ...assignBusinessConsoleMesDispatchTaskMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesDispatchTasks',
        'listBusinessConsoleMesOperationTasks',
      ]).catch(ignoreBackgroundError),
  })

  return {
    assignDispatchTask: (
      operationTaskId: string,
      body: {
        organizationId: string
        environmentId: string
        assignedUserId?: string
        deviceAssetId?: string
        shiftId?: string
        idempotencyKey: string
      },
    ) =>
      assignMutation.mutateAsync({
        path: { operationTaskId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    assignDispatchTaskPending: assignMutation.isLoading,
    dispatchTasks: computed<BusinessConsoleMesDispatchTaskRow[]>(() =>
      envelopeItems<BusinessConsoleMesDispatchTaskRow, BusinessConsoleMesDispatchTaskListEnvelope>(
        dispatchQuery.data.value,
      ),
    ),
    dispatchTasksError: dispatchQuery.error,
    dispatchTasksPending: dispatchQuery.isLoading,
    dispatchTasksState: businessReadState(dispatchQuery, () => hasBusinessContext(filters)),
    dispatchTasksTotal: computed(() => envelopeTotal(dispatchQuery.data.value)),
    filters,
    refreshDispatchTasks: () => refetchWithBusinessContext(filters, dispatchQuery),
  }
}

export function useMesWipSummary() {
  const filters = defaultFilters()

  const wipQuery = useQuery(() =>
    withBusinessContextEnabled(
      getBusinessConsoleMesWipSummaryQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )

  return {
    filters,
    refreshWip: () => refetchWithBusinessContext(filters, wipQuery),
    wipError: wipQuery.error,
    wipPending: wipQuery.isLoading,
    wipState: businessReadState(wipQuery, () => hasBusinessContext(filters)),
    wipRows: computed<BusinessConsoleMesWipSummaryRow[]>(() =>
      envelopeItems<BusinessConsoleMesWipSummaryRow, BusinessConsoleMesWipSummaryEnvelope>(
        wipQuery.data.value,
      ),
    ),
    wipTotal: computed(() => envelopeTotal(wipQuery.data.value)),
  }
}

export function useMesProductionReports() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const reverseDetailReportNo = shallowRef('')

  const reportsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesProductionReportsQueryOptions({
        query: toListQueryWithoutStatus(filters),
      }),
      filters,
    ),
  )

  const reverseDetailQuery = useQuery(() => ({
    ...getBusinessConsoleMesProductionReportQueryOptions({
      path: { reportNo: reverseDetailReportNo.value },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
    enabled: hasBusinessContext(filters) && reverseDetailReportNo.value.trim().length > 0,
  }))

  const reverseMutation = useMutation({
    ...reverseBusinessConsoleMesProductionReportMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        // 本域:冲销新增负向记录行,并使原报工在列表中呈现为已冲销
        'listBusinessConsoleMesProductionReports',
        // 工单累计良品/报废回退(WorkOrder.ReverseProductionProgress),状态可能 Completed→Started
        'getBusinessConsoleMesWorkOrderDetail',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesOverview',
        'getBusinessConsoleMesWipSummary',
        // 冲销 reopen 报工所在工序任务(OperationTask.ReopenAfterReportReversal)
        'listBusinessConsoleMesOperationTasks',
        // 冲销取消该产出批次未过账的完工入库请求(FinishedGoodsReceiptRequest.Cancel)
        'listBusinessConsoleMesFinishedGoodsReceiptRequests',
      ]).catch(ignoreBackgroundError)
      // 冲销仅持久化负向物料消耗,不发布库存过账/预留事件(见 MES ReverseProductionReportCommandHandler),
      // 故不失效库存可用量读面——与取消工单(释放预留→需失效库存)语义不同,不做无据的跨域失效。
    },
  })

  return {
    filters,
    productionReports: computed<BusinessConsoleMesProductionReportRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesProductionReportRow,
        BusinessConsoleMesProductionReportListEnvelope
      >(reportsQuery.data.value),
    ),
    productionReportsError: reportsQuery.error,
    productionReportsPending: reportsQuery.isLoading,
    productionReportsState: businessReadState(reportsQuery, () => hasBusinessContext(filters)),
    productionReportsTotal: computed(() => envelopeTotal(reportsQuery.data.value)),
    refreshProductionReports: () => refetchWithBusinessContext(filters, reportsQuery),
    activateReverseDetail(reportNo: string) {
      reverseDetailReportNo.value = reportNo.trim()
    },
    deactivateReverseDetail() {
      reverseDetailReportNo.value = ''
    },
    reverseProductionReportDetail: computed<
      BusinessConsoleMesProductionReportDetailResponse | undefined
    >(() =>
      reverseDetailReportNo.value
        ? unwrapData<
            BusinessConsoleMesProductionReportDetailResponse,
            BusinessConsoleMesProductionReportDetailEnvelope
          >(reverseDetailQuery.data.value)
        : undefined,
    ),
    reverseProductionReportDetailError: reverseDetailQuery.error,
    reverseProductionReportDetailPending: reverseDetailQuery.isLoading,
    reverseProductionReport: (
      reportNo: string,
      body: { reason: string; reversedAtUtc?: string; idempotencyKey?: string },
    ) =>
      reverseMutation.mutateAsync({
        path: { reportNo },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    reverseProductionReportError: reverseMutation.error,
    reverseProductionReportPending: reverseMutation.isLoading,
  }
}

export function useMesTelemetryProductionReportCandidates() {
  const filters = Object.assign(defaultFilters(), {
    status: 'pending-confirmation',
    fromUtc: undefined as string | undefined,
    toUtc: undefined as string | undefined,
  })
  const queryCache = useQueryCache()
  const candidatesQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          status: filters.status === 'all' ? undefined : filters.status || undefined,
          workCenterId: filters.workCenterId || undefined,
          deviceAssetId: filters.deviceAssetId || undefined,
          fromUtc: filters.fromUtc,
          toUtc: filters.toUtc,
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )
  const promoteMutation = useMutation({
    ...promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesTelemetryProductionReportCandidates',
        'listBusinessConsoleMesProductionReports',
        'listBusinessConsoleMesWorkOrders',
      ]).catch(ignoreBackgroundError),
  })
  const dismissMutation = useMutation({
    ...dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesTelemetryProductionReportCandidates',
      ]).catch(ignoreBackgroundError),
  })
  type CandidateEnvelope = {
    data?: { items?: BusinessConsoleMesTelemetryCandidateRow[]; total?: number } | null
  }
  return {
    filters,
    candidates: computed(() =>
      envelopeItems<BusinessConsoleMesTelemetryCandidateRow, CandidateEnvelope>(
        candidatesQuery.data.value as CandidateEnvelope | undefined,
      ),
    ),
    total: computed(() =>
      envelopeTotal(candidatesQuery.data.value as CandidateEnvelope | undefined),
    ),
    pending: candidatesQuery.isLoading,
    error: candidatesQuery.error,
    state: businessReadState(candidatesQuery, () => hasBusinessContext(filters)),
    refresh: () => refetchWithBusinessContext(filters, candidatesQuery),
    promote: (candidateId: string, workOrderId: string, operationTaskId: string) =>
      promoteMutation.mutateAsync({
        path: { candidateId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { workOrderId, operationTaskId },
      }),
    dismiss: (candidateId: string, reason: string) =>
      dismissMutation.mutateAsync({
        path: { candidateId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { reason },
      }),
    actionPending: computed(
      () => promoteMutation.isLoading.value || dismissMutation.isLoading.value,
    ),
  }
}

export interface MesWorkOrderProducedLot {
  producedLotNo: string
  reportNo?: string
  goodQuantity: number
  // 剩余可入库量（读面已过滤耗尽批次）：Console 据此提示并按剩余限制登记数量。
  remainingQuantity: number
  serialNo?: string
}

// 工单的真实产出批次来源：完工入库创建端点强制引用 MES 已生成的产出批次
// （CreateFinishedGoodsReceiptRequestCommandHandler 在数量校验之前即拒绝空/不存在的 producedLotNo，并校验其存在于
// OutputLotGenealogies）。故直接消费权威端点 listBusinessConsoleMesReceivableProducedLots（读同一张 OutputLotGenealogies、
// 与完工入库同域 receipts.read 权限）：①报工冲销会删除对应 genealogy → 已冲销批次天然不出现，不会选中后端已判定不存在的批次；
// ②权限与本页/创建一致，避免入库操作员因缺 reporting.read 而 403。页面据此从工单真实产出中选择，不伪造批次号。
export function useMesWorkOrderProducedLots(workOrderId: () => string) {
  const filters = defaultFilters()

  const producedLotsQuery = useQuery(() => {
    const workOrderIdValue = workOrderId().trim()
    return {
      ...listBusinessConsoleMesReceivableProducedLotsQueryOptions({
        path: { workOrderId: workOrderIdValue },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
      }),
      enabled: hasBusinessContext(filters) && isNonEmpty(workOrderIdValue),
    }
  })

  const producedLots = computed<MesWorkOrderProducedLot[]>(() => {
    if (!isNonEmpty(workOrderId().trim())) return []
    const rows = envelopeItems<
      BusinessConsoleMesReceivableProducedLotRow,
      BusinessConsoleMesReceivableProducedLotListEnvelope
    >(producedLotsQuery.data.value)
    // 端点已按工单服务端过滤、产出批次在 (org,env) 内唯一，故无需前端去重/过滤。
    return rows
      .filter((row) => isNonEmpty(row.producedLotNo ?? ''))
      .map((row) => ({
        producedLotNo: (row.producedLotNo ?? '').trim(),
        reportNo: row.reportNo ?? undefined,
        goodQuantity: row.quantity ?? 0,
        remainingQuantity: row.remainingQuantity ?? 0,
        serialNo: row.serialNo?.trim() || undefined,
      }))
  })

  return {
    producedLots,
    producedLotsError: producedLotsQuery.error,
    producedLotsPending: producedLotsQuery.isLoading,
    producedLotsState: businessReadState(
      producedLotsQuery,
      () => hasBusinessContext(filters) && isNonEmpty(workOrderId().trim()),
    ),
    refreshProducedLots: () => refetchWithBusinessContext(filters, producedLotsQuery),
  }
}

export function useMesFinishedGoodsReceipts() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const receiptsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )

  const createReceiptMutation = useMutation({
    ...createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesFinishedGoodsReceiptRequests',
        'getBusinessConsoleMesOverview',
      ]).catch(ignoreBackgroundError)
    },
  })

  // 完工入库失败重试（#833 facade）：只对 InventoryPostingFailed 单据重投库存过账意图。
  const retryMutation = useMutation({
    ...retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesFinishedGoodsReceiptRequests',
        'getBusinessConsoleMesOverview',
        // 跨域（A1 §4.2）：重投成功后库存移动过账，库存可用量读面失效
        'getBusinessConsoleInventoryAvailability',
      ]).catch(ignoreBackgroundError)
    },
  })
  // 正在重试的单据号集合（支持并发重试）：spinner/禁用按单据号各自作用，A 在途时重试 B 不会互相清空状态。
  const retryingRequestNos = reactive(new Set<string>())

  return {
    createReceiptRequest: (body: BusinessConsoleMesCreateReceiptRequest) =>
      createReceiptMutation.mutateAsync({ body }),
    createReceiptRequestError: createReceiptMutation.error,
    createReceiptRequestPending: createReceiptMutation.isLoading,
    retryInventoryPosting: async (requestNo: string) => {
      retryingRequestNos.add(requestNo)
      try {
        await retryMutation.mutateAsync({
          path: { requestNo },
          query: {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
          },
          body: { idempotencyKey: makeIdempotencyKey('receipt-retry') },
        })
      } finally {
        // 只删当前单据：并发重试时不会误清其他仍在途单据的状态。
        retryingRequestNos.delete(requestNo)
      }
    },
    retryInventoryPostingError: retryMutation.error,
    isRetrying: (requestNo: string) => retryingRequestNos.has(requestNo),
    filters,
    receiptRequests: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesReceiptRequestRow,
        BusinessConsoleMesReceiptRequestListEnvelope
      >(receiptsQuery.data.value),
    ),
    receiptRequestsError: receiptsQuery.error,
    receiptRequestsPending: receiptsQuery.isLoading,
    receiptRequestsState: businessReadState(receiptsQuery, () => hasBusinessContext(filters)),
    receiptRequestsTotal: computed(() => envelopeTotal(receiptsQuery.data.value)),
    refreshReceiptRequests: () => refetchWithBusinessContext(filters, receiptsQuery),
  }
}

export interface MesQualityHoldSource {
  organizationId: string
  environmentId: string
  sourceService: string
  sourceDocumentId: string
}

// 单个质量保留(quality hold)的时间线读面(#886)+人工强制释放(既有 force-release 写面)。
// 由工单详情 hold 区块按活跃保留逐个实例化;定位键为 sourceService + sourceDocumentId。
// isReadable：时间线读端点要求 business.mes.quality.read（网关 MesQualityRead），高于本页 work-orders.read。
// 无该权限的用户不发时间线请求（否则每个保留逐一 403），由调用方按权限传入。
export function useMesQualityHold(
  source: () => MesQualityHoldSource,
  isReadable: () => boolean = () => true,
) {
  const queryCache = useQueryCache()
  const enabled = computed(() => {
    const s = source()
    return (
      isReadable() &&
      isNonEmpty(s.organizationId) &&
      isNonEmpty(s.environmentId) &&
      isNonEmpty(s.sourceService) &&
      isNonEmpty(s.sourceDocumentId)
    )
  })

  const timelineQuery = useQuery(() => {
    const s = source()
    return {
      ...getBusinessConsoleMesQualityHoldTimelineQueryOptions({
        path: { sourceDocumentId: s.sourceDocumentId },
        query: {
          organizationId: s.organizationId,
          environmentId: s.environmentId,
          sourceService: s.sourceService,
        },
      }),
      enabled: enabled.value,
    }
  })

  const forceReleaseMutation = useMutation({
    ...forceReleaseBusinessConsoleMesQualityHoldMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        // 本读面：释放后时间线追加一条 manual-force-released 事件
        'getBusinessConsoleMesQualityHoldTimeline',
        // 保留解除改动工单详情活跃保留、列表锁定标记与齐套/开工阻塞
        'getBusinessConsoleMesWorkOrderDetail',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesMaterialReadiness',
        'getBusinessConsoleMesProductionPlanReadiness',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    timeline: computed<BusinessConsoleMesQualityHoldTimelineItem[]>(() => {
      const data = timelineQuery.data.value
      return data?.success ? (data.data?.items ?? []) : []
    }),
    timelinePending: timelineQuery.isLoading,
    timelineError: timelineQuery.error,
    timelineState: businessReadState(timelineQuery, () => enabled.value),
    refreshTimeline: () => (enabled.value ? timelineQuery.refetch() : Promise.resolve()),
    forceRelease: (reason: string) => {
      const s = source()
      return forceReleaseMutation.mutateAsync({
        path: { sourceDocumentId: s.sourceDocumentId },
        query: { organizationId: s.organizationId, environmentId: s.environmentId },
        body: {
          reason,
          sourceService: s.sourceService,
          idempotencyKey: makeIdempotencyKey('quality-hold-release'),
        },
      })
    },
    forceReleasePending: forceReleaseMutation.isLoading,
    forceReleaseError: forceReleaseMutation.error,
  }
}

export function useMesQualityContext() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const qualityQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesRelatedQualityItemsQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )
  const defectMutation = useMutation({
    ...recordBusinessConsoleMesDefectMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesRelatedQualityItems']).catch(
        ignoreBackgroundError,
      ),
  })

  return {
    filters,
    qualityItems: computed<BusinessConsoleMesRelatedQualityItemRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesRelatedQualityItemRow,
        BusinessConsoleMesRelatedQualityItemListEnvelope
      >(qualityQuery.data.value),
    ),
    qualityItemsError: qualityQuery.error,
    qualityItemsPending: qualityQuery.isLoading,
    qualityItemsState: businessReadState(qualityQuery, () => hasBusinessContext(filters)),
    qualityItemsTotal: computed(() => envelopeTotal(qualityQuery.data.value)),
    recordDefect: (body: BusinessConsoleMesRecordDefectRequest) =>
      defectMutation.mutateAsync({ body }),
    recordDefectPending: defectMutation.isLoading,
    refreshQualityItems: () => refetchWithBusinessContext(filters, qualityQuery),
  }
}

export const useMesRelatedQualityItems = useMesQualityContext

export function useMesDowntimeEvents() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const downtimeQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesDowntimeEventsQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )
  const recordMutation = useMutation({
    ...recordBusinessConsoleMesDowntimeEventMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesDowntimeEvents',
        'listBusinessConsoleMesCapacityImpacts',
      ]).catch(ignoreBackgroundError),
  })
  const recoverMutation = useMutation({
    ...confirmBusinessConsoleMesDowntimeRecoveryMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesDowntimeEvents',
        'listBusinessConsoleMesCapacityImpacts',
      ]).catch(ignoreBackgroundError),
  })

  return {
    downtimeEvents: computed<BusinessConsoleMesDowntimeEventRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesDowntimeEventRow,
        BusinessConsoleMesDowntimeEventListEnvelope
      >(downtimeQuery.data.value),
    ),
    downtimeEventsError: downtimeQuery.error,
    downtimeEventsPending: downtimeQuery.isLoading,
    downtimeEventsState: businessReadState(downtimeQuery, () => hasBusinessContext(filters)),
    downtimeEventsTotal: computed(() => envelopeTotal(downtimeQuery.data.value)),
    filters,
    recordDowntimeEvent: (body: BusinessConsoleMesRecordDowntimeEventRequest) =>
      recordMutation.mutateAsync({ body }),
    recordDowntimeEventPending: recordMutation.isLoading,
    recoverDowntimeEvent: (
      downtimeEventId: string,
      body: {
        organizationId: string
        environmentId: string
        recoveredAtUtc: string
        idempotencyKey: string
      },
    ) =>
      recoverMutation.mutateAsync({
        path: { downtimeEventId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    recoverDowntimeEventPending: recoverMutation.isLoading,
    refreshDowntimeEvents: () => refetchWithBusinessContext(filters, downtimeQuery),
  }
}

export function useMesShiftHandovers() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const handoversQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesShiftHandoversQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )
  const createMutation = useMutation({
    ...createBusinessConsoleMesShiftHandoverMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesShiftHandovers']).catch(
        ignoreBackgroundError,
      ),
  })
  const acceptMutation = useMutation({
    ...acceptBusinessConsoleMesShiftHandoverMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesShiftHandovers']).catch(
        ignoreBackgroundError,
      ),
  })

  return {
    acceptShiftHandover: (
      handoverId: string,
      body: { organizationId: string; environmentId: string; idempotencyKey: string },
    ) =>
      acceptMutation.mutateAsync({
        path: { handoverId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    createShiftHandover: (body: BusinessConsoleMesCreateShiftHandoverRequest) =>
      createMutation.mutateAsync({ body }),
    filters,
    handovers: computed<BusinessConsoleMesShiftHandoverRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesShiftHandoverRow,
        BusinessConsoleMesShiftHandoverListEnvelope
      >(handoversQuery.data.value),
    ),
    handoversError: handoversQuery.error,
    handoversPending: handoversQuery.isLoading,
    handoversState: businessReadState(handoversQuery, () => hasBusinessContext(filters)),
    handoversTotal: computed(() => envelopeTotal(handoversQuery.data.value)),
    refreshHandovers: () => refetchWithBusinessContext(filters, handoversQuery),
  }
}

export function useMesTraceability() {
  const filters = defaultTraceabilityFilters()
  const workOrderEnabled = computed(
    () =>
      hasBusinessContext(filters) &&
      filters.mode === 'work-order' &&
      isNonEmpty(filters.workOrderId),
  )
  const batchEnabled = computed(
    () =>
      hasBusinessContext(filters) && filters.mode === 'batch' && isNonEmpty(filters.batchOrSerial),
  )
  const materialLotEnabled = computed(
    () =>
      hasBusinessContext(filters) &&
      filters.mode === 'material-lot' &&
      isNonEmpty(filters.materialLotId),
  )
  const workOrderQuery = useQuery(() => ({
    ...getBusinessConsoleMesWorkOrderTraceabilityQueryOptions({
      path: { workOrderId: filters.workOrderId },
      query: toContextQuery(filters),
    }),
    enabled: workOrderEnabled.value,
  }))
  const batchQuery = useQuery(() => ({
    ...getBusinessConsoleMesBatchTraceabilityQueryOptions({
      path: { batchOrSerial: filters.batchOrSerial },
      query: toContextQuery(filters),
    }),
    enabled: batchEnabled.value,
  }))
  const materialLotQuery = useQuery(() => ({
    ...getBusinessConsoleMesMaterialLotTraceabilityQueryOptions({
      path: { materialLotId: filters.materialLotId },
      query: toContextQuery(filters),
    }),
    enabled: materialLotEnabled.value,
  }))

  const activeEnvelope = computed(() => {
    if (filters.mode === 'batch') return batchQuery.data.value
    if (filters.mode === 'material-lot') return materialLotQuery.data.value
    return workOrderQuery.data.value
  })

  return {
    filters,
    refreshTraceability: () => {
      if (filters.mode === 'batch') {
        return batchEnabled.value ? batchQuery.refetch() : Promise.resolve()
      }
      if (filters.mode === 'material-lot') {
        return materialLotEnabled.value ? materialLotQuery.refetch() : Promise.resolve()
      }
      return workOrderEnabled.value ? workOrderQuery.refetch() : Promise.resolve()
    },
    traceability: computed<BusinessConsoleMesTraceabilityResponse | undefined>(() =>
      unwrapData<BusinessConsoleMesTraceabilityResponse, BusinessConsoleMesTraceabilityEnvelope>(
        activeEnvelope.value,
      ),
    ),
    traceabilityError: computed(() => {
      if (filters.mode === 'batch') return batchQuery.error.value
      if (filters.mode === 'material-lot') return materialLotQuery.error.value
      return workOrderQuery.error.value
    }),
    traceabilityPending: computed(() => {
      if (filters.mode === 'batch') return batchQuery.isLoading.value
      if (filters.mode === 'material-lot') return materialLotQuery.isLoading.value
      return workOrderQuery.isLoading.value
    }),
  }
}

export function useMesCapacityImpacts() {
  const filters = defaultFilters()

  const capacityQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesCapacityImpactsQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )

  return {
    capacityImpacts: computed<BusinessConsoleMesCapacityImpactRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesCapacityImpactRow,
        BusinessConsoleMesCapacityImpactListEnvelope
      >(capacityQuery.data.value),
    ),
    capacityImpactsError: capacityQuery.error,
    capacityImpactsPending: capacityQuery.isLoading,
    capacityImpactsState: businessReadState(capacityQuery, () => hasBusinessContext(filters)),
    capacityImpactsTotal: computed(() => envelopeTotal(capacityQuery.data.value)),
    filters,
    refreshCapacityImpacts: () => refetchWithBusinessContext(filters, capacityQuery),
  }
}

export function useMesSchedules() {
  const queryCache = useQueryCache()
  // 本次会话刚跑的那一次：立即展示，不必等历史列表重取。
  const lastScheduleEnvelope = shallowRef<BusinessConsoleMesScheduleEnvelope>()
  const filters = defaultFilters()

  // 历史排程结果的真读面。此前这里只有 mutation，页面刷新后历史一条都查不到。
  const historyQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesScheduleResultsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  const runScheduleMutation = useMutation({
    ...runBusinessConsoleMesScheduleMutationOptions(),
    onSuccess(result) {
      lastScheduleEnvelope.value = result
      void invalidateWorkOrders(queryCache).catch(ignoreBackgroundError)
      // 新跑的这一次已经落库，失效重取比乐观补一行更准。
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesScheduleResults']).catch(
        ignoreBackgroundError,
      )
    },
  })

  return {
    filters,
    lastSchedule: computed(() => unwrapSchedule(lastScheduleEnvelope.value)),
    scheduleHistory: computed<BusinessConsoleMesScheduleResultRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesScheduleResultRow,
        BusinessConsoleMesScheduleResultListEnvelope
      >(historyQuery.data.value),
    ),
    scheduleHistoryTotal: computed(() => envelopeTotal(historyQuery.data.value)),
    scheduleHistoryError: historyQuery.error,
    scheduleHistoryPending: historyQuery.isLoading,
    scheduleHistoryState: businessReadState(historyQuery, () => hasBusinessContext(filters)),
    refreshScheduleHistory: () => refetchWithBusinessContext(filters, historyQuery),
    runSchedule: (body: BusinessConsoleRunScheduleRequest) =>
      runScheduleMutation.mutateAsync({ body }),
    runScheduleError: runScheduleMutation.error,
    runSchedulePending: runScheduleMutation.isLoading,
  }
}
