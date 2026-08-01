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
  listBusinessConsoleMesReportableOperationTasks,
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
  type BusinessConsoleMesWorkOrderDetailResponse,
  type BusinessConsoleMesWorkOrderListEnvelope,
  type BusinessConsoleRecordProductionReportRequest,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  formatWorkScopeKey,
  parseWorkScopeKey,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from '@/composables/useListFreshness'
import { computed, reactive, shallowRef, watch, watchEffect, type Ref } from 'vue'
import { assertLifecycleActionExecutable } from '@/composables/lifecycleActionRecovery'
import { useAuthStore } from '@/stores/auth'
import { useTaskListPagination } from './useTaskListPagination'

const DEFAULT_TAKE = 100
const TASK_LIST_PAGE_SIZE = 20
const MES_OPERATIONS_READ_PERMISSION = 'business.mes.operations.read'
const MES_OPERATIONS_MANAGE_PERMISSION = 'business.mes.operations.manage'
const MES_REPORTING_READ_PERMISSION = 'business.mes.reporting.read'
const MES_REPORTING_WRITE_PERMISSION = 'business.mes.reporting.write'
const MES_WORK_ORDERS_READ_PERMISSION = 'business.mes.work-orders.read'

export const MES_WORK_SCOPE_REQUIRED_MESSAGE =
  '尚未选择已授权作业范围，当前操作已禁用。请在页面顶部的「作业范围」中选择后继续。'

export const MES_WORK_SCOPE_UNAVAILABLE_MESSAGE =
  '当前账号在本组织没有已授权的作业范围，无法读取现场数据。' +
  '请联系管理员在 IAM 为该账号配置数据范围（组织/车间/工作中心/班组/本人）后重新登录。'

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

export interface MesWorkScopeOption {
  label: string
  value: string
}

const MES_WORK_SCOPE_KIND_LABELS: Record<string, string> = {
  self: '本人',
  team: '班组',
  'work-center': '工作中心',
  workshop: '车间',
  organization: '组织',
}

export function mesWorkScopeKindLabel(kind: string) {
  return MES_WORK_SCOPE_KIND_LABELS[kind] ?? kind
}

// 同一 principal/org/env 的作业范围选择在整个 PDA 共享（工序执行/报工口径一致），
// 用户显式选择会记住（localStorage）——PDA 是交班轮用的手持设备，重开 App 不该丢掉刚选的范围；
// 自动兜底选择不写入，避免把兜底固化成偏好。
const MES_WORK_SCOPE_STORAGE_PREFIX = 'nerv-iip.business-pda.mes-work-scope.v1'
const sharedMesWorkScopeSelections = reactive(new Map<string, string>())

function readRememberedMesWorkScope(selectionKey: string) {
  try {
    return (
      globalThis.localStorage?.getItem(`${MES_WORK_SCOPE_STORAGE_PREFIX}:${selectionKey}`) ??
      undefined
    )
  } catch {
    return undefined
  }
}

function writeRememberedMesWorkScope(selectionKey: string, value: string) {
  try {
    globalThis.localStorage?.setItem(`${MES_WORK_SCOPE_STORAGE_PREFIX}:${selectionKey}`, value)
  } catch {
    // 持久化失败只影响「记住选择」，不影响本次会话内的共享选择。
  }
}

