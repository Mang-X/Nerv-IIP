import {
  completeBusinessConsoleWmsOutboundOrder,
  confirmBusinessConsoleOperation,
  createBusinessConsoleWmsOutboundOrderMutationOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrders,
  type BusinessConsoleCreateWmsOutboundOrderRequest,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsOutboundOrderListEnvelope,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
  useScopeBoundListResponse,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, shallowRef } from 'vue'
import { executeLifecycleAction } from '../shared/lifecycleAction'
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
  requireFrozenCompletion,
  requireVersion,
  trustedWorkScope,
  type WmsCompletionAttemptOptions,
  type WmsListFilters,
  withWmsListScopeEnabled,
} from './wmsShared'

export interface WmsOutboundListFilters extends WmsListFilters {
  locationCode?: string
  lotNo?: string
}

export function useWmsOutboundOrders(initialFilters: Partial<WmsOutboundListFilters> = {}) {
  const auth = useAuthStore()
  const filters = defaultFilters<WmsOutboundListFilters>(initialFilters)
  const outboundOrdersQuery = useQuery(() => ({
    ...withWmsListScopeEnabled(
      listBusinessConsoleWmsOutboundOrdersQueryOptions({
        query: {
          ...baseQuery(filters),
          ...optionalQuery('locationCode', filters.locationCode),
          ...optionalQuery('lotNo', filters.lotNo),
        },
      }),
      filters,
    ),
    autoRefetch: (state: { data?: BusinessConsoleWmsOutboundOrderListEnvelope }) =>
      state.data?.data?.items?.some((order) => order.inventoryPostingStatus === 'pending')
        ? 10_000
        : false,
  }))
  const outboundOrdersScopeReady = computed(() => listScopeReady(filters))
  const outboundOrdersResponse = useScopeBoundListResponse(
    () => outboundOrdersQuery.data.value,
    () => listScopeKey(filters),
    outboundOrdersScopeReady,
  )

  const completeOutboundPending = shallowRef(false)
  const completeOutboundError = shallowRef<unknown>()
  const createMutation = useMutation({
    ...createBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void refetchWithWmsListScope(filters, outboundOrdersQuery)
    },
  })

  async function completeOutboundOrder(
    outboundOrderId: string,
    payload: { passed: boolean },
    idempotencyKey = createWmsIdempotencyKey(),
    options: WmsCompletionAttemptOptions = { attempt: 'initial' },
  ) {
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      operationType: 'wms.outbound-order.complete',
      payloadFingerprint: `${outboundOrderId}:${JSON.stringify(payload)}`,
    }
    const restored = peekPendingBusinessIntent(scope)
    const commandScope = trustedWorkScope(filters, restored?.payloadSnapshot)
    let authoritative: BusinessConsoleWmsOutboundOrderItem | undefined
    let pending: ReturnType<typeof acquirePendingBusinessIntent> | undefined
    completeOutboundPending.value = true
    completeOutboundError.value = undefined
    try {
      const result = await completePendingBusinessIntent(scope, async () => {
        const envelope = await executeLifecycleAction({
          readLatest: async () => {
            const response = await listBusinessConsoleWmsOutboundOrders({
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
                ...commandScope,
                outboundOrderId,
                skip: 0,
                take: 2,
              },
              throwOnError: false,
            })
            authoritative = exactSuccessfulItem<BusinessConsoleWmsOutboundOrderItem>(
              response,
              (candidate) => candidate.outboundOrderId === outboundOrderId,
            )
            return authoritative
              ? {
                  domain: 'wms-outbound' as const,
                  action: 'complete' as const,
                  facts: {
                    status: authoritative.status,
                    idempotentReplay: restored !== undefined,
                  },
                }
              : undefined
          },
          command: () => {
            pending = acquirePendingBusinessIntent(
              scope,
              () => idempotencyKey,
              restored?.payloadSnapshot ?? {
                ...payload,
                ...commandScope,
                expectedVersion: requireVersion(authoritative),
              },
            )
            const stablePayload = requireFrozenCompletion<typeof payload>(
              pending.payloadSnapshot,
              '出库完成',
            )
            options.onCommandAttempt?.()
            return completeBusinessConsoleWmsOutboundOrder({
              path: { outboundOrderId },
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
              },
              body: {
                passed: stablePayload.passed,
                idempotencyKey: pending.idempotencyKey,
                scopeKind: stablePayload.scopeKind,
                scopeId: stablePayload.scopeId,
                expectedVersion: stablePayload.expectedVersion,
              },
              throwOnError: false,
            })
          },
        })
        if (!envelope || !pending) throw new Error('出库完成未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'wms.outbound-order.complete',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: outboundOrderId,
          // 回读走的是范围受限的列表读面，不带范围必 403 → 成功也会被报成「尚未确认」（#1397）。
          readbackScope: commandScope,
          // 库存过账是异步事件，通常几秒内落定：多等一会儿拿到过账结果（#3926）；
          // 仍在过账就以「已提交、正在过账」收尾，不报失败。
          attempts: 20,
          retryDelayMs: 500,
        })
        return envelope
      })
      await refetchWithWmsListScope(filters, outboundOrdersQuery)
      return result
    } catch (error) {
      completeOutboundError.value = error
      throw error
    } finally {
      completeOutboundPending.value = false
    }
  }

  return {
    filters,
    outboundOrders: computed<BusinessConsoleWmsOutboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsOutboundOrderItem>(
        outboundOrdersResponse.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
      ),
    ),
    outboundOrdersError: outboundOrdersQuery.error,
    outboundOrdersPending: outboundOrdersQuery.isLoading,
    outboundOrdersTotal: computed(() =>
      listTotal(
        outboundOrdersResponse.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
      ),
    ),
    refreshOutboundOrders: () => refetchWithWmsListScope(filters, outboundOrdersQuery),
    completeOutbound: completeOutboundOrder,
    completeOutboundPending,
    completeOutboundError,
    createOutbound: (body: BusinessConsoleCreateWmsOutboundOrderRequest) =>
      createMutation.mutateAsync({ body }),
    createOutboundPending: createMutation.isLoading,
    createOutboundError: createMutation.error,
  }
}
