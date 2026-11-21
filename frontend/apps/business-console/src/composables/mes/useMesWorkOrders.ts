import {
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  getBusinessConsoleMesWorkOrderTransformationQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  mergeBusinessConsoleMesWorkOrdersMutationOptions,
  releaseBusinessConsoleMesWorkOrderMutationOptions,
  splitBusinessConsoleMesWorkOrderMutationOptions,
  type BusinessConsoleCreateRushWorkOrderRequest,
  type BusinessConsoleMesWorkOrderItem,
  type BusinessConsoleMesWorkOrderListEnvelope,
} from '@nerv-iip/api-client'
import { useScopeBoundListResponse } from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import { hasBusinessContext, type BusinessContextFields } from '../businessContextBinding'
import { businessReadState } from '../businessReadState'
import { mesWorkOrderReleaseBlocker } from './workOrderRelease'
import {
  MES_WORK_ORDERS_MANAGE_PERMISSION,
  MES_WORK_ORDERS_READ_PERMISSION,
  MES_WORK_SCOPE_REQUIRED_MESSAGE,
  defaultFilters,
  defaultWorkOrderContext,
  envelopeTotal,
  ignoreBackgroundError,
  invalidateMesQueries,
  toContextQuery,
  toListQuery,
} from './mesShared'
import { type MesSelectedWorkScope, useMesPrincipalWorkScope } from './useMesWorkScope'
import { useMesProductionReporting } from './useMesProductionReporting'

