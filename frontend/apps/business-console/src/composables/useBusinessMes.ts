import {
  acceptBusinessConsoleMesShiftHandoverMutationOptions,
  assignBusinessConsoleMesDispatchTaskMutationOptions,
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleMesDowntimeRecoveryMutationOptions,
  convertBusinessConsoleMesPlanToWorkOrderMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  createBusinessConsoleMesShiftHandoverMutationOptions,
  getBusinessConsoleMesBatchTraceabilityQueryOptions,
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions,
  getBusinessConsoleMesFoundationReadinessQueryOptions,
  getBusinessConsoleMesMaterialReadinessQueryOptions,
  getBusinessConsoleMesOverviewQueryOptions,
  getBusinessConsoleMesProductionPlanReadinessQueryOptions,
  getBusinessConsoleMesWipSummaryQueryOptions,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions,
  listBusinessConsoleMesDispatchTasksQueryOptions,
  listBusinessConsoleMesDowntimeEventsQueryOptions,
  listBusinessConsoleMesCapacityImpactsQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesProductionPlansQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesRelatedQualityItemsQueryOptions,
  listBusinessConsoleMesShiftHandoversQueryOptions,
  pauseBusinessConsoleMesOperationTaskMutationOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesDefectMutationOptions,
  recordBusinessConsoleMesDowntimeEventMutationOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  releaseBusinessConsoleMesWorkOrderMutationOptions,
  resumeBusinessConsoleMesOperationTaskMutationOptions,
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
  type BusinessConsoleMesOperationTaskActionRequest,
  type BusinessConsoleMesOperationTaskListEnvelope,
  type BusinessConsoleMesOperationTaskRow,
  type BusinessConsoleMesOverviewEnvelope,
  type BusinessConsoleMesProductionPlanListEnvelope,
  type BusinessConsoleMesProductionPlanRow,
  type BusinessConsoleMesProductionReportListEnvelope,
  type BusinessConsoleMesProductionReportRow,
  type BusinessConsoleMesRecordDefectRequest,
  type BusinessConsoleMesRecordDowntimeEventRequest,
  type BusinessConsoleMesRelatedQualityItemListEnvelope,
  type BusinessConsoleMesRelatedQualityItemRow,
  type BusinessConsoleMesReceiptRequestListEnvelope,
  type BusinessConsoleMesReceiptRequestRow,
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
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'

const DEFAULT_TAKE = 100

export interface MesListFilters {
  organizationId: string
  environmentId: string
  status?: string
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

export interface MesContextFilters {
  organizationId: string
  environmentId: string
}

export interface MesTraceabilityFilters extends MesContextFilters {
  workOrderId: string
  batchOrSerial: string
  materialLotId: string
  mode: 'work-order' | 'batch' | 'material-lot'
}

function defaultFilters(): MesListFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    take: DEFAULT_TAKE,
  })
}

function defaultContext(): MesContextFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
}

function defaultFoundationFilters(): MesFoundationReadinessFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
}

function defaultWorkOrderContext(): MesWorkOrderContext {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    workOrderId: 'WO-001',
  })
}

function defaultTraceabilityFilters(): MesTraceabilityFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    workOrderId: 'WO-001',
    batchOrSerial: '',
    materialLotId: '',
    mode: 'work-order',
  })
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
    ...optionalQuery('status', filters.status),
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

function envelopeItems<TItem, TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
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
    listBusinessConsoleMesWorkOrdersQueryOptions({
      query: toListQuery(filters),
    }),
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
    refreshWorkOrders: workOrdersQuery.refetch,
    releaseWorkOrder: (workOrderId: string, body: { organizationId: string; environmentId: string; confirmWarnings: boolean; idempotencyKey: string }) =>
      releaseMutation.mutateAsync({ path: { workOrderId }, query: { organizationId: body.organizationId, environmentId: body.environmentId }, body }),
    releaseWorkOrderError: releaseMutation.error,
    releaseWorkOrderPending: releaseMutation.isLoading,
    workOrders: computed<BusinessConsoleMesWorkOrderItem[]>(() =>
      listItems(workOrdersQuery.data.value),
    ),
    workOrdersError: workOrdersQuery.error,
    workOrdersPending: workOrdersQuery.isLoading,
  }
}

export function useMesProductionPlans() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const plansQuery = useQuery(() =>
    listBusinessConsoleMesProductionPlansQueryOptions({
      query: toListQuery(filters),
    }),
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
      body: { organizationId: string; environmentId: string; workOrderId?: string; workCenterId?: string; dueUtc?: string; idempotencyKey: string },
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
      envelopeItems<BusinessConsoleMesProductionPlanRow, BusinessConsoleMesProductionPlanListEnvelope>(
        plansQuery.data.value,
      ),
    ),
    productionPlansError: plansQuery.error,
    productionPlansPending: plansQuery.isLoading,
    refreshProductionPlans: plansQuery.refetch,
  }
}

