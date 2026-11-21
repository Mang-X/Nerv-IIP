import {
  completeBusinessConsoleWmsPickingTask,
  completeBusinessConsoleWmsPutawayTask,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPickingTasks,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasks,
  listBusinessConsoleWmsReceivingQualityGatesQueryOptions,
  recordBusinessConsoleWmsPickingTaskProgress,
  recordBusinessConsoleWmsPutawayTaskProgress,
  reportBusinessConsoleWmsPickingTaskException,
  reportBusinessConsoleWmsPutawayTaskException,
  startBusinessConsoleWmsPickingTask,
  startBusinessConsoleWmsPutawayTask,
  type BusinessConsoleCompleteWmsWarehouseTaskRequest,
  type BusinessConsoleWmsReceivingQualityGateItem,
  type BusinessConsoleWmsReceivingQualityGateListEnvelope,
  type BusinessConsoleWmsWarehouseTaskItem,
  type BusinessConsoleWmsWarehouseTaskActionEnvelope,
  type BusinessConsoleWmsWarehouseTaskActionResult,
  type BusinessConsoleWmsWarehouseTaskListEnvelope,
  type BusinessConsoleRecordWmsWarehouseTaskProgressRequest,
  type BusinessConsoleReportWmsWarehouseTaskExceptionRequest,
  type BusinessConsoleStartWmsWarehouseTaskRequest,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
  type PendingBusinessIntentScope,
  useListResponseState,
  useScopeBoundListResponse,
} from '@nerv-iip/business-core'
import { useQuery } from '@pinia/colada'
import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useWmsSessionKey } from '@/composables/useWmsWorkScope'
import {
  TASK_PAGE_SIZE,
  defaultFilters,
  exactItem,
  isCurrentPagingRequest,
  lifecycleUnavailable,
  listItems,
  listTotal,
  optionalQuery,
  requireFrozenScope,
  type WmsTaskFilters,
  useAuthorizedWmsScope,
  usePagedAccumulator,
  useWmsTenantScope,
} from './wmsShared'

export interface WmsWarehouseTask extends Omit<
  BusinessConsoleWmsWarehouseTaskItem,
  'warehouseTaskId' | 'allowedActions' | 'blockReasons'
> {
  warehouseTaskId: string
  allowedActions: string[]
  blockReasons: string[]
}

export interface WmsWarehouseTaskExecutionIntent {
  action: 'start' | 'progress' | 'exception' | 'complete'
  task: BusinessConsoleWmsWarehouseTaskItem
  executedQuantity?: number
  exceptionCode?: string
  reason?: string
}

type WarehouseTaskAction = WmsWarehouseTaskExecutionIntent['action']

interface FrozenWarehouseTaskAction {
  action: WarehouseTaskAction
  warehouseTaskId: string
  taskNo?: string
  expectedVersion: number
  payloadSnapshot: {
    expectedVersion: number
    executedQuantity?: number
    exceptionCode?: string
    reason?: string
    differenceReason?: string
    scopeKind: string
    scopeId: string
  }
  intentScope: PendingBusinessIntentScope
  pendingLookupKey: string
}

function normalizeWarehouseTask(
  task: BusinessConsoleWmsWarehouseTaskItem,
): WmsWarehouseTask | undefined {
  const warehouseTaskId = task.warehouseTaskId?.trim()
  if (!warehouseTaskId) return undefined
  return {
    ...task,
    warehouseTaskId,
    allowedActions: task.allowedActions ?? [],
    blockReasons: task.blockReasons ?? [],
  }
}

function taskLifecycleError(task: BusinessConsoleWmsWarehouseTaskItem | undefined) {
  return lifecycleUnavailable(task)
}

function canonicalWarehouseTaskStatus(status: string | null | undefined) {
  return (
    status
      ?.trim()
      .toLowerCase()
      .replaceAll(/[^a-z]/g, '') ?? ''
  )
}

