import {
  acceptBusinessConsoleMesShiftHandoverMutationOptions,
  assignBusinessConsoleMesDispatchTaskMutationOptions,
  cancelBusinessConsoleMesWorkOrderMutationOptions,
  completeBusinessConsoleMesOperationTaskMutationOptions,
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
  listBusinessConsoleMesProductionPlansQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions,
  promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  listBusinessConsoleMesRelatedQualityItemsQueryOptions,
  listBusinessConsoleMesShiftHandoversQueryOptions,
  pauseBusinessConsoleMesOperationTaskMutationOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesDefectMutationOptions,
  recordBusinessConsoleMesDowntimeEventMutationOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  releaseBusinessConsoleMesWorkOrderMutationOptions,
  resumeBusinessConsoleMesOperationTaskMutationOptions,
  reverseBusinessConsoleMesProductionReportMutationOptions,
  runBusinessConsoleMesScheduleMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
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

export interface MesContextFilters extends BusinessContextFields {}

export interface MesTraceabilityFilters extends MesContextFilters {
  workOrderId: string
  batchOrSerial: string
  materialLotId: string
  mode: 'work-order' | 'batch' | 'material-lot'
}

function defaultFilters(): MesListFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
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

export function useMesWorkOrders() {
  const filters = defaultFilters()
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

  const reportMutation = useMutation({
    ...recordBusinessConsoleMesProductionReportMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'getBusinessConsoleMesOverview',
        'getBusinessConsoleMesWipSummary',
        'listBusinessConsoleMesProductionReports',
        'listBusinessConsoleMesWorkOrders',
      ]).catch(ignoreBackgroundError)
    },
  })

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
    recordProductionReport: (body: BusinessConsoleRecordProductionReportRequest) =>
      reportMutation.mutateAsync({ body }),
    recordProductionReportError: reportMutation.error,
    recordProductionReportPending: reportMutation.isLoading,
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

  const cancelMutation = useMutation({
    ...cancelBusinessConsoleMesWorkOrderMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
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
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    activateCancelPreview: () => {
      cancelPreviewRequested.value = true
    },
    cancelWorkOrder: (reason: string) =>
      cancelMutation.mutateAsync({
        path: { workOrderId: filters.workOrderId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { reason },
      }),
    cancelWorkOrderError: cancelMutation.error,
    cancelWorkOrderPending: cancelMutation.isLoading,
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
  const startMutation = useMutation({
    ...startBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesOperationTasks',
        'getBusinessConsoleMesWipSummary',
      ]).catch(ignoreBackgroundError),
  })
  const pauseMutation = useMutation({
    ...pauseBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesOperationTasks',
        'getBusinessConsoleMesWipSummary',
      ]).catch(ignoreBackgroundError),
  })
  const resumeMutation = useMutation({
    ...resumeBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesOperationTasks',
        'getBusinessConsoleMesWipSummary',
      ]).catch(ignoreBackgroundError),
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesOperationTasks',
        'getBusinessConsoleMesWipSummary',
      ]).catch(ignoreBackgroundError),
  })

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

  return {
    filters,
    completeOperationTask: (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => completeMutation.mutateAsync(operationActionBody(operationTaskId, context, body)),
    operationTasks: computed<BusinessConsoleMesOperationTaskRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesOperationTaskRow,
        BusinessConsoleMesOperationTaskListEnvelope
      >(operationTasksQuery.data.value),
    ),
    operationTasksError: operationTasksQuery.error,
    operationTasksPending: operationTasksQuery.isLoading,
    operationTasksTotal: computed(() => envelopeTotal(operationTasksQuery.data.value)),
    pauseOperationTask: (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => pauseMutation.mutateAsync(operationActionBody(operationTaskId, context, body)),
    refreshOperationTasks: () => refetchWithBusinessContext(filters, operationTasksQuery),
    resumeOperationTask: (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => resumeMutation.mutateAsync(operationActionBody(operationTaskId, context, body)),
    startOperationTask: (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => startMutation.mutateAsync(operationActionBody(operationTaskId, context, body)),
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
        query: toListQuery(filters),
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
  serialNo?: string
}

// 工单的真实产出批次来源：完工入库创建端点强制引用 MES 已生成的产出批次
// （CreateFinishedGoodsReceiptRequestCommandHandler 在数量校验之前即拒绝空/不存在的 producedLotNo），
// 页面据此让操作员从工单真实报工产出中选择，而非前端伪造。产出批次即报工时生成的 OutputLotGenealogy，
// 与生产报工一一对应，故从工单的报工列表按 producedLotNo 去重取得（列表 keyword 命中工单号，前端再精确过滤）。
export function useMesWorkOrderProducedLots(workOrderId: () => string) {
  const filters = defaultFilters()

  const reportsQuery = useQuery(() => {
    const workOrderIdValue = workOrderId().trim()
    return {
      ...listBusinessConsoleMesProductionReportsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('keyword', workOrderIdValue),
          skip: 0,
          take: DEFAULT_TAKE,
        },
      }),
      enabled: hasBusinessContext(filters) && isNonEmpty(workOrderIdValue),
    }
  })

  const producedLots = computed<MesWorkOrderProducedLot[]>(() => {
    const workOrderIdValue = workOrderId().trim()
    if (!isNonEmpty(workOrderIdValue)) return []
    const rows = envelopeItems<
      BusinessConsoleMesProductionReportRow,
      BusinessConsoleMesProductionReportListEnvelope
    >(reportsQuery.data.value)
    const seen = new Set<string>()
    const lots: MesWorkOrderProducedLot[] = []
    for (const row of rows) {
      const lot = row.producedLotNo?.trim()
      // 仅取该工单、有产出批次、良品>0 的报工（排除仅报废/返修与冲销负向行），按批次号去重。
      if (
        !lot ||
        row.workOrderId !== workOrderIdValue ||
        (row.goodQuantity ?? 0) <= 0 ||
        seen.has(lot)
      ) {
        continue
      }
      seen.add(lot)
      lots.push({
        producedLotNo: lot,
        reportNo: row.reportNo,
        goodQuantity: row.goodQuantity ?? 0,
        serialNo: row.serialNo?.trim() || undefined,
      })
    }
    return lots
  })

  return {
    producedLots,
    producedLotsError: reportsQuery.error,
    producedLotsPending: reportsQuery.isLoading,
    refreshProducedLots: () => refetchWithBusinessContext(filters, reportsQuery),
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
  // 当前正在重试的单据号，供行内按钮 spinner/禁用只作用于该行（列表其余行不受影响）。
  const retryingRequestNo = shallowRef<string | null>(null)

  return {
    createReceiptRequest: (body: BusinessConsoleMesCreateReceiptRequest) =>
      createReceiptMutation.mutateAsync({ body }),
    createReceiptRequestError: createReceiptMutation.error,
    createReceiptRequestPending: createReceiptMutation.isLoading,
    retryInventoryPosting: async (requestNo: string) => {
      retryingRequestNo.value = requestNo
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
        retryingRequestNo.value = null
      }
    },
    retryInventoryPostingError: retryMutation.error,
    retryingRequestNo: computed(() => retryingRequestNo.value),
    filters,
    receiptRequests: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesReceiptRequestRow,
        BusinessConsoleMesReceiptRequestListEnvelope
      >(receiptsQuery.data.value),
    ),
    receiptRequestsError: receiptsQuery.error,
    receiptRequestsPending: receiptsQuery.isLoading,
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
export function useMesQualityHold(source: () => MesQualityHoldSource) {
  const queryCache = useQueryCache()
  const enabled = computed(() => {
    const s = source()
    return (
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
    capacityImpactsTotal: computed(() => envelopeTotal(capacityQuery.data.value)),
    filters,
    refreshCapacityImpacts: () => refetchWithBusinessContext(filters, capacityQuery),
  }
}

export function useMesSchedules() {
  const queryCache = useQueryCache()
  const lastScheduleEnvelope = shallowRef<BusinessConsoleMesScheduleEnvelope>()

  const runScheduleMutation = useMutation({
    ...runBusinessConsoleMesScheduleMutationOptions(),
    onSuccess(result) {
      lastScheduleEnvelope.value = result
      void invalidateWorkOrders(queryCache).catch(ignoreBackgroundError)
    },
  })

  return {
    lastSchedule: computed(() => unwrapSchedule(lastScheduleEnvelope.value)),
    runSchedule: (body: BusinessConsoleRunScheduleRequest) =>
      runScheduleMutation.mutateAsync({ body }),
    runScheduleError: runScheduleMutation.error,
    runSchedulePending: runScheduleMutation.isLoading,
  }
}
