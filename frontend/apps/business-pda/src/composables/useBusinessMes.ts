import {
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleOperation,
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  createBusinessConsoleSopFileDownloadGrantMutationOptions,
  getBusinessConsolePrincipalWorkContextQueryOptions,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  getBusinessConsoleMesWorkOrderDetailQueryKey,
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequests,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesOperationTasks,
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
  type BusinessConsoleMesWorkOrderDetailEnvelope,
  type BusinessConsoleMesWorkOrderDetailResponse,
  type BusinessConsoleMesWorkOrderListEnvelope,
  type BusinessConsoleRecordProductionReportRequest,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { useListFreshness } from '@/composables/useListFreshness'
import { computed, reactive, watch, watchEffect, type Ref } from 'vue'
import { assertLifecycleActionExecutable } from '@/composables/lifecycleActionRecovery'
import { useAuthStore } from '@/stores/auth'

const DEFAULT_TAKE = 100
const MES_OPERATIONS_MANAGE_PERMISSION = 'business.mes.operations.manage'
const MES_REPORTING_WRITE_PERMISSION = 'business.mes.reporting.write'

export const MES_WORK_SCOPE_REQUIRED_MESSAGE =
  '尚未选择已授权作业范围，当前操作已禁用。请刷新范围后重试。'

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
  return bindAuthScope(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )
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

interface MesSelectedWorkScope {
  kind: string
  id: string
  displayName?: string
}

function useMesPrincipalWorkScope(scope: MesScope, permissionCode: string) {
  const workContextQuery = useQuery(() => ({
    ...getBusinessConsolePrincipalWorkContextQueryOptions({
      query: {
        organizationId: scope.organizationId,
        environmentId: scope.environmentId,
        permissionCode,
      },
    }),
    enabled: hasScope(scope),
  }))
  const selectedScope = computed<MesSelectedWorkScope | undefined>(() => {
    const envelope = workContextQuery.data.value
    if (!envelope?.success) return undefined
    const selection = envelope.data?.selectedScope
    const kind = selection?.kind?.trim()
    const id = selection?.id?.trim()
    const displayName = selection?.displayName?.trim()
    if (!kind || !id) return undefined
    return {
      kind,
      id,
      ...(displayName ? { displayName } : {}),
    }
  })
  const scopeReady = computed(() => selectedScope.value !== undefined)
  const scopeMessage = computed(() => {
    if (!hasScope(scope)) return '尚未进入有效组织与环境，当前操作已禁用。'
    if (workContextQuery.isLoading.value) return '正在核验当前作业范围…'
    if (workContextQuery.error.value) return '作业范围核验失败，当前操作已禁用。请刷新后重试。'
    return scopeReady.value ? '' : MES_WORK_SCOPE_REQUIRED_MESSAGE
  })

  function requireSelectedScope() {
    const selection = selectedScope.value
    if (!selection) throw new Error(scopeMessage.value || MES_WORK_SCOPE_REQUIRED_MESSAGE)
    return selection
  }

  return {
    requireSelectedScope,
    selectedScope,
    scopeMessage,
    scopePending: workContextQuery.isLoading,
    scopeReady,
  }
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

function envelopeData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return undefined
  }
  return envelope.data ?? undefined
}

function exactItem<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
  matches: (item: TItem) => boolean,
) {
  const matchesById = (envelope?.success ? (envelope.data?.items ?? []) : []).filter(matches)
  return matchesById.length === 1 ? matchesById[0] : undefined
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
  const lastUpdatedAt = useListFreshness(
    () => workOrdersQuery.data.value,
    () => hasScope(filters),
  )

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
    lastUpdatedAt,
    refresh: () => (hasScope(filters) ? workOrdersQuery.refetch() : Promise.resolve()),
  }
}