function isWarehouseTaskActionConfirmed(
  frozen: FrozenWarehouseTaskAction,
  authoritative: BusinessConsoleWmsWarehouseTaskItem | undefined,
) {
  if (
    !authoritative ||
    !Number.isInteger(authoritative.version) ||
    authoritative.version! <= frozen.expectedVersion
  ) {
    return false
  }

  const status = canonicalWarehouseTaskStatus(authoritative.status)
  if (frozen.action === 'start') return status === 'inprogress'
  if (frozen.action === 'exception') return status === 'exception'

  const quantityMatches = authoritative.executedQuantity === frozen.payloadSnapshot.executedQuantity
  if (frozen.action === 'progress') return status === 'inprogress' && quantityMatches
  return (status === 'completed' || status === 'completedwithdifference') && quantityMatches
}

function unwrapTaskActionResult(
  envelope: BusinessConsoleWmsWarehouseTaskActionEnvelope | undefined,
  warehouseTaskId: string,
) {
  if (
    envelope?.success !== true ||
    !envelope.data ||
    envelope.data.warehouseTaskId !== warehouseTaskId
  ) {
    throw new Error('作业提交结果无法确认，请刷新后重试')
  }
  return envelope.data
}

async function invokeWarehouseTaskAction(
  taskType: 'picking' | 'putaway',
  action: WmsWarehouseTaskExecutionIntent['action'],
  warehouseTaskId: string,
  query: {
    organizationId: string
    environmentId: string
    scopeKind: string
    scopeId: string
  },
  body:
    | BusinessConsoleStartWmsWarehouseTaskRequest
    | BusinessConsoleRecordWmsWarehouseTaskProgressRequest
    | BusinessConsoleReportWmsWarehouseTaskExceptionRequest
    | BusinessConsoleCompleteWmsWarehouseTaskRequest,
): Promise<BusinessConsoleWmsWarehouseTaskActionResult> {
  const common = {
    path: { warehouseTaskId },
    query,
    throwOnError: true as const,
  }
  const response =
    taskType === 'picking'
      ? action === 'start'
        ? await startBusinessConsoleWmsPickingTask({
            ...common,
            body: body as BusinessConsoleStartWmsWarehouseTaskRequest,
          })
        : action === 'progress'
          ? await recordBusinessConsoleWmsPickingTaskProgress({
              ...common,
              body: body as BusinessConsoleRecordWmsWarehouseTaskProgressRequest,
            })
          : action === 'exception'
            ? await reportBusinessConsoleWmsPickingTaskException({
                ...common,
                body: body as BusinessConsoleReportWmsWarehouseTaskExceptionRequest,
              })
            : await completeBusinessConsoleWmsPickingTask({
                ...common,
                body: body as BusinessConsoleCompleteWmsWarehouseTaskRequest,
              })
      : action === 'start'
        ? await startBusinessConsoleWmsPutawayTask({
            ...common,
            body: body as BusinessConsoleStartWmsWarehouseTaskRequest,
          })
        : action === 'progress'
          ? await recordBusinessConsoleWmsPutawayTaskProgress({
              ...common,
              body: body as BusinessConsoleRecordWmsWarehouseTaskProgressRequest,
            })
          : action === 'exception'
            ? await reportBusinessConsoleWmsPutawayTaskException({
                ...common,
                body: body as BusinessConsoleReportWmsWarehouseTaskExceptionRequest,
              })
            : await completeBusinessConsoleWmsPutawayTask({
                ...common,
                body: body as BusinessConsoleCompleteWmsWarehouseTaskRequest,
              })

  return unwrapTaskActionResult(
    response.data as BusinessConsoleWmsWarehouseTaskActionEnvelope | undefined,
    warehouseTaskId,
  )
}

