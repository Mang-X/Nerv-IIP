import {
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  createBusinessConsoleSopFileDownloadGrantMutationOptions,
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions,
  promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  pauseBusinessConsoleMesOperationTaskMutationOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  resumeBusinessConsoleMesOperationTaskMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
  type BusinessConsoleCurrentSopDocumentItem,
  type BusinessConsoleCurrentSopDocumentsEnvelope,
  type BusinessConsoleSopFileDownloadGrantEnvelope,
  type BusinessConsoleSopFileDownloadGrantResponse,
  type BusinessConsoleMesConfirmLineSideReceiptRequest,
  type BusinessConsoleMesCreateMaterialIssueRequest,
  type BusinessConsoleMesCreateReceiptRequest,
  type BusinessConsoleMesMaterialIssueRequestListEnvelope,
  type BusinessConsoleMesMaterialIssueRequestRow,
  type BusinessConsoleMesOperationTaskActionRequest,
  type BusinessConsoleMesOperationTaskListEnvelope,
  type BusinessConsoleMesOperationTaskRow,
  type BusinessConsoleMesProductionReportListEnvelope,
  type BusinessConsoleMesProductionReportRow,
  type BusinessConsoleMesTelemetryCandidateRow,
  type BusinessConsoleMesReceiptRequestListEnvelope,
  type BusinessConsoleMesReceiptRequestRow,
  type BusinessConsoleMesWorkOrderItem,
  type BusinessConsoleMesWorkOrderListEnvelope,
  type BusinessConsoleRecordProductionReportRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, watchEffect } from 'vue'
import { useAuthStore } from '@/stores/auth'

const DEFAULT_TAKE = 100

export interface MesListFilters {
  organizationId: string
  environmentId: string
  status?: string
  keyword?: string
  workOrderId?: string
  workCenterId?: string
  deviceAssetId?: string
  skip: number
  take: number
}

type MesScope = Pick<MesListFilters, 'organizationId' | 'environmentId'>

/**
 * PDA has no business-context store — org/env come from the logged-in principal.
 * Keeps the reactive `filters` scope synced so an empty principal yields an empty
 * scope and disables list queries.
 */
function bindAuthScope<TFilters extends MesScope>(filters: TFilters): TFilters {
  const auth = useAuthStore()
  watchEffect(() => {
    filters.organizationId = auth.principal?.organizationId ?? ''
    filters.environmentId = auth.principal?.environmentId ?? ''
  })
  return filters
}

function defaultFilters(): MesListFilters {
  return bindAuthScope(reactive({
    organizationId: '',
    environmentId: '',
    skip: 0,
    take: DEFAULT_TAKE,
  }))
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function toListQuery(filters: MesListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
    ...optionalQuery('workOrderId', filters.workOrderId),
    ...optionalQuery('workCenterId', filters.workCenterId),
    ...optionalQuery('deviceAssetId', filters.deviceAssetId),
    skip: filters.skip,
    take: filters.take,
  }
}

function hasScope(filters: MesScope) {
  return Boolean(filters.organizationId && filters.environmentId)
}

function scopeQuery(filters: MesScope) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
  }
}

