import {
  completeBusinessConsoleWmsWcsTaskMutationOptions,
  createBusinessConsoleWmsPickingTaskMutationOptions,
  createBusinessConsoleWmsPutawayTaskMutationOptions,
  dispatchBusinessConsoleWmsWcsTaskMutationOptions,
  failBusinessConsoleWmsWcsTaskMutationOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPickingTasks,
  startBusinessConsoleWmsPickingTask,
  completeBusinessConsoleWmsPickingTask,
  reportBusinessConsoleWmsPickingTaskException,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
  type BusinessConsoleCreateWmsPickingTaskRequest,
  type BusinessConsoleCreateWmsPutawayTaskRequest,
  type BusinessConsoleWmsWarehouseTaskItem,
  type BusinessConsoleWmsWarehouseTaskListEnvelope,
  type BusinessConsoleWmsWcsTaskItem,
  type BusinessConsoleWmsWcsTaskListEnvelope,
} from '@nerv-iip/api-client'
import { useScopeBoundListResponse } from '@nerv-iip/business-core'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, shallowRef } from 'vue'
import {
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import { LifecycleStateChangedError } from '../shared/lifecycleAction'
import {
  baseQuery,
  createWmsIdempotencyKey,
  defaultFilters,
  exactSuccessfulItem,
  listItems,
  listScopeKey,
  listScopeReady,
  listTotal,
  optionalQuery,
  refetchWithWmsListScope,
  requireVersion,
  trustedWorkScope,
  type WmsListFilters,
  type WmsWarehouseTaskListFilters,
  withWmsListScopeEnabled,
} from './wmsShared'

export interface WmsWcsTaskListFilters extends WmsListFilters {
  externalTaskId?: string
  warehouseTaskId?: string
  failed?: boolean
}

export function useWmsWcsTasks(initialFilters: Partial<WmsWcsTaskListFilters> = {}) {
  const filters = defaultFilters<WmsWcsTaskListFilters>(initialFilters)
  const wcsTasksQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleWmsWcsTasksQueryOptions({
        query: {
          ...baseQuery(filters),
          ...optionalQuery('externalTaskId', filters.externalTaskId),
          ...optionalQuery('warehouseTaskId', filters.warehouseTaskId),
          ...optionalQuery('failed', filters.failed),
        },
      }),
      filters,
    ),
  )

  function withQuery() {
    return { organizationId: filters.organizationId, environmentId: filters.environmentId }
  }
  const dispatchMutation = useMutation({
    ...dispatchBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, wcsTasksQuery)
    },
  })
  const failMutation = useMutation({
    ...failBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, wcsTasksQuery)
    },
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, wcsTasksQuery)
    },
  })
  return {
    filters,
    wcsTasks: computed<BusinessConsoleWmsWcsTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWcsTaskItem>(
        wcsTasksQuery.data.value as BusinessConsoleWmsWcsTaskListEnvelope | undefined,
      ),
    ),
    wcsTasksError: wcsTasksQuery.error,
    wcsTasksPending: wcsTasksQuery.isLoading,
    wcsTasksTotal: computed(() =>
      listTotal(wcsTasksQuery.data.value as BusinessConsoleWmsWcsTaskListEnvelope | undefined),
    ),
    refreshWcsTasks: () => refetchWithBusinessContext(filters, wcsTasksQuery),
    dispatchWcs: (
      warehouseTaskId: string,
      payload: {
        adapterType: string
        externalTaskId: string
        expectedVersion?: number
      },
    ) => {
      if (!Number.isInteger(payload.expectedVersion)) {
        return Promise.reject(new LifecycleStateChangedError('preflight'))
      }
      return dispatchMutation.mutateAsync({
        path: { warehouseTaskId },
        query: withQuery(),
        body: {
          adapterType: payload.adapterType,
          externalTaskId: payload.externalTaskId,
          expectedVersion: payload.expectedVersion!,
        },
      })
    },
    dispatchWcsPending: dispatchMutation.isLoading,
    dispatchWcsError: dispatchMutation.error,
    failWcs: (externalTaskId: string, payload: { failureCode: string; failureMessage: string }) =>
      failMutation.mutateAsync({ path: { externalTaskId }, query: withQuery(), body: payload }),
    failWcsPending: failMutation.isLoading,
    failWcsError: failMutation.error,
    completeWcs: (externalTaskId: string, payload: { completionPayloadJson: string }) =>
      completeMutation.mutateAsync({ path: { externalTaskId }, query: withQuery(), body: payload }),
    completeWcsPending: completeMutation.isLoading,
    completeWcsError: completeMutation.error,
  }
}