export function useMesWorkOrderDetail(workOrderId: Readonly<Ref<string>>) {
  const scope = bindAuthScope(reactive({ organizationId: '', environmentId: '' }))
  const queryCache = useQueryCache()
  const detailEnabled = computed(() => hasScope(scope) && workOrderId.value.trim() !== '')
  watch(
    () => [workOrderId.value.trim(), scope.organizationId, scope.environmentId] as const,
    (_current, previous) => {
      const [previousWorkOrderId, organizationId, environmentId] = previous ?? []
      if (!previousWorkOrderId || !organizationId || !environmentId) return
      void queryCache.cancelQueries({
        key: getBusinessConsoleMesWorkOrderDetailQueryKey({
          path: { workOrderId: previousWorkOrderId },
          query: { organizationId, environmentId },
        }),
        exact: true,
      })
    },
    { flush: 'sync' },
  )
  const detailQuery = useQuery(() => {
    const requestedId = workOrderId.value.trim()
    return {
      ...getBusinessConsoleMesWorkOrderDetailQueryOptions({
        path: { workOrderId: requestedId },
        query: scopeQuery(scope),
      }),
      enabled: detailEnabled.value,
    }
  })
  const lastUpdatedAt = useListFreshness(() => detailQuery.data.value, detailEnabled)

  return {
    workOrder: computed<BusinessConsoleMesWorkOrderDetailResponse | undefined>(() =>
      envelopeData<
        BusinessConsoleMesWorkOrderDetailResponse,
        BusinessConsoleMesWorkOrderDetailEnvelope
      >(detailQuery.data.value),
    ),
    pending: detailQuery.isLoading,
    error: detailQuery.error,
    lastUpdatedAt,
    refresh: detailQuery.refetch,
  }
}

const EXACT_TASK_PAGE_SIZE = 100

function exactOperationTaskQueryKey(
  organizationId: string,
  environmentId: string,
  workOrderId: string,
  operationTaskId: string,
) {
  return [
    'mes-report-exact-operation-task',
    organizationId,
    environmentId,
    workOrderId,
    operationTaskId,
  ] as const
}

