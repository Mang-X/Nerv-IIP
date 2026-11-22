import {
  assignBusinessConsoleMesDispatchTaskMutationOptions,
  completeBusinessConsoleMesOperationTaskMutationOptions,
  confirmBusinessConsoleOperation,
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  getBusinessConsoleMesWipSummaryQueryOptions,
  listBusinessConsoleMesDispatchTasksQueryOptions,
  listBusinessConsoleMesCapacityImpactsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  pauseBusinessConsoleMesOperationTaskMutationOptions,
  resumeBusinessConsoleMesOperationTaskMutationOptions,
  startBusinessConsoleMesOperationTaskMutationOptions,
  type BusinessConsoleMesCapacityImpactListEnvelope,
  type BusinessConsoleMesCapacityImpactRow,
  type BusinessConsoleMesDispatchTaskListEnvelope,
  type BusinessConsoleMesDispatchTaskRow,
  type BusinessConsoleCurrentSopDocumentItem,
  type BusinessConsoleCurrentSopDocumentsEnvelope,
  type BusinessConsoleMesOperationTaskActionRequest,
  type BusinessConsoleMesOperationTaskListEnvelope,
  type BusinessConsoleMesOperationTaskRow,
  type BusinessConsoleMesWipSummaryEnvelope,
  type BusinessConsoleMesWipSummaryRow,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
  type BusinessContextFields,
} from '../shared/businessContextBinding'
import { businessReadState } from '../shared/businessReadState'
import { executeLifecycleAction } from '../shared/lifecycleAction'
import {
  MES_OPERATIONS_MANAGE_PERMISSION,
  MES_OPERATIONS_READ_PERMISSION,
  defaultFilters,
  envelopeItems,
  envelopeTotal,
  ignoreBackgroundError,
  invalidateMesQueries,
  makeIdempotencyKey,
  optionalQuery,
  requirePendingPayloadSnapshot,
  toListQuery,
  type MesContextFilters,
} from './mesShared'
import { type MesSelectedWorkScope, useMesPrincipalWorkScope } from './useMesWorkScope'
import { readMesOperationLifecycleRequest } from './mesOperationLifecycle'