function listItems(envelope: BusinessConsoleMesWorkOrderListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

export interface UseMesWorkOrdersOptions {
  initialTake?: number
}

/** 方案关联工单按 ID 读取，包含不再进入待排池的完成/关闭工单。 */
export type SchedulingWorkOrderFacts = BusinessConsoleMesWorkOrderItem & {
  executionStatus?: string
}

export function useMesWorkOrderFacts(orderIds: MaybeRefOrGetter<readonly (string | undefined)[]>) {
  const filters = defaultWorkOrderContext()
  const scope = useMesPrincipalWorkScope(filters, MES_WORK_ORDERS_READ_PERMISSION)
  const ids = computed(() =>
    [...new Set(toValue(orderIds).filter((id): id is string => Boolean(id)))].sort(),
  )
  const enabled = computed(
    () => hasBusinessContext(filters) && scope.scopeReady.value && ids.value.length > 0,
  )
  const identity = computed(() =>
    [
      scope.principalIdentity.value,
      filters.organizationId,
      filters.environmentId,
      scope.selectedScope.value?.kind,
      scope.selectedScope.value?.id,
      ...ids.value,
    ].join(':'),
  )
  const query = useQuery(() => ({
    key: [{ _id: 'schedulingWorkOrderFacts' }, identity.value],
    enabled: enabled.value,
    query: async (context: { signal: AbortSignal }): Promise<SchedulingWorkOrderFacts[]> => {
      const selectedScope = scope.selectedScope.value!
      return Promise.all(
        ids.value.map(async (workOrderId) => {
          const options = getBusinessConsoleMesWorkOrderDetailQueryOptions({
            path: { workOrderId },
            query: {
              ...toContextQuery(filters),
              scopeKind: selectedScope.kind,
              scopeId: selectedScope.id,
            },
          })
          const response = await options.query(context as Parameters<typeof options.query>[0])
          if (response?.success !== true || !response.data)
            throw new Error(response?.message ?? '关联工单事实读取失败')
          return {
            workOrderId,
            commercialSourceFacts: response.data.commercialSourceFacts,
            executionStatus: response.data.status,
            quantity: response.data.quantity,
            completedQuantity: response.data.completedQuantity,
            operationTasks: response.data.operationTasks,
          }
        }),
      )
    },
  }))
  const response = useScopeBoundListResponse(() => query.data.value, identity, enabled)
  return { workOrders: computed(() => response.value ?? []), error: query.error }
}

export function useMesWorkOrders(options: UseMesWorkOrdersOptions = {}) {
  const filters = defaultFilters(options.initialTake)
  const workOrderReadScope = useMesPrincipalWorkScope(filters, MES_WORK_ORDERS_READ_PERMISSION)
  const auth = useAuthStore()
  const workOrderManageScope = useMesPrincipalWorkScope(
    filters,
    MES_WORK_ORDERS_MANAGE_PERMISSION,
    () => auth.principal?.permissionCodes?.includes(MES_WORK_ORDERS_MANAGE_PERMISSION) === true,
  )
  const queryCache = useQueryCache()
  const workOrdersScopeReady = computed(
    () => hasBusinessContext(filters) && workOrderReadScope.scopeReady.value,
  )
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
      enabled: workOrdersScopeReady.value,
    }
  })
  const workOrdersResponse = useScopeBoundListResponse(
    () => workOrdersQuery.data.value,
    workOrdersIdentity,
    workOrdersScopeReady,
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

  // 报工写操作与工单列表解耦，统一走 useMesProductionReporting（报工弹窗在工序执行页也用同一个）。
  const reporting = useMesProductionReporting()

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

  async function readReleaseCandidate(
    workOrderId: string,
    selectedReadScope: MesSelectedWorkScope,
    selectedManageScope: MesSelectedWorkScope,
  ) {
    const query = getBusinessConsoleMesWorkOrderDetailQueryOptions({
      path: { workOrderId },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        scopeKind: selectedReadScope.kind,
        scopeId: selectedReadScope.id,
      },
    })
    const response = await query.query({
      signal: new AbortController().signal,
    } as Parameters<typeof query.query>[0])
    if (response?.success !== true || !response.data) {
      throw new Error(response?.message ?? '未取得当前读取范围内的工单详情')
    }
    if (!workOrderManageScope.coversWorkOrder(response.data, selectedManageScope)) {
      throw new Error('当前工单不在所选管理范围内，不能下达')
    }
    return response.data
  }

  return {
    createRushWorkOrder: (body: BusinessConsoleCreateRushWorkOrderRequest) =>
      createRushMutation.mutateAsync({ body }),
    createRushWorkOrderError: createRushMutation.error,
    createRushWorkOrderPending: createRushMutation.isLoading,
    filters,
    recordProductionReport: reporting.recordProductionReport,
    recordProductionReportError: reporting.recordProductionReportError,
    recordProductionReportPending: reporting.recordProductionReportPending,
    refreshWorkOrders: () =>
      workOrdersScopeReady.value ? workOrdersQuery.refetch() : Promise.resolve(),
    readWorkOrderForRelease: async (workOrderId: string) => {
      const selectedReadScope = workOrderReadScope.requireSelectedScope()
      const selectedManageScope = workOrderManageScope.requireSelectedScope()
      return readReleaseCandidate(workOrderId, selectedReadScope, selectedManageScope)
    },
    releaseWorkOrder: async (
      workOrderId: string,
      body: {
        organizationId: string
        environmentId: string
        confirmWarnings: boolean
      },
    ) => {
      const selectedReadScope = workOrderReadScope.requireSelectedScope()
      const selectedManageScope = workOrderManageScope.requireSelectedScope()
      const latest = await readReleaseCandidate(workOrderId, selectedReadScope, selectedManageScope)
      const blocker = mesWorkOrderReleaseBlocker(latest)
      if (blocker) throw new Error(blocker)
      return releaseMutation.mutateAsync({
        path: { workOrderId },
        query: {
          organizationId: body.organizationId,
          environmentId: body.environmentId,
          scopeKind: selectedManageScope.kind,
          scopeId: selectedManageScope.id,
        },
        body,
      })
    },
    releaseWorkOrderError: releaseMutation.error,
    releaseWorkOrderPending: releaseMutation.isLoading,
    workOrders: computed<BusinessConsoleMesWorkOrderItem[]>(() =>
      listItems(workOrdersResponse.value),
    ),
    workOrdersError: workOrdersQuery.error,
    workOrdersPending: workOrdersQuery.isLoading,
    workOrdersState: businessReadState(workOrdersQuery, () => workOrdersScopeReady.value),
    workOrdersTotal: computed(() => envelopeTotal(workOrdersResponse.value)),
    workOrderReadScope: workOrderReadScope.selectedScope,
    workOrderReadScopeMessage: workOrderReadScope.scopeMessage,
    workOrderReadScopePending: workOrderReadScope.scopePending,
    workOrderReadScopeReady: workOrderReadScope.scopeReady,
    workOrderManageScope: workOrderManageScope.selectedScope,
    workOrderManageScopeMessage: workOrderManageScope.scopeMessage,
    workOrderManageScopePending: workOrderManageScope.scopePending,
    workOrderManageScopeReady: workOrderManageScope.scopeReady,
  }
}

export interface MesWorkOrderTransformationMutation {
  accepted: boolean
  transformationId: string
  type: string
  sourceWorkOrderIds: string[]
  targetWorkOrderIds: string[]
  isIdempotentReplay: boolean
}

export interface MesWorkOrderTransformationLine {
  sourceWorkOrderId: string
  targetWorkOrderId: string
  quantity: number
  uomCode: string
  sourceStatus: string
  targetStatus: string
  sourceVersion: number
  targetVersion: number
}

export interface MesWorkOrderTransformationReadback {
  transformationId: string
  type: string
  idempotencyKey: string
  actor: string
  reason: string
  occurredAtUtc: string
  lines: MesWorkOrderTransformationLine[]
}