export function useMesProductionPlanReadiness(productionPlanId = 'PLAN-001') {
  const filters = reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    productionPlanId,
  })

  const readinessQuery = useQuery(() =>
    getBusinessConsoleMesProductionPlanReadinessQueryOptions({
      path: { productionPlanId: filters.productionPlanId },
      query: toContextQuery(filters),
    }),
  )

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
    refreshPlanReadiness: readinessQuery.refetch,
  }
}

export function useMesFoundationReadiness() {
  const filters = defaultFoundationFilters()

  const readinessQuery = useQuery(() =>
    getBusinessConsoleMesFoundationReadinessQueryOptions({
      query: toFoundationQuery(filters),
    }),
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
    refreshReadiness: readinessQuery.refetch,
  }
}

export function useMesOverview() {
  const filters = defaultContext()

  const overviewQuery = useQuery(() =>
    getBusinessConsoleMesOverviewQueryOptions({
      query: toContextQuery(filters),
    }),
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
    refreshOverview: overviewQuery.refetch,
  }
}

export function useMesWorkOrderDetail() {
  const filters = defaultWorkOrderContext()

  const detailQuery = useQuery(() =>
    getBusinessConsoleMesWorkOrderDetailQueryOptions({
      path: { workOrderId: filters.workOrderId },
      query: toContextQuery(filters),
    }),
  )

  const materialQuery = useQuery(() =>
    getBusinessConsoleMesMaterialReadinessQueryOptions({
      path: { workOrderId: filters.workOrderId },
      query: toContextQuery(filters),
    }),
  )

  return {
    detail: computed<BusinessConsoleMesWorkOrderDetailResponse | undefined>(() =>
      unwrapData<BusinessConsoleMesWorkOrderDetailResponse, BusinessConsoleMesWorkOrderDetailEnvelope>(
        detailQuery.data.value,
      ),
    ),
    detailError: detailQuery.error,
    detailPending: detailQuery.isLoading,
    filters,
    materialReadiness: computed(() =>
      unwrapData<
        NonNullable<BusinessConsoleMesMaterialReadinessEnvelope['data']>,
        BusinessConsoleMesMaterialReadinessEnvelope
      >(materialQuery.data.value),
    ),
    materialReadinessError: materialQuery.error,
    materialReadinessPending: materialQuery.isLoading,
    refreshDetail: detailQuery.refetch,
    refreshMaterialReadiness: materialQuery.refetch,
  }
}

export function useMesOperationTasks() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const operationTasksQuery = useQuery(() =>
    listBusinessConsoleMesOperationTasksQueryOptions({
      query: toListQuery(filters),
    }),
  )
  const startMutation = useMutation({
    ...startBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesOperationTasks', 'getBusinessConsoleMesWipSummary']).catch(ignoreBackgroundError),
  })
  const pauseMutation = useMutation({
    ...pauseBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesOperationTasks', 'getBusinessConsoleMesWipSummary']).catch(ignoreBackgroundError),
  })
  const resumeMutation = useMutation({
    ...resumeBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesOperationTasks', 'getBusinessConsoleMesWipSummary']).catch(ignoreBackgroundError),
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesOperationTasks', 'getBusinessConsoleMesWipSummary']).catch(ignoreBackgroundError),
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
    completeOperationTask: (operationTaskId: string, context: MesContextFilters, body: BusinessConsoleMesOperationTaskActionRequest) =>
      completeMutation.mutateAsync(operationActionBody(operationTaskId, context, body)),
    operationTasks: computed<BusinessConsoleMesOperationTaskRow[]>(() =>
      envelopeItems<BusinessConsoleMesOperationTaskRow, BusinessConsoleMesOperationTaskListEnvelope>(
        operationTasksQuery.data.value,
      ),
    ),
    operationTasksError: operationTasksQuery.error,
    operationTasksPending: operationTasksQuery.isLoading,
    pauseOperationTask: (operationTaskId: string, context: MesContextFilters, body: BusinessConsoleMesOperationTaskActionRequest) =>
      pauseMutation.mutateAsync(operationActionBody(operationTaskId, context, body)),
    refreshOperationTasks: operationTasksQuery.refetch,
    resumeOperationTask: (operationTaskId: string, context: MesContextFilters, body: BusinessConsoleMesOperationTaskActionRequest) =>
      resumeMutation.mutateAsync(operationActionBody(operationTaskId, context, body)),
    startOperationTask: (operationTaskId: string, context: MesContextFilters, body: BusinessConsoleMesOperationTaskActionRequest) =>
      startMutation.mutateAsync(operationActionBody(operationTaskId, context, body)),
  }
}