export function useMesExactOperationTask(
  workOrderId: Readonly<Ref<string>>,
  operationTaskId: Readonly<Ref<string>>,
  detail: Readonly<Ref<BusinessConsoleMesWorkOrderDetailResponse | null | undefined>>,
) {
  const scope = bindAuthScope(reactive({ organizationId: '', environmentId: '' }))
  const queryCache = useQueryCache()
  watch(
    () =>
      [
        scope.organizationId,
        scope.environmentId,
        workOrderId.value.trim(),
        operationTaskId.value.trim(),
      ] as const,
    (_current, previous) => {
      const [organizationId, environmentId, requestedWorkOrderId, requestedTaskId] = previous ?? []
      if (!organizationId || !environmentId || !requestedWorkOrderId || !requestedTaskId) return
      void queryCache.cancelQueries({
        key: exactOperationTaskQueryKey(
          organizationId,
          environmentId,
          requestedWorkOrderId,
          requestedTaskId,
        ),
        exact: true,
      })
    },
    { flush: 'sync' },
  )
  const enabled = computed(() => {
    const requestedWorkOrderId = workOrderId.value.trim()
    const requestedTaskId = operationTaskId.value.trim()
    return (
      hasScope(scope) &&
      requestedWorkOrderId !== '' &&
      requestedTaskId !== '' &&
      detail.value?.workOrderId === requestedWorkOrderId &&
      !detail.value.operationTasks?.some(
        (task) =>
          task.workOrderId === requestedWorkOrderId && task.operationTaskId === requestedTaskId,
      )
    )
  })
  const query = useQuery(() => ({
    key: exactOperationTaskQueryKey(
      scope.organizationId,
      scope.environmentId,
      workOrderId.value.trim(),
      operationTaskId.value.trim(),
    ),
    enabled: enabled.value,
    query: async ({ signal }) => {
      const organizationId = scope.organizationId
      const environmentId = scope.environmentId
      const requestedWorkOrderId = workOrderId.value.trim()
      const requestedTaskId = operationTaskId.value.trim()
      let skip = 0
      while (true) {
        const response = await listBusinessConsoleMesOperationTasks({
          query: {
            organizationId,
            environmentId,
            workOrderId: requestedWorkOrderId,
            skip,
            take: EXACT_TASK_PAGE_SIZE,
          },
          signal,
        })
        const envelope = response.data
        if (!envelope?.success || !envelope.data) {
          throw new Error(envelope?.message?.trim() || '工序任务精确查询失败。')
        }
        const items = envelope.data.items ?? []
        const match = items.find(
          (task) =>
            task.workOrderId === requestedWorkOrderId && task.operationTaskId === requestedTaskId,
        )
        if (match) return match
        skip += items.length
        if (
          items.length < EXACT_TASK_PAGE_SIZE ||
          (envelope.data.total !== undefined && skip >= envelope.data.total)
        ) {
          break
        }
      }
      return null
    },
  }))

  return {
    task: query.data,
    pending: query.isLoading,
    error: query.error,
    refresh: query.refetch,
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

type OperationAction = 'start' | 'pause' | 'resume' | 'complete'

async function readExactOperationTask(
  filters: MesScope,
  operationTaskId: string,
  workOrderId?: string,
): Promise<BusinessConsoleMesOperationTaskRow | undefined> {
  const { data } = await listBusinessConsoleMesOperationTasks({
    query: {
      ...scopeQuery(filters),
      ...(workOrderId ? { workOrderId } : {}),
      keyword: operationTaskId,
      skip: 0,
      take: 2,
    },
    throwOnError: true,
  })
  return exactItem(
    data as BusinessConsoleMesOperationTaskListEnvelope | undefined,
    (item: BusinessConsoleMesOperationTaskRow) =>
      item.operationTaskId === operationTaskId &&
      (!workOrderId || item.workOrderId === workOrderId),
  )
}

export function useMesOperationTasks() {
  const auth = useAuthStore()
  const filters = defaultFilters()
  const operationScope = useMesPrincipalWorkScope(filters, MES_OPERATIONS_MANAGE_PERMISSION)
  const queryCache = useQueryCache()

  const operationTasksQuery = useQuery(() => ({
    ...listBusinessConsoleMesOperationTasksQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: hasScope(filters),
  }))
  const lastUpdatedAt = useListFreshness(
    () => operationTasksQuery.data.value,
    () => hasScope(filters),
  )

  const invalidate = () =>
    void invalidateMesQueries(queryCache, ['listBusinessConsoleMesOperationTasks']).catch(
      ignoreBackgroundError,
    )

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

  function actionPayload(
    operationTaskId: string,
    selectedScope: MesSelectedWorkScope,
    options: OperationActionOptions,
  ) {
    const { reasonCode, idempotencyKey } = options
    return {
      path: { operationTaskId },
      query: {
        ...scopeQuery(filters),
        scopeKind: selectedScope.kind,
        scopeId: selectedScope.id,
      },
      body: {
        ...(reasonCode === undefined ? {} : { reasonCode }),
        idempotencyKey,
      } satisfies BusinessConsoleMesOperationTaskActionRequest,
    }
  }

  async function performAction(
    action: OperationAction,
    mutation: typeof startMutation,
    operationTaskId: string,
    options: OperationActionOptions,
  ) {
    const selectedScope = operationScope.requireSelectedScope()
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      operationType: `mes.operation-task.${action}`,
      payloadFingerprint: `${operationTaskId}:${selectedScope.kind}:${selectedScope.id}:${options.reasonCode ?? ''}`,
    }
    const isReplay = Boolean(peekPendingBusinessIntent(scope))
    const pending = acquirePendingBusinessIntent(scope, () => options.idempotencyKey)
    try {
      const authoritative = await readExactOperationTask(filters, operationTaskId)
      assertLifecycleActionExecutable({
        domain: 'mes-operation-task',
        action,
        facts: { status: authoritative?.status, idempotentReplay: isReplay },
      })
    } catch (error) {
      if (!isReplay) clearPendingBusinessIntent(scope)
      throw error
    }
    return completePendingBusinessIntent(scope, async () =>
      confirmBusinessConsoleOperation(
        await mutation.mutateAsync(
          actionPayload(operationTaskId, selectedScope, {
            ...options,
            idempotencyKey: pending.idempotencyKey,
          }),
        ),
        {
          expectedOperationType: `mes.operation-task.${action}`,
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: operationTaskId,
        },
      ),
    )
  }

  return {
    filters,
    operationTasks: computed<BusinessConsoleMesOperationTaskRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesOperationTaskRow,
        BusinessConsoleMesOperationTaskListEnvelope
      >(operationTasksQuery.data.value),
    ),
    total: computed(() => envelopeTotal(operationTasksQuery.data.value)),
    pending: operationTasksQuery.isLoading,
    error: operationTasksQuery.error,
    operationScopeMessage: operationScope.scopeMessage,
    operationScopePending: operationScope.scopePending,
    operationScopeReady: operationScope.scopeReady,
    lastUpdatedAt,
    refresh: () => (hasScope(filters) ? operationTasksQuery.refetch() : Promise.resolve()),
    cancelPendingTasks: () =>
      queryCache.cancelQueries({
        predicate: isBusinessQuery('listBusinessConsoleMesOperationTasks'),
      }),
    startTask: (operationTaskId: string, options: OperationActionOptions) =>
      performAction('start', startMutation, operationTaskId, options),
    pauseTask: (operationTaskId: string, options: OperationActionOptions) =>
      performAction('pause', pauseMutation, operationTaskId, options),
    resumeTask: (operationTaskId: string, options: OperationActionOptions) =>
      performAction('resume', resumeMutation, operationTaskId, options),
    completeTask: (operationTaskId: string, options: OperationActionOptions) =>
      performAction('complete', completeMutation, operationTaskId, options),
    actionPending: computed(
      () =>
        startMutation.isLoading.value ||
        pauseMutation.isLoading.value ||
        resumeMutation.isLoading.value ||
        completeMutation.isLoading.value,
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
  const filters = bindAuthScope(
    reactive<CurrentOperationSopFilters>({
      organizationId: '',
      environmentId: '',
      operationCode: '',
      workCenterCode: '',
      routingCode: '',
      routingRevision: '',
      asOfDate: '',
    }),
  )
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
      envelopeData<
        BusinessConsoleSopFileDownloadGrantResponse,
        BusinessConsoleSopFileDownloadGrantEnvelope
      >(envelope as BusinessConsoleSopFileDownloadGrantEnvelope) ?? null
    )
  }

  return {
    filters,
    currentSops: computed<BusinessConsoleCurrentSopDocumentItem[]>(
      () =>
        envelopeData<
          NonNullable<BusinessConsoleCurrentSopDocumentsEnvelope['data']>,
          BusinessConsoleCurrentSopDocumentsEnvelope
        >(currentSopsQuery.data.value as BusinessConsoleCurrentSopDocumentsEnvelope | undefined)
          ?.items ?? [],
    ),
    pending: currentSopsQuery.isLoading,
    error: currentSopsQuery.error,
    refresh: currentSopsQuery.refetch,
    createSopFileDownloadGrant,
  }
}

