import {
  claimBusinessConsoleMesOperationTaskMutationOptions,
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleOperation,
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesOperationTasks,
  listBusinessConsoleMesReportableOperationTasks,
  pauseBusinessConsoleMesOperationTaskMutationOptions,
  resumeBusinessConsoleMesOperationTaskMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
  type BusinessConsoleCurrentSopDocumentItem,
  type BusinessConsoleCurrentSopDocumentsEnvelope,
  type BusinessConsoleMesOperationTaskActionRequest,
  type BusinessConsoleMesOperationTaskListEnvelope,
  type BusinessConsoleMesOperationTaskRow,
  type BusinessConsoleMesWorkOrderDetailResponse,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
  statusActionGate,
  useListResponseState,
  useScopeBoundListResponse,
} from '@nerv-iip/business-core'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive, watch, type Ref } from 'vue'
import { LifecycleActionUnavailableError } from '@/composables/shared/lifecycleActionRecovery'
import { useAuthStore } from '@/stores/auth'
import { useTaskListPagination } from '../shared/useTaskListPagination'
import {
  MES_OPERATIONS_MANAGE_PERMISSION,
  MES_OPERATIONS_READ_PERMISSION,
  MES_REPORTING_READ_PERMISSION,
  bindAuthScope,
  defaultFilters,
  envelopeData,
  exactItem,
  hasScope,
  ignoreBackgroundError,
  invalidateMesQueries,
  isBusinessQuery,
  optionalQuery,
  scopeQuery,
  toListQuery,
  type MesScope,
  type MesSelectedWorkScope,
} from './mesShared'
import { useMesPrincipalWorkScope } from './useMesWorkScope'

const TASK_LIST_PAGE_SIZE = 20

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
  context: OperationActionContext
}

export type OperationAction = 'claim' | 'start' | 'pause' | 'resume' | 'complete'

type OperationLifecycleAction = Exclude<OperationAction, 'claim'>

export interface OperationActionContext {
  readonly principalId: string
  readonly organizationId: string
  readonly environmentId: string
  readonly scopeKind: string
  readonly scopeId: string
  readonly action: OperationAction
  readonly workOrderId: string
  readonly operationTaskId: string
}

export class MesOperationActionContextChangedError extends Error {
  readonly code = 'mes-operation-action-context-changed'

  constructor() {
    super('账号、组织、环境或作业范围已变化，旧操作不能重试。请返回当前列表重新发起。')
    this.name = 'MesOperationActionContextChangedError'
  }
}