export function useMesMaterialIssueRequests() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const requestsQuery = useQuery(() =>
    listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
      query: toListQuery(filters),
    }),
  )

  const createRequestMutation = useMutation({
    ...createBusinessConsoleMesMaterialIssueRequestMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests', 'getBusinessConsoleMesMaterialReadiness']).catch(ignoreBackgroundError)
    },
  })

  return {
    createMaterialIssueRequest: (workOrderId: string, context: MesContextFilters, body: BusinessConsoleMesCreateMaterialIssueRequest) =>
      createRequestMutation.mutateAsync({
        path: { workOrderId },
        query: { organizationId: context.organizationId, environmentId: context.environmentId },
        body,
      }),
    createMaterialIssueRequestPending: createRequestMutation.isLoading,
    filters,
    materialIssueRequests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<BusinessConsoleMesMaterialIssueRequestRow, BusinessConsoleMesMaterialIssueRequestListEnvelope>(
        requestsQuery.data.value,
      ),
    ),
    materialIssueRequestsError: requestsQuery.error,
    materialIssueRequestsPending: requestsQuery.isLoading,
    refreshMaterialIssueRequests: requestsQuery.refetch,
  }
}

export function useMesDispatchTasks() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const dispatchQuery = useQuery(() =>
    listBusinessConsoleMesDispatchTasksQueryOptions({
      query: toListQuery(filters),
    }),
  )
  const assignMutation = useMutation({
    ...assignBusinessConsoleMesDispatchTaskMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesDispatchTasks', 'listBusinessConsoleMesOperationTasks']).catch(ignoreBackgroundError),
  })

  return {
    assignDispatchTask: (operationTaskId: string, body: { organizationId: string; environmentId: string; assignedUserId?: string; deviceAssetId?: string; shiftId?: string; idempotencyKey: string }) =>
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
    filters,
    refreshDispatchTasks: dispatchQuery.refetch,
  }
}

export function useMesWipSummary() {
  const filters = defaultFilters()

  const wipQuery = useQuery(() =>
    getBusinessConsoleMesWipSummaryQueryOptions({
      query: toListQuery(filters),
    }),
  )

  return {
    filters,
    refreshWip: wipQuery.refetch,
    wipError: wipQuery.error,
    wipPending: wipQuery.isLoading,
    wipRows: computed<BusinessConsoleMesWipSummaryRow[]>(() =>
      envelopeItems<BusinessConsoleMesWipSummaryRow, BusinessConsoleMesWipSummaryEnvelope>(
        wipQuery.data.value,
      ),
    ),
  }
}

export function useMesProductionReports() {
  const filters = defaultFilters()

  const reportsQuery = useQuery(() =>
    listBusinessConsoleMesProductionReportsQueryOptions({
      query: toListQuery(filters),
    }),
  )

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
    refreshProductionReports: reportsQuery.refetch,
  }
}

export function useMesFinishedGoodsReceipts() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const receiptsQuery = useQuery(() =>
    listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions({
      query: toListQuery(filters),
    }),
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

  return {
    createReceiptRequest: (body: BusinessConsoleMesCreateReceiptRequest) =>
      createReceiptMutation.mutateAsync({ body }),
    createReceiptRequestError: createReceiptMutation.error,
    createReceiptRequestPending: createReceiptMutation.isLoading,
    filters,
    receiptRequests: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<BusinessConsoleMesReceiptRequestRow, BusinessConsoleMesReceiptRequestListEnvelope>(
        receiptsQuery.data.value,
      ),
    ),
    receiptRequestsError: receiptsQuery.error,
    receiptRequestsPending: receiptsQuery.isLoading,
    refreshReceiptRequests: receiptsQuery.refetch,
  }
}

export function useMesQualityContext() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const qualityQuery = useQuery(() =>
    listBusinessConsoleMesRelatedQualityItemsQueryOptions({
      query: toListQuery(filters),
    }),
  )
  const defectMutation = useMutation({
    ...recordBusinessConsoleMesDefectMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesRelatedQualityItems']).catch(ignoreBackgroundError),
  })

  return {
    filters,
    qualityItems: computed<BusinessConsoleMesRelatedQualityItemRow[]>(() =>
      envelopeItems<BusinessConsoleMesRelatedQualityItemRow, BusinessConsoleMesRelatedQualityItemListEnvelope>(
        qualityQuery.data.value,
      ),
    ),
    qualityItemsError: qualityQuery.error,
    qualityItemsPending: qualityQuery.isLoading,
    recordDefect: (body: BusinessConsoleMesRecordDefectRequest) => defectMutation.mutateAsync({ body }),
    recordDefectPending: defectMutation.isLoading,
    refreshQualityItems: qualityQuery.refetch,
  }
}