export type RecordReportInput = Omit<
  BusinessConsoleRecordProductionReportRequest,
  'organizationId' | 'environmentId' | 'reportedAtUtc' | 'scopeKind' | 'scopeId'
>

export function useMesProductionReports() {
  const auth = useAuthStore()
  const filters = defaultFilters()
  const reportScope = useMesPrincipalWorkScope(filters, MES_REPORTING_WRITE_PERMISSION)
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
      envelopeItems<
        BusinessConsoleMesProductionReportRow,
        BusinessConsoleMesProductionReportListEnvelope
      >(reportsQuery.data.value),
    ),
    total: computed(() => envelopeTotal(reportsQuery.data.value)),
    pending: reportsQuery.isLoading,
    error: reportsQuery.error,
    refresh: reportsQuery.refetch,
    reportScopeMessage: reportScope.scopeMessage,
    reportScopePending: reportScope.scopePending,
    reportScopeReady: reportScope.scopeReady,
    recordReport: async (input: RecordReportInput) => {
      const selectedScope = reportScope.requireSelectedScope()
      const { idempotencyKey: suppliedKey, ...payload } = input
      const scope = {
        principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        operationType: 'mes.production-report.record',
        payloadFingerprint: JSON.stringify({
          ...payload,
          scopeKind: selectedScope.kind,
          scopeId: selectedScope.id,
        }),
      }
      const isReplay = Boolean(peekPendingBusinessIntent(scope))
      const currentPayload = {
        ...input,
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        reportedAtUtc: new Date().toISOString(),
        scopeKind: selectedScope.kind,
        scopeId: selectedScope.id,
      } satisfies BusinessConsoleRecordProductionReportRequest
      const pending = acquirePendingBusinessIntent(
        scope,
        () =>
          suppliedKey?.trim() ||
          globalThis.crypto?.randomUUID?.() ||
          `mes-report-${Date.now()}-${Math.random()}`,
        currentPayload,
      )
      if (input.completesOperation) {
        try {
          const workOrderId = input.workOrderId?.trim()
          const operationTaskId = input.operationTaskId?.trim()
          const authoritative = operationTaskId
            ? await readExactOperationTask(filters, operationTaskId, workOrderId)
            : undefined
          assertLifecycleActionExecutable({
            domain: 'mes-operation-task',
            action: 'report-complete',
            facts: {
              status:
                authoritative?.workOrderId === workOrderId ? authoritative?.status : undefined,
              idempotentReplay: isReplay,
            },
          })
        } catch (error) {
          if (!isReplay) clearPendingBusinessIntent(scope)
          throw error
        }
      }
      const frozenPayload =
        pending.payloadSnapshot !== undefined
          ? (pending.payloadSnapshot as BusinessConsoleRecordProductionReportRequest)
          : currentPayload
      return completePendingBusinessIntent(scope, async () =>
        confirmBusinessConsoleOperation(
          await recordMutation.mutateAsync({
            body: {
              ...frozenPayload,
              idempotencyKey: pending.idempotencyKey,
            },
          }),
          {
            expectedOperationType: 'mes.production-report.record',
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceIdSelector: (envelope) => envelope.data?.productionReportId,
          },
        ),
      )
    },
  }
}