export interface MesWorkOrderTransformationResult {
  mutation: MesWorkOrderTransformationMutation
  readback?: MesWorkOrderTransformationReadback
  readbackError?: unknown
}

export interface MesWorkOrderTransformationContext {
  filters: Pick<BusinessContextFields, 'organizationId' | 'environmentId'>
  readScope: { value: MesSelectedWorkScope | undefined }
  manageScope: { value: MesSelectedWorkScope | undefined }
}

type WorkOrderTransformationMutationEnvelope = {
  success?: boolean
  message?: string
  data?: MesWorkOrderTransformationMutation | null
}

type WorkOrderTransformationReadbackEnvelope = {
  success?: boolean
  message?: string
  data?: MesWorkOrderTransformationReadback | null
}

export function useMesWorkOrderTransformations(context: MesWorkOrderTransformationContext) {
  const queryCache = useQueryCache()
  const splitMutation = useMutation({
    ...splitBusinessConsoleMesWorkOrderMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'getBusinessConsoleMesWorkOrderDetail',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesOverview',
        'getBusinessConsoleMesWipSummary',
        'listBusinessConsoleMesOperationTasks',
        'listBusinessConsoleMesDispatchTasks',
      ]).catch(ignoreBackgroundError)
    },
  })
  const mergeMutation = useMutation({
    ...mergeBusinessConsoleMesWorkOrdersMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'getBusinessConsoleMesWorkOrderDetail',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesOverview',
        'getBusinessConsoleMesWipSummary',
        'listBusinessConsoleMesOperationTasks',
        'listBusinessConsoleMesDispatchTasks',
      ]).catch(ignoreBackgroundError)
    },
  })

  function requireScope(scope: { value: MesSelectedWorkScope | undefined }) {
    if (!scope.value) throw new Error(MES_WORK_SCOPE_REQUIRED_MESSAGE)
    return scope.value
  }

  function scopedQuery(scope: MesSelectedWorkScope) {
    return {
      organizationId: context.filters.organizationId,
      environmentId: context.filters.environmentId,
      scopeKind: scope.kind,
      scopeId: scope.id,
    }
  }

  async function readTransformation(transformationId: string) {
    const scope = requireScope(context.readScope)
    const query = getBusinessConsoleMesWorkOrderTransformationQueryOptions({
      path: { transformationId },
      query: scopedQuery(scope),
    })
    const response = (await query.query({
      signal: new AbortController().signal,
    } as Parameters<typeof query.query>[0])) as WorkOrderTransformationReadbackEnvelope
    if (response.success !== true || !response.data) {
      throw new Error(response.message ?? '工单拆分或合并已受理，但回读结果暂不可用。')
    }
    return response.data
  }

  async function readAfterAccepted(mutation: MesWorkOrderTransformationMutation) {
    try {
      return { readback: await readTransformation(mutation.transformationId) }
    } catch (readbackError) {
      return { readbackError }
    }
  }

  async function splitWorkOrder(
    workOrderId: string,
    body: {
      targets: Array<{ workOrderId: string; quantity: number }>
      reason: string
      idempotencyKey: string
    },
  ): Promise<MesWorkOrderTransformationResult> {
    const scope = requireScope(context.manageScope)
    const response = (await splitMutation.mutateAsync({
      path: { workOrderId },
      query: scopedQuery(scope),
      body,
    })) as WorkOrderTransformationMutationEnvelope
    if (response.success !== true || !response.data?.accepted || !response.data.transformationId) {
      throw new Error(response.message ?? '拆分结果未确认，请先刷新工单核实。')
    }
    return { mutation: response.data, ...(await readAfterAccepted(response.data)) }
  }

  async function mergeWorkOrders(body: {
    sourceWorkOrderIds: string[]
    targetWorkOrderId: string
    reason: string
    idempotencyKey: string
  }): Promise<MesWorkOrderTransformationResult> {
    const scope = requireScope(context.manageScope)
    const response = (await mergeMutation.mutateAsync({
      query: scopedQuery(scope),
      body,
    })) as WorkOrderTransformationMutationEnvelope
    if (response.success !== true || !response.data?.accepted || !response.data.transformationId) {
      throw new Error(response.message ?? '合并结果未确认，请先刷新工单核实。')
    }
    return { mutation: response.data, ...(await readAfterAccepted(response.data)) }
  }

  return {
    mergeWorkOrders,
    mergeWorkOrdersError: mergeMutation.error,
    mergeWorkOrdersPending: mergeMutation.isLoading,
    readTransformation,
    splitWorkOrder,
    splitWorkOrderError: splitMutation.error,
    splitWorkOrderPending: splitMutation.isLoading,
  }
}