export async function readExactOperationTask(
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
      ...(source === 'operations' ? { operationTaskId } : { keyword: operationTaskId }),
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
  const operationActionContextIdentity = computed(() => {
    const selectedScope = operationScope.selectedScope.value
    return [
      (auth.principal?.principalId ?? auth.sessionId ?? '').trim(),
      filters.organizationId.trim(),
      filters.environmentId.trim(),
      selectedScope?.kind.trim() ?? '',
      selectedScope?.id.trim() ?? '',
    ].join('\u0000')
  })
  const operationTasksIdentity = computed(() => {
    return [
      operationListContextIdentity.value,
      filters.status?.trim() ?? '',
      filters.keyword?.trim() ?? '',
      filters.operationTaskId?.trim() ?? '',
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
  const claimMutation = useMutation({
    ...claimBusinessConsoleMesOperationTaskMutationOptions(),
    onSuccess: invalidate,
  })

  function currentOperationActionContext(
    action: OperationAction,
    workOrderId: string,
    operationTaskId: string,
  ): OperationActionContext | null {
    const selectedScope = operationScope.selectedScope.value
    const context = {
      principalId: (auth.principal?.principalId ?? auth.sessionId ?? '').trim(),
      organizationId: filters.organizationId.trim(),
      environmentId: filters.environmentId.trim(),
      scopeKind: selectedScope?.kind.trim() ?? '',
      scopeId: selectedScope?.id.trim() ?? '',
      action,
      workOrderId: workOrderId.trim(),
      operationTaskId: operationTaskId.trim(),
    } satisfies OperationActionContext
    return Object.values(context).every((value) => value.length > 0) ? context : null
  }

  function captureOperationActionContext(
    action: OperationAction,
    workOrderId: string,
    operationTaskId: string,
  ): OperationActionContext {
    operationScope.requireSelectedScope()
    const context = currentOperationActionContext(action, workOrderId, operationTaskId)
    if (!context) throw new MesOperationActionContextChangedError()
    return Object.freeze(context)
  }

  function requireFrozenOperationActionContext(
    value: OperationActionContext | undefined,
    action: OperationAction,
    workOrderId: string,
    operationTaskId: string,
  ): OperationActionContext {
    if (!value || typeof value !== 'object') throw new MesOperationActionContextChangedError()
    const expectedPair = {
      action,
      workOrderId: workOrderId.trim(),
      operationTaskId: operationTaskId.trim(),
    }
    const fields = [
      value.principalId,
      value.organizationId,
      value.environmentId,
      value.scopeKind,
      value.scopeId,
      value.action,
      value.workOrderId,
      value.operationTaskId,
    ]
    if (
      fields.some((field) => typeof field !== 'string' || field.trim().length === 0) ||
      value.action !== expectedPair.action ||
      value.workOrderId !== expectedPair.workOrderId ||
      value.operationTaskId !== expectedPair.operationTaskId
    ) {
      throw new MesOperationActionContextChangedError()
    }
    return value
  }

  function sameOperationActionContext(
    left: OperationActionContext | null,
    right: OperationActionContext,
  ) {
    return (
      left !== null &&
      left.principalId === right.principalId &&
      left.organizationId === right.organizationId &&
      left.environmentId === right.environmentId &&
      left.scopeKind === right.scopeKind &&
      left.scopeId === right.scopeId &&
      left.action === right.action &&
      left.workOrderId === right.workOrderId &&
      left.operationTaskId === right.operationTaskId
    )
  }

  function assertOperationActionContextCurrent(context: OperationActionContext) {
    if (!isOperationActionContextCurrent(context)) {
      throw new MesOperationActionContextChangedError()
    }
  }

  function isOperationActionContextCurrent(context: OperationActionContext) {
    return sameOperationActionContext(
      currentOperationActionContext(context.action, context.workOrderId, context.operationTaskId),
      context,
    )
  }

  function actionPayload(context: OperationActionContext, options: OperationActionOptions) {
    const { reasonCode, idempotencyKey } = options
    return {
      path: { operationTaskId: context.operationTaskId },
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        scopeKind: context.scopeKind,
        scopeId: context.scopeId,
      },
      body: {
        ...(reasonCode === undefined ? {} : { reasonCode }),
        idempotencyKey,
      } satisfies BusinessConsoleMesOperationTaskActionRequest,
    }
  }

  async function performAction(
    action: OperationLifecycleAction,
    mutation: typeof startMutation,
    workOrderId: string,
    operationTaskId: string,
    options: OperationActionOptions,
  ) {
    const context = requireFrozenOperationActionContext(
      options.context,
      action,
      workOrderId,
      operationTaskId,
    )
    assertOperationActionContextCurrent(context)
    const scope = {
      principalId: context.principalId,
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      operationType: `mes.operation-task.${context.action}`,
      payloadFingerprint: `${context.workOrderId}:${context.operationTaskId}:${context.scopeKind}:${context.scopeId}:${options.reasonCode ?? ''}`,
    }
    const isReplay = Boolean(peekPendingBusinessIntent(scope))
    const pending = acquirePendingBusinessIntent(scope, () => options.idempotencyKey)
    try {
      const authoritative = await readExactOperationTask(
        { organizationId: context.organizationId, environmentId: context.environmentId },
        context.operationTaskId,
        { kind: context.scopeKind, id: context.scopeId },
        context.workOrderId,
      )
      assertOperationActionContextCurrent(context)
      const serverAllowsAction =
        authoritative?.allowedActions?.some(
          (allowedAction) => allowedAction.trim().toLowerCase() === action,
        ) ?? false
      if (!serverAllowsAction) {
        // A same-key replay may legitimately observe the transition's resulting state.
        // A new intent must never reconstruct permission from status: an absent server
        // action is authoritative and fails closed.
        const replayGate = statusActionGate({
          domain: 'mes-operation-task',
          action,
          facts: {
            status: authoritative?.status,
            idempotentReplay: isReplay,
          },
        })
        if (!replayGate.legalNoop) {
          throw new LifecycleActionUnavailableError(replayGate)
        }
      }
    } catch (error) {
      if (!isReplay) clearPendingBusinessIntent(scope)
      throw error
    }
    return completePendingBusinessIntent(scope, async () =>
      confirmBusinessConsoleOperation(
        await mutation.mutateAsync(
          actionPayload(context, {
            ...options,
            idempotencyKey: pending.idempotencyKey,
          }),
        ),
        {
          expectedOperationType: `mes.operation-task.${context.action}`,
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: context.operationTaskId,
        },
      ),
    )
  }

  async function claimTask(
    workOrderId: string,
    operationTaskId: string,
    options: OperationActionOptions,
  ) {
    const context = requireFrozenOperationActionContext(
      options.context,
      'claim',
      workOrderId,
      operationTaskId,
    )
    assertOperationActionContextCurrent(context)
    if (context.scopeKind !== 'work-center') {
      throw new Error('请选择工作中心作业范围后领取任务。')
    }
    const scope = {
      principalId: context.principalId,
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      operationType: 'mes.operation-task.claim',
      payloadFingerprint: `${context.workOrderId}:${context.operationTaskId}:${context.scopeKind}:${context.scopeId}`,
    }
    const pendingIntent = acquirePendingBusinessIntent(scope, () => options.idempotencyKey)
    const envelope = await completePendingBusinessIntent(scope, () =>
      claimMutation.mutateAsync({
        path: { operationTaskId: context.operationTaskId },
        query: {
          organizationId: context.organizationId,
          environmentId: context.environmentId,
          scopeKind: context.scopeKind,
          scopeId: context.scopeId,
        },
        body: { idempotencyKey: pendingIntent.idempotencyKey },
      }),
    )
    if (envelope?.success !== true) {
      throw new Error(envelope?.message?.trim() || '领取任务失败，请刷新后重试。')
    }
    assertOperationActionContextCurrent(context)
    const authoritative = await readExactOperationTask(
      { organizationId: context.organizationId, environmentId: context.environmentId },
      context.operationTaskId,
      { kind: context.scopeKind, id: context.scopeId },
      context.workOrderId,
    )
    if (authoritative?.assignedUserId !== context.principalId) {
      throw new Error('领取结果尚未核实，请刷新任务列表。')
    }
    return authoritative
  }

  return {
    filters,
    operationTasks: taskPager.items,
    total: taskPager.total,
    loaded: taskPager.loaded,
    hasMore: taskPager.hasMore,
    loadingMore: taskPager.loadingMore,
    refreshing: taskPager.refreshing,
    loadMoreError: taskPager.loadMoreError,
    loadMore: taskPager.loadMore,
    pending: operationTasksQuery.isLoading,
    error: operationTasksQuery.error,
    operationListScope: operationListScope.selectedScope,
    operationActionScope: operationScope.selectedScope,
    operationListContextIdentity,
    operationActionContextIdentity,
    operationListScopeMessage: operationListScope.scopeMessage,
    operationListScopePending: operationListScope.scopePending,
    operationListScopeReady: operationListScope.scopeReady,
    operationScopeMessage: operationScope.scopeMessage,
    operationScopePending: operationScope.scopePending,
    operationScopeReady: operationScope.scopeReady,
    captureOperationActionContext,
    isOperationActionContextCurrent,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (scopeReady.value ? taskPager.refresh() : Promise.resolve()),
    cancelPendingTasks: () =>
      queryCache.cancelQueries({
        predicate: isBusinessQuery('listBusinessConsoleMesOperationTasks'),
      }),
    startTask: (workOrderId: string, operationTaskId: string, options: OperationActionOptions) =>
      performAction('start', startMutation, workOrderId, operationTaskId, options),
    pauseTask: (workOrderId: string, operationTaskId: string, options: OperationActionOptions) =>
      performAction('pause', pauseMutation, workOrderId, operationTaskId, options),
    resumeTask: (workOrderId: string, operationTaskId: string, options: OperationActionOptions) =>
      performAction('resume', resumeMutation, workOrderId, operationTaskId, options),
    completeTask: (workOrderId: string, operationTaskId: string, options: OperationActionOptions) =>
      performAction('complete', completeMutation, workOrderId, operationTaskId, options),
    claimTask,
    actionPending: computed(
      () =>
        startMutation.isLoading.value ||
        pauseMutation.isLoading.value ||
        resumeMutation.isLoading.value ||
        completeMutation.isLoading.value ||
        claimMutation.isLoading.value,
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
  }
}