export function useMesOperationTasks(operationTaskId: () => string | undefined = () => undefined) {
  const auth = useAuthStore()
  const filters = defaultFilters()
  const operationListScope = useMesPrincipalWorkScope(filters, MES_OPERATIONS_READ_PERMISSION)
  const operationScope = useMesPrincipalWorkScope(filters, MES_OPERATIONS_MANAGE_PERMISSION)
  const queryCache = useQueryCache()

  const operationTasksScopeReady = computed(
    () =>
      hasBusinessContext(filters) &&
      operationListScope.scopeReady.value &&
      !operationListScope.scopeError.value,
  )
  const operationTasksIdentity = computed(() => {
    const selectedScope = operationListScope.selectedScope.value
    return [
      operationListScope.principalIdentity.value,
      filters.organizationId.trim(),
      filters.environmentId.trim(),
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
      ...(operationTaskId() ? [operationTaskId()] : []),
    ].join(':')
  })
  const operationTasksQuery = useQuery(() => {
    const selectedScope = operationListScope.selectedScope.value
    const identity = operationTasksIdentity.value
    const { key, query: executeQuery } = listBusinessConsoleMesOperationTasksQueryOptions({
      query: {
        ...(operationTaskId()
          ? {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              operationTaskId: operationTaskId(),
              skip: 0,
              take: 1,
            }
          : toListQuery(filters)),
        ...(selectedScope ? { scopeKind: selectedScope.kind, scopeId: selectedScope.id } : {}),
      },
    })
    return {
      key: [...key, `principal-scope:${identity}`],
      query: async (context: Parameters<typeof executeQuery>[0]) => ({
        identity,
        response: await executeQuery(context),
      }),
      enabled: operationTasksScopeReady.value,
    }
  })
  // 身份随查询结果缓存：切回已缓存范围时可立即恢复，晚到的其它范围结果不被重新归属。
  const operationTasksResponse = computed(() =>
    operationTasksScopeReady.value &&
    operationTasksQuery.data.value?.identity === operationTasksIdentity.value
      ? operationTasksQuery.data.value.response
      : undefined,
  )
  const completeMutation = useMutation(completeBusinessConsoleMesOperationTaskMutationOptions())
  const pauseMutation = useMutation(pauseBusinessConsoleMesOperationTaskMutationOptions())
  const resumeMutation = useMutation(resumeBusinessConsoleMesOperationTaskMutationOptions())
  const startMutation = useMutation(startBusinessConsoleMesOperationTaskMutationOptions())
  function operationActionBody(
    operationTaskId: string,
    context: MesContextFilters,
    selectedScope: MesSelectedWorkScope,
    body: BusinessConsoleMesOperationTaskActionRequest,
  ) {
    return {
      path: { operationTaskId },
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        scopeKind: selectedScope.kind,
        scopeId: selectedScope.id,
      },
      body,
    }
  }

  async function performOperationAction(
    action: 'start' | 'pause' | 'resume' | 'complete',
    mutation: typeof startMutation,
    operationTaskId: string,
    context: MesContextFilters,
    body: BusinessConsoleMesOperationTaskActionRequest,
  ) {
    const selectedScope = operationScope.requireSelectedScope()
    const { idempotencyKey: suppliedKey, ...payload } = body
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      operationType: `mes.operation-task.${action}`,
      payloadFingerprint: `${operationTaskId}:${selectedScope.kind}:${selectedScope.id}:${JSON.stringify(payload)}`,
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () => suppliedKey?.trim() || makeIdempotencyKey(`operation-${action}`),
      payload,
    )
    const stablePayload = requirePendingPayloadSnapshot<typeof payload>(
      pending.payloadSnapshot,
      `工序${action}动作`,
    )
    const result = await completePendingBusinessIntent(scope, async () => {
      const envelope = await executeLifecycleAction({
        readLatest: () =>
          readMesOperationLifecycleRequest(
            operationTaskId,
            context,
            selectedScope,
            action,
            restored?.idempotencyKey === pending.idempotencyKey,
          ),
        command: async () => ({
          data: await mutation.mutateAsync(
            operationActionBody(operationTaskId, context, selectedScope, {
              ...stablePayload,
              idempotencyKey: pending.idempotencyKey,
            }),
          ),
          response: { status: 200 },
        }),
      })
      if (!envelope) throw new Error('工序动作未返回业务信封')
      await confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: `mes.operation-task.${action}`,
        expectedIdempotencyKey: pending.idempotencyKey,
        expectedResourceId: operationTaskId,
      })
      return envelope
    })
    await invalidateMesQueries(queryCache, [
      'listBusinessConsoleMesOperationTasks',
      'getBusinessConsoleMesWipSummary',
    ])
    return result
  }

  return {
    filters,
    completeOperationTask: async (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => performOperationAction('complete', completeMutation, operationTaskId, context, body),
    operationTasks: computed<BusinessConsoleMesOperationTaskRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesOperationTaskRow,
        BusinessConsoleMesOperationTaskListEnvelope
      >(operationTasksResponse.value),
    ),
    operationTasksError: operationTasksQuery.error,
    operationTasksPending: operationTasksQuery.isLoading,
    operationTasksState: businessReadState(
      { ...operationTasksQuery, data: operationTasksResponse },
      () => operationTasksScopeReady.value,
    ),
    operationTasksTotal: computed(() => envelopeTotal(operationTasksResponse.value)),
    operationListScope: operationListScope.selectedScope,
    operationListScopeMessage: operationListScope.scopeMessage,
    operationListScopeError: operationListScope.scopeError,
    operationListScopePending: operationListScope.scopePending,
    operationListScopeReady: operationListScope.scopeReady,
    operationScopeMessage: operationScope.scopeMessage,
    operationScopePending: operationScope.scopePending,
    operationScopeReady: operationScope.scopeReady,
    pauseOperationTask: async (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => performOperationAction('pause', pauseMutation, operationTaskId, context, body),
    refreshOperationTasks: () =>
      operationTasksScopeReady.value
        ? operationTasksQuery.refetch()
        : operationListScope.refreshScope(),
    resumeOperationTask: async (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => performOperationAction('resume', resumeMutation, operationTaskId, context, body),
    startOperationTask: async (
      operationTaskId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesOperationTaskActionRequest,
    ) => performOperationAction('start', startMutation, operationTaskId, context, body),
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

export function useMesWipSummary(shouldLoad: () => boolean = () => true) {
  const filters = defaultFilters()

  const wipQuery = useQuery(() => {
    const options = withBusinessContextEnabled(
      getBusinessConsoleMesWipSummaryQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    )
    return { ...options, enabled: options.enabled && shouldLoad() }
  })

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