function useWmsWarehouseTasks(
  taskType: 'picking' | 'putaway',
  queryOptionsFactory:
    | typeof listBusinessConsoleWmsPickingTasksQueryOptions
    | typeof listBusinessConsoleWmsPutawayTasksQueryOptions,
  initialFilters: Partial<WmsTaskFilters> = {},
) {
  const scope = useAuthorizedWmsScope(taskType === 'picking' ? 'shipments' : 'receipts')
  const filters = defaultFilters<WmsTaskFilters>({
    ...initialFilters,
    take: initialFilters.take ?? TASK_PAGE_SIZE,
  })
  const refreshing = shallowRef(false)
  const loadingMore = shallowRef(false)
  let activeLoadMoreToken: symbol | undefined
  const actionPending = shallowRef(false)
  const actionError = shallowRef<unknown>()
  const queryError = shallowRef<unknown>()
  const unconfirmedTaskAction = shallowRef<FrozenWarehouseTaskAction>()
  const actionConfirmedSequence = shallowRef(0)
  const actionUnconfirmed = computed(() => unconfirmedTaskAction.value !== undefined)
  const pendingTaskIntentScopes = new Map<string, PendingBusinessIntentScope>()

  const tasksQuery = useQuery(() =>
    scope.bindQuery(
      queryOptionsFactory({
        query: {
          ...scope.scopeQueryWithPaging(filters),
          ...optionalQuery('locationCode', filters.locationCode),
          ...optionalQuery('lotNo', filters.lotNo),
        },
      }),
    ),
  )
  const currentResponse = useScopeBoundListResponse(
    () => tasksQuery.data.value,
    scope.responseScopeKey,
    scope.hasScope,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scope.hasScope,
    tasksQuery.isLoading,
  )
  const page = usePagedAccumulator(
    () => currentResponse.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
    scope.hasScope,
    normalizeWarehouseTask,
    (task) => task.warehouseTaskId,
  )
  const loadMoreError = shallowRef<unknown>()
  const pagingGeneration = shallowRef(0)

  function resetPaging() {
    pagingGeneration.value += 1
    filters.skip = 0
    filters.take = TASK_PAGE_SIZE
    activeLoadMoreToken = undefined
    loadingMore.value = false
    loadMoreError.value = undefined
    queryError.value = undefined
    page.reset()
  }

  watch(
    [
      scope.selectedScopeKey,
      () => filters.status,
      () => filters.keyword,
      () => filters.locationCode,
      () => filters.lotNo,
    ],
    resetPaging,
    { flush: 'sync' },
  )

  async function refresh() {
    if (!scope.hasScope.value) return
    pagingGeneration.value += 1
    filters.skip = 0
    filters.take = TASK_PAGE_SIZE
    activeLoadMoreToken = undefined
    loadingMore.value = false
    loadMoreError.value = undefined
    queryError.value = undefined
    refreshing.value = true
    try {
      let confirmedAction: Awaited<ReturnType<typeof verifyUnconfirmedTaskAction>>
      try {
        confirmedAction = await verifyUnconfirmedTaskAction()
      } catch (error) {
        actionError.value = error
        throw error
      }

      try {
        await tasksQuery.refetch()
        queryError.value = undefined
        if (!unconfirmedTaskAction.value) actionError.value = undefined
        return confirmedAction ? { confirmedAction } : {}
      } catch (error) {
        queryError.value = error
        throw error
      }
    } finally {
      refreshing.value = false
    }
  }

  async function loadMore() {
    if (
      !scope.hasScope.value ||
      tasksQuery.isLoading.value ||
      refreshing.value ||
      loadingMore.value ||
      page.exhausted.value
    ) {
      return
    }
    const loadMoreToken = Symbol(`wms-${taskType}-load-more`)
    activeLoadMoreToken = loadMoreToken
    loadingMore.value = true
    const requestedSkip = page.nextSkip.value
    const requestScopeKey = scope.responseScopeKey.value
    const requestGeneration = pagingGeneration.value
    try {
      const listOperation =
        taskType === 'picking'
          ? listBusinessConsoleWmsPickingTasks
          : listBusinessConsoleWmsPutawayTasks
      const { data } = await listOperation({
        query: {
          ...scope.scopeQuery(),
          skip: requestedSkip,
          take: TASK_PAGE_SIZE,
          ...optionalQuery('status', filters.status),
          ...optionalQuery('keyword', filters.keyword),
          ...optionalQuery('locationCode', filters.locationCode),
          ...optionalQuery('lotNo', filters.lotNo),
        },
        throwOnError: true,
      })
      if (
        isCurrentPagingRequest(
          requestScopeKey,
          requestGeneration,
          scope.responseScopeKey.value,
          pagingGeneration.value,
        )
      ) {
        loadMoreError.value = undefined
        page.acceptPage(
          data as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
          requestedSkip,
          TASK_PAGE_SIZE,
        )
      }
    } catch (error) {
      if (
        isCurrentPagingRequest(
          requestScopeKey,
          requestGeneration,
          scope.responseScopeKey.value,
          pagingGeneration.value,
        )
      ) {
        loadMoreError.value = error
      }
      throw error
    } finally {
      if (activeLoadMoreToken === loadMoreToken) {
        activeLoadMoreToken = undefined
        loadingMore.value = false
      }
    }
  }

  function taskListOperation() {
    return taskType === 'picking'
      ? listBusinessConsoleWmsPickingTasks
      : listBusinessConsoleWmsPutawayTasks
  }

  async function readAuthoritativeTask(frozen: FrozenWarehouseTaskAction) {
    const { data } = await taskListOperation()({
      query: {
        organizationId: frozen.intentScope.organizationId,
        environmentId: frozen.intentScope.environmentId,
        scopeKind: frozen.payloadSnapshot.scopeKind,
        scopeId: frozen.payloadSnapshot.scopeId,
        keyword: frozen.taskNo?.trim() || frozen.warehouseTaskId,
        skip: 0,
        take: TASK_PAGE_SIZE,
      },
      throwOnError: true,
    })
    return exactItem(
      data as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      (item: BusinessConsoleWmsWarehouseTaskItem) =>
        item.warehouseTaskId === frozen.warehouseTaskId,
    )
  }

  function confirmFrozenTaskAction(frozen: FrozenWarehouseTaskAction) {
    clearPendingBusinessIntent(frozen.intentScope)
    pendingTaskIntentScopes.delete(frozen.pendingLookupKey)
    if (unconfirmedTaskAction.value?.pendingLookupKey === frozen.pendingLookupKey) {
      unconfirmedTaskAction.value = undefined
    }
    actionError.value = undefined
    actionConfirmedSequence.value += 1
  }

  async function verifyUnconfirmedTaskAction() {
    const frozen = unconfirmedTaskAction.value
    if (!frozen) return undefined
    const authoritative = await readAuthoritativeTask(frozen)
    if (!isWarehouseTaskActionConfirmed(frozen, authoritative)) return undefined

    confirmFrozenTaskAction(frozen)
    return frozen.action
  }

  async function executeTask(intent: WmsWarehouseTaskExecutionIntent) {
    if (!scope.hasScope.value) throw taskLifecycleError(undefined)
    const warehouseTaskId = intent.task.warehouseTaskId?.trim()
    const expectedVersion = intent.task.version
    if (!warehouseTaskId || !Number.isInteger(expectedVersion)) {
      throw taskLifecycleError(intent.task)
    }

    const actionPayload =
      intent.action === 'start'
        ? { expectedVersion: expectedVersion! }
        : intent.action === 'progress'
          ? { expectedVersion: expectedVersion!, executedQuantity: intent.executedQuantity }
          : intent.action === 'exception'
            ? {
                expectedVersion: expectedVersion!,
                exceptionCode: intent.exceptionCode?.trim(),
                reason: intent.reason?.trim(),
              }
            : {
                expectedVersion: expectedVersion!,
                executedQuantity: intent.executedQuantity,
                differenceReason: intent.reason?.trim() || undefined,
              }

    if (
      ((intent.action === 'progress' || intent.action === 'complete') &&
        !Number.isFinite(intent.executedQuantity)) ||
      (intent.action === 'exception' && (!actionPayload.exceptionCode || !actionPayload.reason))
    ) {
      throw new Error('作业动作参数不完整，请重新输入')
    }

    const liveCommandScope = scope.scopeQuery()
    const payloadSnapshot = {
      ...actionPayload,
      scopeKind: liveCommandScope.scopeKind,
      scopeId: liveCommandScope.scopeId,
    }
    const pendingLookupKey = [
      scope.principalId.value,
      liveCommandScope.organizationId,
      liveCommandScope.environmentId,
      warehouseTaskId,
      intent.action,
      JSON.stringify(actionPayload),
    ].join(':')
    let intentScope = pendingTaskIntentScopes.get(pendingLookupKey)
    if (intentScope && !peekPendingBusinessIntent(intentScope)) {
      pendingTaskIntentScopes.delete(pendingLookupKey)
      intentScope = undefined
    }
    intentScope ??= {
      principalId: scope.principalId.value,
      organizationId: liveCommandScope.organizationId,
      environmentId: liveCommandScope.environmentId,
      operationType: `wms.${taskType}-task.${intent.action}`,
      payloadFingerprint: `${warehouseTaskId}:${JSON.stringify(payloadSnapshot)}`,
    }
    const restoredPending = peekPendingBusinessIntent(intentScope)
    const pending = acquirePendingBusinessIntent(intentScope, makeIdempotencyKey, payloadSnapshot)
    pendingTaskIntentScopes.set(pendingLookupKey, intentScope)
    const isReplay =
      restoredPending !== undefined && restoredPending.idempotencyKey === pending.idempotencyKey
    if (!requireFrozenScope(pending.payloadSnapshot)) {
      throw taskLifecycleError(intent.task)
    }
    const frozenSnapshot = pending.payloadSnapshot as typeof payloadSnapshot
    const frozenCommandScope = {
      organizationId: intentScope.organizationId,
      environmentId: intentScope.environmentId,
      scopeKind: frozenSnapshot.scopeKind,
      scopeId: frozenSnapshot.scopeId,
    }
    const frozenTaskAction: FrozenWarehouseTaskAction = {
      action: intent.action,
      warehouseTaskId,
      taskNo: intent.task.taskNo,
      expectedVersion: frozenSnapshot.expectedVersion,
      payloadSnapshot: frozenSnapshot,
      intentScope,
      pendingLookupKey,
    }

    actionError.value = undefined
    actionPending.value = true
    let mutationStarted = false
    try {
      const authoritative = await readAuthoritativeTask(frozenTaskAction)
      if (isReplay && isWarehouseTaskActionConfirmed(frozenTaskAction, authoritative)) {
        confirmFrozenTaskAction(frozenTaskAction)
        return authoritative
      }
      if (
        !isReplay &&
        (!authoritative ||
          authoritative.version !== frozenSnapshot.expectedVersion ||
          !authoritative.allowedActions?.includes(intent.action))
      ) {
        clearPendingBusinessIntent(intentScope)
        throw taskLifecycleError(authoritative)
      }

      const { scopeKind: _scopeKind, scopeId: _scopeId, ...frozenPayload } = frozenSnapshot
      const body = {
        ...frozenPayload,
        idempotencyKey: pending.idempotencyKey,
      } as
        | BusinessConsoleStartWmsWarehouseTaskRequest
        | BusinessConsoleRecordWmsWarehouseTaskProgressRequest
        | BusinessConsoleReportWmsWarehouseTaskExceptionRequest
        | BusinessConsoleCompleteWmsWarehouseTaskRequest
      mutationStarted = true
      const result = await completePendingBusinessIntent(intentScope, () =>
        invokeWarehouseTaskAction(
          taskType,
          intent.action,
          warehouseTaskId,
          frozenCommandScope,
          body,
        ),
      )
      confirmFrozenTaskAction(frozenTaskAction)
      try {
        await refresh()
      } catch {
        // The command receipt already confirmed success. Keep the refresh error visible, but do
        // not turn a confirmed write back into a failed action or lose the page's status focus.
      }
      return result
    } catch (error) {
      if (!isReplay && !mutationStarted && peekPendingBusinessIntent(intentScope)) {
        clearPendingBusinessIntent(intentScope)
      }
      unconfirmedTaskAction.value = peekPendingBusinessIntent(intentScope)
        ? frozenTaskAction
        : undefined
      actionError.value = error
      throw error
    } finally {
      if (!peekPendingBusinessIntent(intentScope)) {
        pendingTaskIntentScopes.delete(pendingLookupKey)
      }
      actionPending.value = false
    }
  }

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    principalId: scope.principalId,
    scopeKind: scope.scopeKind,
    scopeId: scope.scopeId,
    scopeKey: scope.selectedScopeKey,
    scopeOptions: scope.scopeOptions,
    selectedScopeLabel: scope.selectedScopeLabel,
    scopeReady: scope.hasScope,
    filters,
    tasks: computed(() => page.items.value),
    total: computed(() => page.total.value),
    pending: computed(() => tasksQuery.isLoading.value || scope.pending.value),
    error: computed(() => queryError.value ?? tasksQuery.error.value ?? scope.error.value),
    loadMoreError,
    actionError,
    refreshing,
    loadingMore,
    actionPending,
    actionUnconfirmed,
    actionConfirmedSequence,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh,
    loadMore,
    executeTask,
  }
}