export function useMesTelemetryProductionReportCandidates() {
  const filters = Object.assign(defaultFilters(), { status: 'pending-confirmation' })
  const queryCache = useQueryCache()
  const query = useQuery(() => ({
    ...listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        status: filters.status,
        workCenterId: filters.workCenterId,
        deviceAssetId: filters.deviceAssetId,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasScope(filters),
  }))
  const promoteMutation = useMutation({
    ...promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesTelemetryProductionReportCandidates',
        'listBusinessConsoleMesProductionReports',
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
        query.data.value as CandidateEnvelope | undefined,
      ),
    ),
    total: computed(() => envelopeTotal(query.data.value as CandidateEnvelope | undefined)),
    pending: query.isLoading,
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
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests']).catch(
        ignoreBackgroundError,
      )
    },
  })

  const confirmMutation = useMutation({
    ...confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests']).catch(
        ignoreBackgroundError,
      )
    },
  })

  return {
    filters,
    requests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesMaterialIssueRequestRow,
        BusinessConsoleMesMaterialIssueRequestListEnvelope
      >(requestsQuery.data.value),
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
    confirmLineSideReceipt: async (
      requestId: string,
      body: ConfirmLineSideReceiptInput,
      context: { workOrderId?: string } = {},
    ) => {
      let skip = 0
      let authoritative: BusinessConsoleMesMaterialIssueRequestRow | undefined
      while (!authoritative) {
        const { data } = await listBusinessConsoleMesMaterialIssueRequests({
          query: {
            ...scopeQuery(filters),
            ...(context.workOrderId?.trim() ? { workOrderId: context.workOrderId.trim() } : {}),
            skip,
            take: DEFAULT_TAKE,
          },
          throwOnError: true,
        })
        const envelope = data as BusinessConsoleMesMaterialIssueRequestListEnvelope | undefined
        authoritative = exactItem(
          envelope,
          (item: BusinessConsoleMesMaterialIssueRequestRow) => item.requestId === requestId,
        )
        if (authoritative) break
        const items = envelope?.success ? (envelope.data?.items ?? []) : []
        const total = envelope?.success ? (envelope.data?.total ?? 0) : 0
        if (items.length === 0 || skip + items.length >= total) break
        skip += items.length
      }
      assertLifecycleActionExecutable({
        domain: 'mes-material-issue',
        action: 'confirm-receipt',
        facts: { status: authoritative?.status },
      })
      return confirmMutation.mutateAsync({
        path: { requestId },
        query: scopeQuery(filters),
        body: { ...body } satisfies BusinessConsoleMesConfirmLineSideReceiptRequest,
      })
    },
  }
}

export type CreateReceiptInput = Omit<
  BusinessConsoleMesCreateReceiptRequest,
  'organizationId' | 'environmentId' | 'requestedAtUtc'
>

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
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesFinishedGoodsReceiptRequests',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    filters,
    receipts: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesReceiptRequestRow,
        BusinessConsoleMesReceiptRequestListEnvelope
      >(receiptsQuery.data.value),
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
