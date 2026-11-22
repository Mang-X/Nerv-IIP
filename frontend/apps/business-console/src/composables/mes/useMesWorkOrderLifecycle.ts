import {
  cancelBusinessConsoleMesWorkOrder,
  closeBusinessConsoleMesWorkOrderMutationOptions,
  recordBusinessConsoleMesEngineeringChangeDecisionMutationOptions,
  holdBusinessConsoleMesWorkOrder,
  type BusinessConsoleMesCloseWorkOrderRequest,
  type BusinessConsoleMesEngineeringChangeDecisionRequest,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
} from '@nerv-iip/api-client'
import { useMutation, type useQueryCache } from '@pinia/colada'
import { shallowRef } from 'vue'
import { executeLifecycleAction } from '../shared/lifecycleAction'
import { ignoreBackgroundError, invalidateMesQueries, type MesWorkOrderContext } from './mesShared'
import type { useMesPrincipalWorkScope } from './useMesWorkScope'

export function useMesWorkOrderLifecycle(
  filters: MesWorkOrderContext,
  workOrderManageScope: ReturnType<typeof useMesPrincipalWorkScope>,
  queryCache: ReturnType<typeof useQueryCache>,
) {
  const cancelWorkOrderPending = shallowRef(false)
  const cancelWorkOrderError = shallowRef<unknown>()
  const holdWorkOrderPending = shallowRef(false)
  const holdWorkOrderError = shallowRef<unknown>()
  const refreshHeldWorkOrderQueries = () =>
    invalidateMesQueries(queryCache, [
      'getBusinessConsoleMesWorkOrderDetail',
      'listBusinessConsoleMesWorkOrders',
      'getBusinessConsoleMesOverview',
      'getBusinessConsoleMesWipSummary',
      'listBusinessConsoleMesOperationTasks',
      'listBusinessConsoleMesDispatchTasks',
    ])
  const refreshCancelledWorkOrderQueries = () =>
    invalidateMesQueries(queryCache, [
      // 本域：取消改动工单及其派生读模型（详情/列表/概览/在制/工序/派工/齐套/领料/完工入库）
      'getBusinessConsoleMesWorkOrderDetail',
      'listBusinessConsoleMesWorkOrders',
      'getBusinessConsoleMesOverview',
      'getBusinessConsoleMesWipSummary',
      'listBusinessConsoleMesOperationTasks',
      'listBusinessConsoleMesDispatchTasks',
      'getBusinessConsoleMesMaterialReadiness',
      'listBusinessConsoleMesMaterialIssueRequests',
      'listBusinessConsoleMesFinishedGoodsReceiptRequests',
      // 跨域（A1 §4.2 跨域刷新首个落地）：预留释放后库存可用量恢复，库存可用量读面必须失效
      'getBusinessConsoleInventoryAvailability',
    ])

  async function cancelWorkOrder(reason: string) {
    const selectedManageScope = workOrderManageScope.requireSelectedScope()
    cancelWorkOrderPending.value = true
    cancelWorkOrderError.value = undefined
    try {
      const result = await executeLifecycleAction({
        readLatest: async () => {
          const query = getBusinessConsoleMesWorkOrderDetailQueryOptions({
            path: { workOrderId: filters.workOrderId },
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              scopeKind: selectedManageScope.kind,
              scopeId: selectedManageScope.id,
            },
          })
          const response = await query.query({
            signal: new AbortController().signal,
          } as Parameters<typeof query.query>[0])
          const item = response?.success ? response.data : undefined
          return item
            ? {
                domain: 'mes-work-order' as const,
                action: 'cancel' as const,
                facts: { status: item.status },
              }
            : undefined
        },
        command: () =>
          cancelBusinessConsoleMesWorkOrder({
            path: { workOrderId: filters.workOrderId },
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              scopeKind: selectedManageScope.kind,
              scopeId: selectedManageScope.id,
            },
            body: { reason },
            throwOnError: false,
          }),
      })
      await refreshCancelledWorkOrderQueries()
      return result
    } catch (error) {
      cancelWorkOrderError.value = error
      throw error
    } finally {
      cancelWorkOrderPending.value = false
    }
  }

  async function holdWorkOrder(reason: string) {
    const selectedManageScope = workOrderManageScope.requireSelectedScope()
    holdWorkOrderPending.value = true
    holdWorkOrderError.value = undefined
    try {
      const result = await executeLifecycleAction({
        readLatest: async () => {
          const query = getBusinessConsoleMesWorkOrderDetailQueryOptions({
            path: { workOrderId: filters.workOrderId },
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              scopeKind: selectedManageScope.kind,
              scopeId: selectedManageScope.id,
            },
          })
          const response = await query.query({
            signal: new AbortController().signal,
          } as Parameters<typeof query.query>[0])
          const item = response?.success ? response.data : undefined
          return item
            ? {
                domain: 'mes-work-order' as const,
                action: 'hold' as const,
                facts: { status: item.status },
              }
            : undefined
        },
        command: () =>
          holdBusinessConsoleMesWorkOrder({
            path: { workOrderId: filters.workOrderId },
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              scopeKind: selectedManageScope.kind,
              scopeId: selectedManageScope.id,
            },
            body: { reason },
            throwOnError: false,
          }),
      })
      await refreshHeldWorkOrderQueries()
      return result
    } catch (error) {
      holdWorkOrderError.value = error
      throw error
    } finally {
      holdWorkOrderPending.value = false
    }
  }

  const closeWorkOrderMutation = useMutation({
    ...closeBusinessConsoleMesWorkOrderMutationOptions(),
    onSuccess() {
      void refreshCancelledWorkOrderQueries().catch(ignoreBackgroundError)
    },
  })
  const engineeringChangeDecisionMutation = useMutation({
    ...recordBusinessConsoleMesEngineeringChangeDecisionMutationOptions(),
    onSuccess() {
      void refreshCancelledWorkOrderQueries().catch(ignoreBackgroundError)
    },
  })

  async function closeWorkOrder(
    body: Pick<BusinessConsoleMesCloseWorkOrderRequest, 'closedAtUtc'> = {},
  ) {
    const selectedManageScope = workOrderManageScope.requireSelectedScope()
    return closeWorkOrderMutation.mutateAsync({
      path: { workOrderId: filters.workOrderId },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        scopeKind: selectedManageScope.kind,
        scopeId: selectedManageScope.id,
      },
      body,
    })
  }

  async function recordEngineeringChangeDecision(
    body: Pick<
      BusinessConsoleMesEngineeringChangeDecisionRequest,
      'changeNumber' | 'decision' | 'reason'
    >,
  ) {
    const selectedManageScope = workOrderManageScope.requireSelectedScope()
    return engineeringChangeDecisionMutation.mutateAsync({
      path: { workOrderId: filters.workOrderId },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        scopeKind: selectedManageScope.kind,
        scopeId: selectedManageScope.id,
      },
      body,
    })
  }

  return {
    cancelWorkOrder,
    cancelWorkOrderError,
    cancelWorkOrderPending,
    closeWorkOrder,
    closeWorkOrderError: closeWorkOrderMutation.error,
    closeWorkOrderPending: closeWorkOrderMutation.isLoading,
    holdWorkOrder,
    holdWorkOrderError,
    holdWorkOrderPending,
    recordEngineeringChangeDecision,
    recordEngineeringChangeDecisionError: engineeringChangeDecisionMutation.error,
    recordEngineeringChangeDecisionPending: engineeringChangeDecisionMutation.isLoading,
  }
}