export function useMesDowntimeEvents() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const downtimeQuery = useQuery(() =>
    listBusinessConsoleMesDowntimeEventsQueryOptions({
      query: toListQuery(filters),
    }),
  )
  const recordMutation = useMutation({
    ...recordBusinessConsoleMesDowntimeEventMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesDowntimeEvents', 'listBusinessConsoleMesCapacityImpacts']).catch(ignoreBackgroundError),
  })
  const recoverMutation = useMutation({
    ...confirmBusinessConsoleMesDowntimeRecoveryMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesDowntimeEvents', 'listBusinessConsoleMesCapacityImpacts']).catch(ignoreBackgroundError),
  })

  return {
    downtimeEvents: computed<BusinessConsoleMesDowntimeEventRow[]>(() =>
      envelopeItems<BusinessConsoleMesDowntimeEventRow, BusinessConsoleMesDowntimeEventListEnvelope>(
        downtimeQuery.data.value,
      ),
    ),
    downtimeEventsError: downtimeQuery.error,
    downtimeEventsPending: downtimeQuery.isLoading,
    filters,
    recordDowntimeEvent: (body: BusinessConsoleMesRecordDowntimeEventRequest) => recordMutation.mutateAsync({ body }),
    recordDowntimeEventPending: recordMutation.isLoading,
    recoverDowntimeEvent: (downtimeEventId: string, body: { organizationId: string; environmentId: string; recoveredAtUtc: string; idempotencyKey: string }) =>
      recoverMutation.mutateAsync({
        path: { downtimeEventId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    recoverDowntimeEventPending: recoverMutation.isLoading,
    refreshDowntimeEvents: downtimeQuery.refetch,
  }
}

export function useMesShiftHandovers() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const handoversQuery = useQuery(() =>
    listBusinessConsoleMesShiftHandoversQueryOptions({
      query: toListQuery(filters),
    }),
  )
  const createMutation = useMutation({
    ...createBusinessConsoleMesShiftHandoverMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesShiftHandovers']).catch(ignoreBackgroundError),
  })
  const acceptMutation = useMutation({
    ...acceptBusinessConsoleMesShiftHandoverMutationOptions(),
    onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesShiftHandovers']).catch(ignoreBackgroundError),
  })

  return {
    acceptShiftHandover: (handoverId: string, body: { organizationId: string; environmentId: string; idempotencyKey: string }) =>
      acceptMutation.mutateAsync({
        path: { handoverId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    createShiftHandover: (body: BusinessConsoleMesCreateShiftHandoverRequest) => createMutation.mutateAsync({ body }),
    filters,
    handovers: computed<BusinessConsoleMesShiftHandoverRow[]>(() =>
      envelopeItems<BusinessConsoleMesShiftHandoverRow, BusinessConsoleMesShiftHandoverListEnvelope>(
        handoversQuery.data.value,
      ),
    ),
    handoversError: handoversQuery.error,
    handoversPending: handoversQuery.isLoading,
    refreshHandovers: handoversQuery.refetch,
  }
}

export function useMesTraceability() {
  const filters = defaultTraceabilityFilters()
  const workOrderQuery = useQuery(() =>
    getBusinessConsoleMesWorkOrderTraceabilityQueryOptions({
      path: { workOrderId: filters.workOrderId || 'WO-001' },
      query: toContextQuery(filters),
    }),
  )
  const batchQuery = useQuery(() =>
    getBusinessConsoleMesBatchTraceabilityQueryOptions({
      path: { batchOrSerial: filters.batchOrSerial || 'BATCH-001' },
      query: toContextQuery(filters),
    }),
  )
  const materialLotQuery = useQuery(() =>
    getBusinessConsoleMesMaterialLotTraceabilityQueryOptions({
      path: { materialLotId: filters.materialLotId || 'LOT-001' },
      query: toContextQuery(filters),
    }),
  )

  const activeEnvelope = computed(() => {
    if (filters.mode === 'batch') return batchQuery.data.value
    if (filters.mode === 'material-lot') return materialLotQuery.data.value
    return workOrderQuery.data.value
  })

  return {
    filters,
    refreshTraceability: () => {
      if (filters.mode === 'batch') return batchQuery.refetch()
      if (filters.mode === 'material-lot') return materialLotQuery.refetch()
      return workOrderQuery.refetch()
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
    listBusinessConsoleMesCapacityImpactsQueryOptions({
      query: toListQuery(filters),
    }),
  )

  return {
    capacityImpacts: computed<BusinessConsoleMesCapacityImpactRow[]>(() =>
      envelopeItems<BusinessConsoleMesCapacityImpactRow, BusinessConsoleMesCapacityImpactListEnvelope>(
        capacityQuery.data.value,
      ),
    ),
    capacityImpactsError: capacityQuery.error,
    capacityImpactsPending: capacityQuery.isLoading,
    filters,
    refreshCapacityImpacts: capacityQuery.refetch,
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