export function useWmsPicking(initialFilters: Partial<WmsTaskFilters> = {}) {
  return useWmsWarehouseTasks(
    'picking',
    listBusinessConsoleWmsPickingTasksQueryOptions,
    initialFilters,
  )
}

export type ReceivingQualityGateLine = BusinessConsoleWmsReceivingQualityGateItem

// 按「选中收货单」查完整收货行（#705 投影 + includeNotRequired=true 含免检行）。
// 单据级质检状态标/上架门禁改由 ListInboundOrders 单据级派生字段驱动（避免按分页门禁
// 行跨页聚合出错）；本 composable 只在打开某单明细时查该单的全部行，用于展示/采集
// 批号效期与逐行门禁。用服务端精确 inboundOrderNo 过滤（非 keyword——keyword 亦命中
// sku/检验号会跨单串扰）；再暴露 total → complete 判据，行数被 take 截断（未证明完整）
// 时调用方 fail closed 禁止提交，避免以不完整行完成收货静默漏采集。
const RECEIVING_LINES_TAKE = 500

export interface WmsReceivingLinesScope {
  scopeKind: MaybeRefOrGetter<string | undefined>
  scopeId: MaybeRefOrGetter<string | undefined>
}

export function useWmsReceivingLines(
  inboundOrderNo: MaybeRefOrGetter<string>,
  selectedScope: WmsReceivingLinesScope,
) {
  const scope = useWmsTenantScope()
  const sessionKey = useWmsSessionKey()
  const orderNo = computed(() => toValue(inboundOrderNo).trim())
  const scopeKind = computed(() => toValue(selectedScope.scopeKind)?.trim() ?? '')
  const scopeId = computed(() => toValue(selectedScope.scopeId)?.trim() ?? '')
  const enabled = computed(
    () =>
      scope.hasScope.value &&
      scopeKind.value.length > 0 &&
      scopeId.value.length > 0 &&
      orderNo.value.length > 0,
  )
  const responseScopeKey = computed(() =>
    JSON.stringify([sessionKey.value, scopeKind.value, scopeId.value, orderNo.value]),
  )

  const linesQuery = useQuery(() => {
    const options = listBusinessConsoleWmsReceivingQualityGatesQueryOptions({
      query: {
        ...scope.scopeQuery(),
        scopeKind: scopeKind.value,
        scopeId: scopeId.value,
        skip: 0,
        take: RECEIVING_LINES_TAKE,
        inboundOrderNo: orderNo.value,
        includeNotRequired: true,
      },
    })
    return { ...options, key: [...options.key, sessionKey.value], enabled: enabled.value }
  })

  const currentResponse = useScopeBoundListResponse(
    () => linesQuery.data.value,
    responseScopeKey,
    enabled,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    enabled,
    linesQuery.isLoading,
  )
  const envelope = computed(
    () => currentResponse.value as BusinessConsoleWmsReceivingQualityGateListEnvelope | undefined,
  )
  const lines = computed(() => listItems<ReceivingQualityGateLine>(envelope.value))
  const total = computed(() => listTotal(envelope.value))
  // 完整性 fail-closed：入库单按域约束必有 ≥1 行，故要求 total>0 且已取回行数覆盖 total
  // （未被 take 截断）。total=0/空集（精确单号未命中、投影暂不一致、数据异常）判为不完整，
  // 调用方禁止提交并展示可重试异常态，避免以空采集行静默完成收货。
  const complete = computed(() => total.value > 0 && lines.value.length >= total.value)

  return {
    lines,
    total,
    complete,
    hasSuccessfulResponse,
    hasFailedResponse,
    pending: linesQuery.isLoading,
    error: linesQuery.error,
    refresh: () => (enabled.value ? linesQuery.refetch() : Promise.resolve()),
  }
}

export function useWmsPutaway(initialFilters: Partial<WmsTaskFilters> = {}) {
  return useWmsWarehouseTasks(
    'putaway',
    listBusinessConsoleWmsPutawayTasksQueryOptions,
    initialFilters,
  )
}
