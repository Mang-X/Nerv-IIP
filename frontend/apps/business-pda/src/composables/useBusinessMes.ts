import {
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  pauseBusinessConsoleMesOperationTaskMutationOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  resumeBusinessConsoleMesOperationTaskMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
  type BusinessConsoleMesConfirmLineSideReceiptRequest,
  type BusinessConsoleMesCreateMaterialIssueRequest,
  type BusinessConsoleMesCreateReceiptRequest,
  type BusinessConsoleMesMaterialIssueRequestListEnvelope,
  type BusinessConsoleMesMaterialIssueRequestRow,
  type BusinessConsoleMesOperationTaskListEnvelope,
  type BusinessConsoleMesOperationTaskRow,
  type BusinessConsoleMesProductionReportListEnvelope,
  type BusinessConsoleMesProductionReportRow,
  type BusinessConsoleMesReceiptRequestListEnvelope,
  type BusinessConsoleMesReceiptRequestRow,
  type BusinessConsoleMesWorkOrderItem,
  type BusinessConsoleMesWorkOrderListEnvelope,
  type BusinessConsoleRecordProductionReportRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, watchEffect } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { makeIdempotencyKey } from './makeIdempotencyKey'

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

  function actionPayload(operationTaskId: string, reasonCode?: string) {
    return {
      path: { operationTaskId },
      query: scopeQuery(filters),
      body: {
        ...(reasonCode === undefined ? {} : { reasonCode }),
        idempotencyKey: makeIdempotencyKey(),
      },
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
    startTask: (operationTaskId: string, reasonCode?: string) =>
      startMutation.mutateAsync(actionPayload(operationTaskId, reasonCode)),
    pauseTask: (operationTaskId: string, reasonCode?: string) =>
      pauseMutation.mutateAsync(actionPayload(operationTaskId, reasonCode)),
    resumeTask: (operationTaskId: string, reasonCode?: string) =>
      resumeMutation.mutateAsync(actionPayload(operationTaskId, reasonCode)),
    completeTask: (operationTaskId: string, reasonCode?: string) =>
      completeMutation.mutateAsync(actionPayload(operationTaskId, reasonCode)),
    actionPending: computed(() =>
      startMutation.isLoading.value
      || pauseMutation.isLoading.value
      || resumeMutation.isLoading.value
      || completeMutation.isLoading.value,
    ),
  }
}

export type RecordReportInput =
  Pick<BusinessConsoleRecordProductionReportRequest, 'workOrderId' | 'operationTaskId' | 'goodQuantity' | 'scrapQuantity' | 'completesOperation'>
  & Partial<BusinessConsoleRecordProductionReportRequest>

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
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          reportedAtUtc: new Date().toISOString(),
          idempotencyKey: makeIdempotencyKey(),
          ...input,
        } satisfies BusinessConsoleRecordProductionReportRequest,
      }),
  }
}

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
    refresh: requestsQuery.refetch,
    createIssue: (workOrderId: string, body: BusinessConsoleMesCreateMaterialIssueRequest) =>
      createMutation.mutateAsync({
        path: { workOrderId },
        query: scopeQuery(filters),
        body: { idempotencyKey: makeIdempotencyKey(), ...body },
      }),
    confirmLineSideReceipt: (requestId: string, body: BusinessConsoleMesConfirmLineSideReceiptRequest) =>
      confirmMutation.mutateAsync({
        path: { requestId },
        query: scopeQuery(filters),
        body: { idempotencyKey: makeIdempotencyKey(), ...body },
      }),
  }
}

export type CreateReceiptInput =
  Pick<BusinessConsoleMesCreateReceiptRequest, 'workOrderId' | 'skuId' | 'quantity' | 'uomCode'>
  & Partial<BusinessConsoleMesCreateReceiptRequest>

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
    refresh: receiptsQuery.refetch,
    createReceipt: (input: CreateReceiptInput) =>
      createMutation.mutateAsync({
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          requestedAtUtc: new Date().toISOString(),
          idempotencyKey: makeIdempotencyKey(),
          ...input,
        } satisfies BusinessConsoleMesCreateReceiptRequest,
      }),
  }
}