function warehouseTaskQuery(filters: WmsWarehouseTaskListFilters) {
  return {
    ...baseQuery(filters),
    ...optionalQuery('locationCode', filters.locationCode),
    ...optionalQuery('lotNo', filters.lotNo),
  }
}

// 上架任务（完工入库 → 上架增量）。后端在收货入库单下挂上架任务；创建需绑定 inboundOrderId。
export function useWmsPutawayTasks(initialFilters: Partial<WmsWarehouseTaskListFilters> = {}) {
  const filters = defaultFilters<WmsWarehouseTaskListFilters>(initialFilters)
  const putawayTasksQuery = useQuery(() =>
    withWmsListScopeEnabled(
      listBusinessConsoleWmsPutawayTasksQueryOptions({
        query: warehouseTaskQuery(filters),
      }),
      filters,
    ),
  )
  const putawayTasksScopeReady = computed(() => listScopeReady(filters))
  const putawayTasksResponse = useScopeBoundListResponse(
    () => putawayTasksQuery.data.value,
    () => listScopeKey(filters),
    putawayTasksScopeReady,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsPutawayTaskMutationOptions(),
    onSuccess() {
      void refetchWithWmsListScope(filters, putawayTasksQuery)
    },
  })

  return {
    filters,
    putawayTasks: computed<BusinessConsoleWmsWarehouseTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWarehouseTaskItem>(
        putawayTasksResponse.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    putawayTasksError: putawayTasksQuery.error,
    putawayTasksPending: putawayTasksQuery.isLoading,
    putawayTasksTotal: computed(() =>
      listTotal(
        putawayTasksResponse.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    refreshPutawayTasks: () => refetchWithWmsListScope(filters, putawayTasksQuery),
    createPutaway: (inboundOrderId: string, body: BusinessConsoleCreateWmsPutawayTaskRequest) =>
      createMutation.mutateAsync({
        path: { inboundOrderId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    createPutawayPending: createMutation.isLoading,
    createPutawayError: createMutation.error,
  }
}

// 拣货任务（领料齐套 → 出库拣货扣减）。后端在出库单下挂拣货任务；创建需绑定 outboundOrderId。
export function useWmsPickingTasks(initialFilters: Partial<WmsWarehouseTaskListFilters> = {}) {
  const filters = defaultFilters<WmsWarehouseTaskListFilters>(initialFilters)
  const pickingTasksQuery = useQuery(() =>
    withWmsListScopeEnabled(
      listBusinessConsoleWmsPickingTasksQueryOptions({
        query: warehouseTaskQuery(filters),
      }),
      filters,
    ),
  )
  const pickingTasksScopeReady = computed(() => listScopeReady(filters))
  const pickingTasksResponse = useScopeBoundListResponse(
    () => pickingTasksQuery.data.value,
    () => listScopeKey(filters),
    pickingTasksScopeReady,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsPickingTaskMutationOptions(),
    onSuccess() {
      void refetchWithWmsListScope(filters, pickingTasksQuery)
    },
  })

  const actionPending = shallowRef(false)

  /**
   * 拣货任务动作的「先读权威、再动手」执行器。
   *
   * 为什么不走 `statusActionGate`：`business-core` 里没有 warehouse-task 域，而后端每行已经
   * 回了 `allowedActions` / `blockReasons`——那是把派工、作业范围、状态机三件事一起算过的
   * 权威结论。再在前端抄一份状态表，只会多一处会漂移的真相（台账里「测试把错误固化成契约」
   * 那类问题的同源）。所以这里直接以服务端的 `allowedActions` 为闸门。
   *
   * 版本号也必须来自这次重读而不是列表里那行的旧快照：中间被别人改过就该撞 409，
   * 而不是拿着过期版本去覆盖。
   */
  async function runPickingAction<TData>(
    warehouseTaskId: string,
    taskNo: string | null | undefined,
    action: 'start' | 'complete' | 'exception',
    command: (context: { expectedVersion: number; scopeKind: string; scopeId: string }) => Promise<{
      data?: TData
      error?: unknown
      response?: Readonly<{ status: number }>
    }>,
  ) {
    const scope = trustedWorkScope(filters)
    actionPending.value = true
    try {
      const latest = await listBusinessConsoleWmsPickingTasks({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...scope,
          // 列表读面没有按 id 过滤的入参，用人读任务号收窄后再按 id 精确匹配。
          ...(taskNo?.trim() ? { keyword: taskNo.trim() } : {}),
          skip: 0,
          take: 50,
        },
        throwOnError: false,
      })
      const authoritative = exactSuccessfulItem<BusinessConsoleWmsWarehouseTaskItem>(
        latest,
        (candidate) => candidate.warehouseTaskId === warehouseTaskId,
      )
      if (!authoritative || !(authoritative.allowedActions ?? []).includes(action)) {
        throw new LifecycleStateChangedError('preflight')
      }

      const result = await command({
        expectedVersion: requireVersion(authoritative),
        ...scope,
      })
      if (result.response?.status === 409) throw new LifecycleStateChangedError('conflict')
      const envelopeError =
        result.data &&
        typeof result.data === 'object' &&
        'success' in result.data &&
        (result.data as { success?: boolean }).success === false
          ? result.data
          : undefined
      if (result.error !== undefined || envelopeError !== undefined) {
        throw result.error ?? envelopeError
      }
      await refetchWithWmsListScope(filters, pickingTasksQuery)
      return result.data
    } finally {
      actionPending.value = false
    }
  }

  return {
    filters,
    pickingTasks: computed<BusinessConsoleWmsWarehouseTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWarehouseTaskItem>(
        pickingTasksResponse.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    pickingTasksError: pickingTasksQuery.error,
    pickingTasksPending: pickingTasksQuery.isLoading,
    pickingTasksTotal: computed(() =>
      listTotal(
        pickingTasksResponse.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    refreshPickingTasks: () => refetchWithWmsListScope(filters, pickingTasksQuery),
    createPicking: (outboundOrderId: string, body: BusinessConsoleCreateWmsPickingTaskRequest) =>
      createMutation.mutateAsync({
        path: { outboundOrderId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    createPickingPending: createMutation.isLoading,
    createPickingError: createMutation.error,
    pickingActionPending: actionPending,
    startPicking: (task: BusinessConsoleWmsWarehouseTaskItem) =>
      runPickingAction(task.warehouseTaskId!, task.taskNo, 'start', (context) =>
        startBusinessConsoleWmsPickingTask({
          path: { warehouseTaskId: task.warehouseTaskId! },
          query: {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
            scopeKind: context.scopeKind,
            scopeId: context.scopeId,
          },
          body: {
            idempotencyKey: createWmsIdempotencyKey(),
            expectedVersion: context.expectedVersion,
          },
          throwOnError: false,
        }),
      ),
    completePicking: (
      task: BusinessConsoleWmsWarehouseTaskItem,
      payload: { executedQuantity: number; differenceReason?: string },
    ) =>
      runPickingAction(task.warehouseTaskId!, task.taskNo, 'complete', (context) =>
        completeBusinessConsoleWmsPickingTask({
          path: { warehouseTaskId: task.warehouseTaskId! },
          query: {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
            scopeKind: context.scopeKind,
            scopeId: context.scopeId,
          },
          body: {
            idempotencyKey: createWmsIdempotencyKey(),
            expectedVersion: context.expectedVersion,
            executedQuantity: payload.executedQuantity,
            // 少拣必须带原因，否则后端 422 `picking-difference-reason-required`。
            ...(payload.differenceReason?.trim()
              ? { differenceReason: payload.differenceReason.trim() }
              : {}),
          },
          throwOnError: false,
        }),
      ),
    reportPickingException: (
      task: BusinessConsoleWmsWarehouseTaskItem,
      payload: { exceptionCode: string; reason: string },
    ) =>
      runPickingAction(task.warehouseTaskId!, task.taskNo, 'exception', (context) =>
        reportBusinessConsoleWmsPickingTaskException({
          path: { warehouseTaskId: task.warehouseTaskId! },
          query: {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
            scopeKind: context.scopeKind,
            scopeId: context.scopeId,
          },
          body: {
            idempotencyKey: createWmsIdempotencyKey(),
            expectedVersion: context.expectedVersion,
            exceptionCode: payload.exceptionCode,
            reason: payload.reason,
          },
          throwOnError: false,
        }),
      ),
  }
}