function useMesPrincipalWorkScope(scope: MesScope, permissionCode: string) {
  const auth = useAuthStore()
  const principalIdentity = computed(
    () => auth.principal?.principalId?.trim() || auth.sessionId?.trim() || 'unrestored-session',
  )
  const selectionKey = computed(() =>
    [principalIdentity.value, scope.organizationId.trim(), scope.environmentId.trim()].join(':'),
  )

  // 该权限下最近一次成功响应的授权范围清单。请求参数与它相互依赖（选择来自清单、清单来自响应），
  // 必须用「最后已知值」而不是当前查询的 data：否则带选择的请求处于加载态时 data 为空，
  // 选择会被清空、查询键回退，来回震荡。principal/org/env 变化时清空重来。
  const knownAuthorizedScopes = shallowRef<MesSelectedWorkScope[]>([])
  watch(selectionKey, () => {
    knownAuthorizedScopes.value = []
  })

  // 期望选择：记住的选择仍在授权清单里就用它，否则回退清单第一项（与 useWmsWorkScope 同姿势）。
  // 只会请求「当前授权清单里存在」的范围，越权选择不可能被发出（后端仍会独立核验并 403 兜底）。
  const requestedScope = computed<MesSelectedWorkScope | undefined>(() => {
    const scopes = knownAuthorizedScopes.value
    if (scopes.length === 0) return undefined
    const storedValue =
      sharedMesWorkScopeSelections.get(selectionKey.value) ??
      readRememberedMesWorkScope(selectionKey.value)
    const stored = parseWorkScopeKey(storedValue)
    const remembered = stored
      ? scopes.find((candidate) => candidate.kind === stored.kind && candidate.id === stored.id)
      : undefined
    return remembered ?? scopes[0]
  })

  const workContextQuery = useQuery(() => {
    const requested = requestedScope.value
    const options = getBusinessConsolePrincipalWorkContextQueryOptions({
      query: {
        organizationId: scope.organizationId,
        environmentId: scope.environmentId,
        permissionCode,
        ...(requested ? { scopeKind: requested.kind, scopeId: requested.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal:${principalIdentity.value}`],
      enabled: hasScope(scope),
    }
  })

  watch(
    () => workContextQuery.data.value,
    (envelope) => {
      if (!envelope?.success) return
      knownAuthorizedScopes.value = (envelope.data?.authorizedScopes ?? [])
        .map((candidate) => {
          const kind = candidate.kind?.trim() ?? ''
          const id = candidate.id?.trim() ?? ''
          const displayName = candidate.displayName?.trim()
          return { kind, id, ...(displayName ? { displayName } : {}) }
        })
        .filter((candidate) => candidate.kind && candidate.id)
    },
    { immediate: true },
  )

  const scopeOptions = computed<MesWorkScopeOption[]>(() =>
    knownAuthorizedScopes.value.map((candidate) => ({
      label: `${candidate.displayName || candidate.id}（${mesWorkScopeKindLabel(candidate.kind)}）`,
      value: formatWorkScopeKey(candidate.kind, candidate.id),
    })),
  )
  const scopeSelectionValue = computed<string | undefined>({
    get: () =>
      requestedScope.value
        ? formatWorkScopeKey(requestedScope.value.kind, requestedScope.value.id)
        : undefined,
    set: (value) => {
      if (!value) return
      sharedMesWorkScopeSelections.set(selectionKey.value, value)
      writeRememberedMesWorkScope(selectionKey.value, value)
    },
  })

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
  // 「读到了、但确实一个授权范围都没有」——与「还没选」是两回事，提示必须说清缺什么、去哪配。
  const scopeUnavailable = computed(
    () =>
      !workContextQuery.isLoading.value &&
      !workContextQuery.error.value &&
      workContextQuery.data.value?.success === true &&
      knownAuthorizedScopes.value.length === 0 &&
      !scopeReady.value,
  )
  const scopeMessage = computed(() => {
    if (!hasScope(scope)) return '尚未进入有效组织与环境，当前操作已禁用。'
    if (workContextQuery.isLoading.value) return '正在核验当前作业范围…'
    if (workContextQuery.error.value) return '作业范围核验失败，当前操作已禁用。请刷新后重试。'
    if (scopeReady.value) return ''
    if (scopeUnavailable.value) return MES_WORK_SCOPE_UNAVAILABLE_MESSAGE
    return MES_WORK_SCOPE_REQUIRED_MESSAGE
  })

  function requireSelectedScope() {
    const selection = selectedScope.value
    if (!selection) throw new Error(scopeMessage.value || MES_WORK_SCOPE_REQUIRED_MESSAGE)
    return selection
  }

  return {
    principalIdentity,
    requireSelectedScope,
    selectedScope,
    scopeMessage,
    scopeOptions,
    scopePending: workContextQuery.isLoading,
    scopeReady,
    scopeSelectionValue,
    scopeUnavailable,
  }
}

/**
 * 作业范围选择入口用的独立实例：work-context 查询按 key 去重，
 * 与工序执行/报工各 composable 内部的同权限实例共享同一次请求与同一份共享选择。
 */
export function useMesWorkScopeSelection(permissionCode: string) {
  return useMesPrincipalWorkScope(
    bindAuthScope(reactive({ organizationId: '', environmentId: '' })),
    permissionCode,
  )
}

function scopeKey(filters: MesScope) {
  return `${filters.organizationId.trim()}:${filters.environmentId.trim()}`
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

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isNonBlankString(value: unknown): value is string {
  return typeof value === 'string' && value.trim() === value && value.length > 0
}

function isWorkOrderOperationTask(value: unknown, requestedWorkOrderId: string) {
  if (!isRecord(value)) return false
  return (
    isNonBlankString(value.operationTaskId) &&
    value.workOrderId === requestedWorkOrderId &&
    isNonBlankString(value.status) &&
    typeof value.operationSequence === 'number' &&
    Number.isInteger(value.operationSequence) &&
    isNonBlankString(value.workCenterId) &&
    isNonBlankString(value.qualityStatus)
  )
}

function isWorkOrderDetail(value: unknown, requestedWorkOrderId: string) {
  if (!isRecord(value)) return false
  return (
    value.workOrderId === requestedWorkOrderId &&
    isNonBlankString(value.skuId) &&
    typeof value.quantity === 'number' &&
    Number.isFinite(value.quantity) &&
    isNonBlankString(value.status) &&
    isNonBlankString(value.readinessStatus) &&
    Array.isArray(value.blockingReasons) &&
    value.blockingReasons.every((reason) => typeof reason === 'string') &&
    Array.isArray(value.operationTasks) &&
    value.operationTasks.every((task) => isWorkOrderOperationTask(task, requestedWorkOrderId))
  )
}

function bindWorkOrderDetailResponse(value: unknown, requestedWorkOrderId: string) {
  if (value === undefined) return undefined
  if (isRecord(value) && value.success === true && isRecord(value.data)) {
    const responseWorkOrderId = value.data.workOrderId
    if (isNonBlankString(responseWorkOrderId) && responseWorkOrderId !== requestedWorkOrderId) {
      return undefined
    }
    if (isWorkOrderDetail(value.data, requestedWorkOrderId)) return value
  }
  const message =
    isRecord(value) && typeof value.message === 'string' && value.message.trim()
      ? value.message.trim()
      : '工单详情响应无效，请重试。'
  return { success: false, message }
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
  const workOrderReadScope = useMesPrincipalWorkScope(filters, MES_WORK_ORDERS_READ_PERMISSION)
  const scopeReady = computed(() => hasScope(filters) && workOrderReadScope.scopeReady.value)
  const workOrdersIdentity = computed(() => {
    const selectedScope = workOrderReadScope.selectedScope.value
    return [
      workOrderReadScope.principalIdentity.value,
      filters.organizationId.trim(),
      filters.environmentId.trim(),
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
    ].join(':')
  })

  const workOrdersQuery = useQuery(() => {
    const selectedScope = workOrderReadScope.selectedScope.value
    const options = listBusinessConsoleMesWorkOrdersQueryOptions({
      query: {
        ...toListQuery(filters),
        ...(selectedScope ? { scopeKind: selectedScope.kind, scopeId: selectedScope.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal-scope:${workOrdersIdentity.value}`],
      enabled: scopeReady.value,
    }
  })
  const currentResponse = useScopeBoundListResponse(
    () => workOrdersQuery.data.value,
    workOrdersIdentity,
    scopeReady,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scopeReady)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    workOrdersQuery.isLoading,
  )

  return {
    filters,
    workOrders: computed<BusinessConsoleMesWorkOrderItem[]>(() =>
      envelopeItems<BusinessConsoleMesWorkOrderItem, BusinessConsoleMesWorkOrderListEnvelope>(
        currentResponse.value,
      ),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: workOrdersQuery.isLoading,
    error: workOrdersQuery.error,
    workOrderReadScope: workOrderReadScope.selectedScope,
    workOrderReadScopeMessage: workOrderReadScope.scopeMessage,
    workOrderReadScopePending: workOrderReadScope.scopePending,
    workOrderReadScopeReady: workOrderReadScope.scopeReady,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (scopeReady.value ? workOrdersQuery.refetch() : Promise.resolve()),
  }
}

export function useMesWorkOrderDetail(workOrderId: Readonly<Ref<string>>) {
  const scope = bindAuthScope(reactive({ organizationId: '', environmentId: '' }))
  const workOrderReadScope = useMesPrincipalWorkScope(scope, MES_WORK_ORDERS_READ_PERMISSION)
  const queryCache = useQueryCache()
  const detailEnabled = computed(
    () => hasScope(scope) && workOrderReadScope.scopeReady.value && workOrderId.value.trim() !== '',
  )
  const detailScopeIdentity = computed(() => {
    const selectedScope = workOrderReadScope.selectedScope.value
    return [
      workOrderReadScope.principalIdentity.value,
      scope.organizationId.trim(),
      scope.environmentId.trim(),
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
    ].join(':')
  })
  const detailIdentityKey = computed(
    () => `${detailScopeIdentity.value}:${workOrderId.value.trim()}`,
  )
  watch(
    () => {
      const selectedScope = workOrderReadScope.selectedScope.value
      return [
        workOrderId.value.trim(),
        scope.organizationId,
        scope.environmentId,
        workOrderReadScope.principalIdentity.value,
        selectedScope?.kind ?? '',
        selectedScope?.id ?? '',
      ] as const
    },
    (_current, previous) => {
      const [
        previousWorkOrderId,
        organizationId,
        environmentId,
        principalIdentity,
        scopeKind,
        scopeId,
      ] = previous ?? []
      if (
        !previousWorkOrderId ||
        !organizationId ||
        !environmentId ||
        !principalIdentity ||
        !scopeKind ||
        !scopeId
      ) {
        return
      }
      const previousScopeIdentity = [
        principalIdentity,
        organizationId.trim(),
        environmentId.trim(),
        scopeKind,
        scopeId,
      ].join(':')
      void queryCache.cancelQueries({
        key: [
          ...getBusinessConsoleMesWorkOrderDetailQueryKey({
            path: { workOrderId: previousWorkOrderId },
            query: { organizationId, environmentId, scopeKind, scopeId },
          }),
          `principal-scope:${previousScopeIdentity}`,
        ],
        exact: true,
      })
    },
    { flush: 'sync' },
  )
  const detailQuery = useQuery(() => {
    const requestedId = workOrderId.value.trim()
    const selectedScope = workOrderReadScope.selectedScope.value
    const options = getBusinessConsoleMesWorkOrderDetailQueryOptions({
      path: { workOrderId: requestedId },
      query: {
        ...scopeQuery(scope),
        ...(selectedScope ? { scopeKind: selectedScope.kind, scopeId: selectedScope.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal-scope:${detailScopeIdentity.value}`],
      enabled: detailEnabled.value,
    }
  })
  const currentResponse = useScopeBoundListResponse(
    () => detailQuery.data.value,
    detailIdentityKey,
    detailEnabled,
  )
  const boundDetailResponse = computed(() =>
    bindWorkOrderDetailResponse(currentResponse.value, workOrderId.value.trim()),
  )
  const lastUpdatedAt = useListFreshness(boundDetailResponse, detailEnabled)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    boundDetailResponse,
    detailEnabled,
    detailQuery.isLoading,
  )
  const error = computed(() => {
    if (detailQuery.error.value) return detailQuery.error.value
    if (!hasFailedResponse.value) return undefined
    const response = boundDetailResponse.value
    const message =
      isRecord(response) && typeof response.message === 'string' && response.message.trim()
        ? response.message.trim()
        : '工单详情响应无效，请重试。'
    return new Error(message)
  })

  return {
    workOrder: computed<BusinessConsoleMesWorkOrderDetailResponse | undefined>(() => {
      const response = boundDetailResponse.value
      return isRecord(response) && response.success === true && isRecord(response.data)
        ? (response.data as BusinessConsoleMesWorkOrderDetailResponse)
        : undefined
    }),
    pending: detailQuery.isLoading,
    error,
    workOrderReadScope: workOrderReadScope.selectedScope,
    workOrderReadScopeMessage: workOrderReadScope.scopeMessage,
    workOrderReadScopePending: workOrderReadScope.scopePending,
    workOrderReadScopeReady: workOrderReadScope.scopeReady,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (detailEnabled.value ? detailQuery.refetch() : Promise.resolve()),
  }
}

const EXACT_TASK_PAGE_SIZE = 100

function exactOperationTaskQueryKey(
  organizationId: string,
  environmentId: string,
  workOrderId: string,
  operationTaskId: string,
  principalScopeIdentity: string,
) {
  return [
    'mes-report-exact-operation-task',
    organizationId,
    environmentId,
    workOrderId,
    operationTaskId,
    principalScopeIdentity,
  ] as const
}

export function useMesExactOperationTask(
  workOrderId: Readonly<Ref<string>>,
  operationTaskId: Readonly<Ref<string>>,
  detail: Readonly<Ref<BusinessConsoleMesWorkOrderDetailResponse | null | undefined>>,
) {
  const scope = bindAuthScope(reactive({ organizationId: '', environmentId: '' }))
  const reportingReadScope = useMesPrincipalWorkScope(scope, MES_REPORTING_READ_PERMISSION)
  const queryCache = useQueryCache()
  const principalScopeIdentity = computed(() => {
    const selectedScope = reportingReadScope.selectedScope.value
    return [
      reportingReadScope.principalIdentity.value,
      scope.organizationId.trim(),
      scope.environmentId.trim(),
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
    ].join(':')
  })
  watch(
    () =>
      [
        scope.organizationId,
        scope.environmentId,
        workOrderId.value.trim(),
        operationTaskId.value.trim(),
        principalScopeIdentity.value,
      ] as const,
    (_current, previous) => {
      const [
        organizationId,
        environmentId,
        requestedWorkOrderId,
        requestedTaskId,
        previousPrincipalScopeIdentity,
      ] = previous ?? []
      if (
        !organizationId ||
        !environmentId ||
        !requestedWorkOrderId ||
        !requestedTaskId ||
        !previousPrincipalScopeIdentity
      ) {
        return
      }
      void queryCache.cancelQueries({
        key: exactOperationTaskQueryKey(
          organizationId,
          environmentId,
          requestedWorkOrderId,
          requestedTaskId,
          previousPrincipalScopeIdentity,
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
      reportingReadScope.scopeReady.value &&
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
      principalScopeIdentity.value,
    ),
    enabled: enabled.value,
    query: async ({ signal }) => {
      const organizationId = scope.organizationId
      const environmentId = scope.environmentId
      const requestedWorkOrderId = workOrderId.value.trim()
      const requestedTaskId = operationTaskId.value.trim()
      const selectedScope = reportingReadScope.requireSelectedScope()
      let skip = 0
      while (true) {
        const response = await listBusinessConsoleMesReportableOperationTasks({
          query: {
            organizationId,
            environmentId,
            workOrderId: requestedWorkOrderId,
            scopeKind: selectedScope.kind,
            scopeId: selectedScope.id,
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
    reportingReadScope: reportingReadScope.selectedScope,
    reportingReadScopeMessage: reportingReadScope.scopeMessage,
    reportingReadScopePending: reportingReadScope.scopePending,
    reportingReadScopeReady: reportingReadScope.scopeReady,
    refresh: () => (enabled.value ? query.refetch() : Promise.resolve()),
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
  selectedScope: MesSelectedWorkScope,
  workOrderId?: string,
  source: 'operations' | 'reportable' = 'operations',
): Promise<BusinessConsoleMesOperationTaskRow | undefined> {
  const request = {
    query: {
      ...scopeQuery(filters),
      ...(workOrderId ? { workOrderId } : {}),
      scopeKind: selectedScope.kind,
      scopeId: selectedScope.id,
      keyword: operationTaskId,
      skip: 0,
      take: 2,
    },
    throwOnError: true,
  } as const
  const { data } =
    source === 'reportable'
      ? await listBusinessConsoleMesReportableOperationTasks(request)
      : await listBusinessConsoleMesOperationTasks(request)
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
  filters.take = TASK_LIST_PAGE_SIZE
  const operationListScope = useMesPrincipalWorkScope(filters, MES_OPERATIONS_READ_PERMISSION)
  const operationScope = useMesPrincipalWorkScope(filters, MES_OPERATIONS_MANAGE_PERMISSION)
  const queryCache = useQueryCache()
  const scopeReady = computed(() => hasScope(filters) && operationListScope.scopeReady.value)
  const operationListContextIdentity = computed(() => {
    const selectedScope = operationListScope.selectedScope.value
    return [
      operationListScope.principalIdentity.value,
      filters.organizationId.trim(),
      filters.environmentId.trim(),
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
    ].join('\u0000')
  })
  const operationTasksIdentity = computed(() => {
    return [
      operationListContextIdentity.value,
      filters.status?.trim() ?? '',
      filters.keyword?.trim() ?? '',
      filters.workOrderId?.trim() ?? '',
      filters.workCenterId?.trim() ?? '',
      filters.deviceAssetId?.trim() ?? '',
    ].join(':')
  })

  const operationTasksQuery = useQuery(() => {
    const selectedScope = operationListScope.selectedScope.value
    const options = listBusinessConsoleMesOperationTasksQueryOptions({
      query: {
        ...toListQuery(filters),
        ...(selectedScope ? { scopeKind: selectedScope.kind, scopeId: selectedScope.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal-scope:${operationTasksIdentity.value}`],
      enabled: scopeReady.value,
    }
  })
  const currentResponse = useScopeBoundListResponse(
    () => operationTasksQuery.data.value,
    operationTasksIdentity,
    scopeReady,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scopeReady)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    operationTasksQuery.isLoading,
  )
  const firstTaskPage = computed(() => {
    const envelope = currentResponse.value as
      | BusinessConsoleMesOperationTaskListEnvelope
      | undefined
    if (envelope?.success !== true) return undefined
    return {
      items: envelope.data?.items ?? [],
      total: envelope.data?.total ?? 0,
    }
  })
  const taskPager = useTaskListPagination<BusinessConsoleMesOperationTaskRow>({
    identity: operationTasksIdentity,
    firstPage: firstTaskPage,
    pageSize: TASK_LIST_PAGE_SIZE,
    itemKey: (task) => task.operationTaskId ?? '',
    fetchPage: async ({ skip, take }) => {
      const selectedScope = operationListScope.selectedScope.value
      if (!selectedScope) throw new Error(operationListScope.scopeMessage.value)
      const { data } = await listBusinessConsoleMesOperationTasks({
        query: {
          ...toListQuery({ ...filters, skip, take }),
          scopeKind: selectedScope.kind,
          scopeId: selectedScope.id,
        },
        throwOnError: true,
      })
      const envelope = data as BusinessConsoleMesOperationTaskListEnvelope | undefined
      if (envelope?.success !== true) {
        throw new Error(envelope?.message?.trim() || '工序任务下一页加载失败，请重试。')
      }
      return { items: envelope.data?.items ?? [], total: envelope.data?.total ?? 0 }
    },
    refreshFirstPage: operationTasksQuery.refetch,
  })

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
      const authoritative = await readExactOperationTask(filters, operationTaskId, selectedScope)
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
    operationTasks: taskPager.items,
    total: taskPager.total,
    loaded: taskPager.loaded,
    hasMore: taskPager.hasMore,
    loadingMore: taskPager.loadingMore,
    loadMoreError: taskPager.loadMoreError,
    loadMore: taskPager.loadMore,
    pending: operationTasksQuery.isLoading,
    error: operationTasksQuery.error,
    operationListScope: operationListScope.selectedScope,
    operationListContextIdentity,
    operationListScopeMessage: operationListScope.scopeMessage,
    operationListScopePending: operationListScope.scopePending,
    operationListScopeReady: operationListScope.scopeReady,
    operationScopeMessage: operationScope.scopeMessage,
    operationScopePending: operationScope.scopePending,
    operationScopeReady: operationScope.scopeReady,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (scopeReady.value ? taskPager.refresh() : Promise.resolve()),
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
  const scopeReady = computed(() => hasScope(filters))

  const reportsQuery = useQuery(() => ({
    ...listBusinessConsoleMesProductionReportsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: scopeReady.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => reportsQuery.data.value,
    () => scopeKey(filters),
    scopeReady,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scopeReady)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    reportsQuery.isLoading,
  )

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
      >(currentResponse.value),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: reportsQuery.isLoading,
    error: reportsQuery.error,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (scopeReady.value ? reportsQuery.refetch() : Promise.resolve()),
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
            ? await readExactOperationTask(
                filters,
                operationTaskId,
                selectedScope,
                workOrderId,
                'reportable',
              )
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
  const scopeReady = computed(() => hasScope(filters))
  const scopeKey = computed(() =>
    scopeReady.value ? `${filters.organizationId}:${filters.environmentId}` : '',
  )

  const requestsQuery = useQuery(() => ({
    ...listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: scopeReady.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => requestsQuery.data.value,
    scopeKey,
    scopeReady,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scopeReady)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    requestsQuery.isLoading,
  )

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
    scopeReady,
    requests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesMaterialIssueRequestRow,
        BusinessConsoleMesMaterialIssueRequestListEnvelope
      >(currentResponse.value),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: requestsQuery.isLoading,
    error: requestsQuery.error,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (hasScope(filters) ? requestsQuery.refetch() : Promise.resolve()),
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
  const scopeReady = computed(() => hasScope(filters))
  const scopeKey = computed(() =>
    scopeReady.value ? `${filters.organizationId}:${filters.environmentId}` : '',
  )

  const receiptsQuery = useQuery(() => ({
    ...listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: scopeReady.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => receiptsQuery.data.value,
    scopeKey,
    scopeReady,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scopeReady)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    receiptsQuery.isLoading,
  )

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
    scopeReady,
    receipts: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesReceiptRequestRow,
        BusinessConsoleMesReceiptRequestListEnvelope
      >(currentResponse.value),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: receiptsQuery.isLoading,
    error: receiptsQuery.error,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (hasScope(filters) ? receiptsQuery.refetch() : Promise.resolve()),
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