function envelopeItems<TItem, TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null }>(
  envelope: TEnvelope | undefined,
) {
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

function envelopeData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
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

export function useMesWorkOrders() {
  const filters = defaultFilters()

  const workOrdersQuery = useQuery(() => ({
    ...listBusinessConsoleMesWorkOrdersQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: hasScope(filters),
  }))

  return {
    filters,
    workOrders: computed<BusinessConsoleMesWorkOrderItem[]>(() =>
      envelopeItems<BusinessConsoleMesWorkOrderItem, BusinessConsoleMesWorkOrderListEnvelope>(
        workOrdersQuery.data.value,
      ),
    ),
    total: computed(() => envelopeTotal(workOrdersQuery.data.value)),
    pending: workOrdersQuery.isLoading,
    error: workOrdersQuery.error,
    refresh: workOrdersQuery.refetch,
  }
}

/**
 * Per-action options for operation-task transitions. The page mints a STABLE
 * `idempotencyKey` once per user-initiated action and reuses it across retries,
 * so a lost response never double-applies (illegal 工序 start/complete).
 */
export interface OperationActionOptions {
  reasonCode?: string
  idempotencyKey: string
}

export function useMesOperationTasks() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const operationTasksQuery = useQuery(() => ({
    ...listBusinessConsoleMesOperationTasksQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: hasScope(filters),
  }))

  const invalidate = () =>
    void invalidateMesQueries(queryCache, ['listBusinessConsoleMesOperationTasks']).catch(ignoreBackgroundError)

  const startMutation = useMutation({
    ...startBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: invalidate,
  })
  const pauseMutation = useMutation({
    ...pauseBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: invalidate,
  })
  const resumeMutation = useMutation({
    ...resumeBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: invalidate,
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: invalidate,
  })

  function actionPayload(operationTaskId: string, options: OperationActionOptions) {
    const { reasonCode, idempotencyKey } = options
    return {
      path: { operationTaskId },
      query: scopeQuery(filters),
      body: {
        ...(reasonCode === undefined ? {} : { reasonCode }),
        idempotencyKey,
      } satisfies BusinessConsoleMesOperationTaskActionRequest,
    }
  }

  return {
    filters,
    operationTasks: computed<BusinessConsoleMesOperationTaskRow[]>(() =>
      envelopeItems<BusinessConsoleMesOperationTaskRow, BusinessConsoleMesOperationTaskListEnvelope>(
        operationTasksQuery.data.value,
      ),
    ),
    total: computed(() => envelopeTotal(operationTasksQuery.data.value)),
    pending: operationTasksQuery.isLoading,
    error: operationTasksQuery.error,
    refresh: operationTasksQuery.refetch,
    startTask: (operationTaskId: string, options: OperationActionOptions) =>
      startMutation.mutateAsync(actionPayload(operationTaskId, options)),
    pauseTask: (operationTaskId: string, options: OperationActionOptions) =>
      pauseMutation.mutateAsync(actionPayload(operationTaskId, options)),
    resumeTask: (operationTaskId: string, options: OperationActionOptions) =>
      resumeMutation.mutateAsync(actionPayload(operationTaskId, options)),
    completeTask: (operationTaskId: string, options: OperationActionOptions) =>
      completeMutation.mutateAsync(actionPayload(operationTaskId, options)),
    actionPending: computed(() =>
      startMutation.isLoading.value
      || pauseMutation.isLoading.value
      || resumeMutation.isLoading.value
      || completeMutation.isLoading.value,
    ),
  }
}

export interface CurrentOperationSopFilters extends MesScope {
  operationCode?: string
  workCenterCode?: string | null
  routingCode?: string | null
  routingRevision?: string | null
  asOfDate?: string | null
}

export function useMesCurrentOperationSops() {
  const filters = bindAuthScope(reactive<CurrentOperationSopFilters>({
    organizationId: '',
    environmentId: '',
    operationCode: '',
    workCenterCode: '',
    routingCode: '',
    routingRevision: '',
    asOfDate: '',
  }))
  const enabled = computed(() => hasScope(filters) && Boolean(filters.operationCode?.trim()))

  const currentSopsQuery = useQuery(() => ({
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
  const downloadGrantMutation = useMutation(createBusinessConsoleSopFileDownloadGrantMutationOptions())

  async function createSopFileDownloadGrant(fileId: string): Promise<BusinessConsoleSopFileDownloadGrantResponse | null> {
    const envelope = await downloadGrantMutation.mutateAsync({
      path: { fileId },
      body: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    })
    return envelopeData<BusinessConsoleSopFileDownloadGrantResponse, BusinessConsoleSopFileDownloadGrantEnvelope>(
      envelope as BusinessConsoleSopFileDownloadGrantEnvelope,
    ) ?? null
  }

  return {
    filters,
    currentSops: computed<BusinessConsoleCurrentSopDocumentItem[]>(
      () => envelopeData<NonNullable<BusinessConsoleCurrentSopDocumentsEnvelope['data']>, BusinessConsoleCurrentSopDocumentsEnvelope>(
        currentSopsQuery.data.value as BusinessConsoleCurrentSopDocumentsEnvelope | undefined,
      )?.items ?? [],
    ),
    pending: currentSopsQuery.isLoading,
    error: currentSopsQuery.error,
    refresh: currentSopsQuery.refetch,
    createSopFileDownloadGrant,
  }
}

export type RecordReportInput =
  Omit<BusinessConsoleRecordProductionReportRequest, 'organizationId' | 'environmentId' | 'reportedAtUtc'>

export function useMesProductionReports() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const reportsQuery = useQuery(() => ({
    ...listBusinessConsoleMesProductionReportsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: hasScope(filters),
  }))

  const recordMutation = useMutation({
    ...recordBusinessConsoleMesProductionReportMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesProductionReports',
        'listBusinessConsoleMesWorkOrders',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    filters,
    productionReports: computed<BusinessConsoleMesProductionReportRow[]>(() =>
      envelopeItems<BusinessConsoleMesProductionReportRow, BusinessConsoleMesProductionReportListEnvelope>(
        reportsQuery.data.value,
      ),
    ),
    total: computed(() => envelopeTotal(reportsQuery.data.value)),
    pending: reportsQuery.isLoading,
    error: reportsQuery.error,
    refresh: reportsQuery.refetch,
    recordReport: (input: RecordReportInput) =>
      recordMutation.mutateAsync({
        body: {
          ...input,
          // org/env + timestamp injected LAST from principal scope — never the caller.
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          reportedAtUtc: new Date().toISOString(),
        } satisfies BusinessConsoleRecordProductionReportRequest,
      }),
  }
}

export function useMesTelemetryProductionReportCandidates() {
  const filters = Object.assign(defaultFilters(), { status: 'pending-confirmation' })
  const queryCache = useQueryCache()
  const query = useQuery(() => ({
    ...listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions({ query: {
      organizationId: filters.organizationId, environmentId: filters.environmentId, status: filters.status,
      workCenterId: filters.workCenterId, deviceAssetId: filters.deviceAssetId, skip: filters.skip, take: filters.take,
    } }), enabled: hasScope(filters),
  }))
  const promoteMutation = useMutation({ ...promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions(), onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesTelemetryProductionReportCandidates', 'listBusinessConsoleMesProductionReports']).catch(ignoreBackgroundError) })
  const dismissMutation = useMutation({ ...dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions(), onSuccess: () => void invalidateMesQueries(queryCache, ['listBusinessConsoleMesTelemetryProductionReportCandidates']).catch(ignoreBackgroundError) })
  type CandidateEnvelope = { data?: { items?: BusinessConsoleMesTelemetryCandidateRow[]; total?: number } | null }
  return {
    filters,
    candidates: computed(() => envelopeItems<BusinessConsoleMesTelemetryCandidateRow, CandidateEnvelope>(query.data.value as CandidateEnvelope | undefined)),
    total: computed(() => envelopeTotal(query.data.value as CandidateEnvelope | undefined)),
    pending: query.isLoading,
    promote: (candidateId: string, workOrderId: string, operationTaskId: string) => promoteMutation.mutateAsync({ path: { candidateId }, query: { organizationId: filters.organizationId, environmentId: filters.environmentId }, body: { workOrderId, operationTaskId } }),
    dismiss: (candidateId: string, reason: string) => dismissMutation.mutateAsync({ path: { candidateId }, query: { organizationId: filters.organizationId, environmentId: filters.environmentId }, body: { reason } }),
  }
}

export type CreateIssueInput = BusinessConsoleMesCreateMaterialIssueRequest

export type ConfirmLineSideReceiptInput = BusinessConsoleMesConfirmLineSideReceiptRequest

export function useMesMaterialIssue() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const requestsQuery = useQuery(() => ({
    ...listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: hasScope(filters),
  }))

  const createMutation = useMutation({
    ...createBusinessConsoleMesMaterialIssueRequestMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests']).catch(ignoreBackgroundError)
    },
  })

  const confirmMutation = useMutation({
    ...confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests']).catch(ignoreBackgroundError)
    },
  })

  return {
    filters,
    requests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<BusinessConsoleMesMaterialIssueRequestRow, BusinessConsoleMesMaterialIssueRequestListEnvelope>(
        requestsQuery.data.value,
      ),
    ),
    total: computed(() => envelopeTotal(requestsQuery.data.value)),
    pending: requestsQuery.isLoading,
    error: requestsQuery.error,
    refresh: requestsQuery.refetch,
    createIssue: (workOrderId: string, body: CreateIssueInput) =>
      createMutation.mutateAsync({
        path: { workOrderId },
        query: scopeQuery(filters),
        body: { ...body } satisfies BusinessConsoleMesCreateMaterialIssueRequest,
      }),
    confirmLineSideReceipt: (requestId: string, body: ConfirmLineSideReceiptInput) =>
      confirmMutation.mutateAsync({
        path: { requestId },
        query: scopeQuery(filters),
        body: { ...body } satisfies BusinessConsoleMesConfirmLineSideReceiptRequest,
      }),
  }
}

export type CreateReceiptInput =
  Omit<BusinessConsoleMesCreateReceiptRequest, 'organizationId' | 'environmentId' | 'requestedAtUtc'>

export function useMesReceipts() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const receiptsQuery = useQuery(() => ({
    ...listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: hasScope(filters),
  }))

  const createMutation = useMutation({
    ...createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesFinishedGoodsReceiptRequests']).catch(ignoreBackgroundError)
    },
  })

  return {
    filters,
    receipts: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<BusinessConsoleMesReceiptRequestRow, BusinessConsoleMesReceiptRequestListEnvelope>(
        receiptsQuery.data.value,
      ),
    ),
    total: computed(() => envelopeTotal(receiptsQuery.data.value)),
    pending: receiptsQuery.isLoading,
    error: receiptsQuery.error,
    refresh: receiptsQuery.refetch,
    createReceipt: (input: CreateReceiptInput) =>
      createMutation.mutateAsync({
        body: {
          ...input,
          // org/env + timestamp injected LAST from principal scope — never the caller.
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          requestedAtUtc: new Date().toISOString(),
        } satisfies BusinessConsoleMesCreateReceiptRequest,
      }),
  }
}
