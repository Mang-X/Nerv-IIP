import {
  completeBusinessConsoleWmsOutboundOrderMutationOptions,
  confirmBusinessConsoleOperation,
  listBusinessConsoleWmsOutboundOrders,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  type BusinessConsoleCompleteWmsOutboundOrderRequest,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsOutboundOrderListEnvelope,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
  useListResponseState,
  useScopeBoundListResponse,
} from '@nerv-iip/business-core'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, shallowRef, watch } from 'vue'
import { assertLifecycleActionExecutable } from '@/composables/shared/lifecycleActionRecovery'
import {
  TASK_PAGE_SIZE,
  defaultFilters,
  exactItem,
  isCurrentPagingRequest,
  lifecycleUnavailable,
  optionalQuery,
  requireFrozenScope,
  requireVersion,
  stableItemKey,
  type CompleteOutboundInput,
  type WmsCompletionAttemptOptions,
  type WmsTaskFilters,
  useAuthorizedWmsScope,
  usePagedAccumulator,
} from './wmsShared'

export function useWmsOutbound(initialFilters: Partial<WmsTaskFilters> = {}) {
  const scope = useAuthorizedWmsScope('shipments')
  const filters = defaultFilters<WmsTaskFilters>({
    ...initialFilters,
    take: initialFilters.take ?? TASK_PAGE_SIZE,
  })
  const refreshing = shallowRef(false)
  const loadingMore = shallowRef(false)
  let activeLoadMoreToken: symbol | undefined

  const ordersQuery = useQuery(() =>
    scope.bindQuery(
      listBusinessConsoleWmsOutboundOrdersQueryOptions({
        query: {
          ...scope.scopeQueryWithPaging(filters),
          ...optionalQuery('locationCode', filters.locationCode),
          ...optionalQuery('lotNo', filters.lotNo),
        },
      }),
    ),
  )
  const currentResponse = useScopeBoundListResponse(
    () => ordersQuery.data.value,
    scope.responseScopeKey,
    scope.hasScope,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scope.hasScope,
    ordersQuery.isLoading,
  )
  const page = usePagedAccumulator(
    () => currentResponse.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
    scope.hasScope,
    (item: BusinessConsoleWmsOutboundOrderItem) => item,
    (item) => stableItemKey(item, item.outboundOrderId, item.outboundOrderNo),
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
    refreshing.value = true
    try {
      await ordersQuery.refetch()
    } finally {
      refreshing.value = false
    }
  }

  async function loadMore() {
    if (
      !scope.hasScope.value ||
      ordersQuery.isLoading.value ||
      refreshing.value ||
      loadingMore.value ||
      page.exhausted.value
    ) {
      return
    }
    const loadMoreToken = Symbol('wms-outbound-load-more')
    activeLoadMoreToken = loadMoreToken
    loadingMore.value = true
    const requestedSkip = page.nextSkip.value
    const requestScopeKey = scope.responseScopeKey.value
    const requestGeneration = pagingGeneration.value
    try {
      const { data } = await listBusinessConsoleWmsOutboundOrders({
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
          data as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
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

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void refresh()
    },
  })

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    scopeKind: scope.scopeKind,
    scopeId: scope.scopeId,
    scopeKey: scope.selectedScopeKey,
    scopeOptions: scope.scopeOptions,
    selectedScopeLabel: scope.selectedScopeLabel,
    scopeReady: scope.hasScope,
    filters,
    orders: computed(() => page.items.value),
    total: computed(() => page.total.value),
    pending: computed(() => ordersQuery.isLoading.value || scope.pending.value),
    error: computed(() => ordersQuery.error.value ?? scope.error.value),
    loadMoreError,
    refreshing,
    loadingMore,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh,
    loadMore,
    completeOutbound: async (
      outboundOrderId: string,
      input: CompleteOutboundInput,
      options: WmsCompletionAttemptOptions = { attempt: 'initial' },
    ) => {
      // 页面提供 passed/idempotencyKey（幂等键跨重试复用）；复核单号由系统生成；
      // org/env 不取自 input，恒由登录主体注入 query，敌意 org/env 永远落空。
      const suppliedKey = input.idempotencyKey
      const payload = input.passed === undefined ? {} : { passed: input.passed }
      const intentScope = {
        principalId: scope.principalId.value,
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        operationType: 'wms.outbound-order.complete',
        payloadFingerprint: `${outboundOrderId}:${JSON.stringify(payload)}`,
      }
      const restoredPending = peekPendingBusinessIntent(intentScope)
      const isReplay = restoredPending !== undefined
      if (restoredPending && !requireFrozenScope(restoredPending.payloadSnapshot)) {
        throw lifecycleUnavailable()
      }
      const restoredSnapshot = restoredPending?.payloadSnapshot as
        | (typeof payload & {
            scopeKind: string
            scopeId: string
            expectedVersion: number
          })
        | undefined
      const commandScope = restoredSnapshot
        ? {
            ...scope.tenantQuery(),
            scopeKind: restoredSnapshot.scopeKind,
            scopeId: restoredSnapshot.scopeId,
          }
        : scope.scopeQuery()
      const { data } = await listBusinessConsoleWmsOutboundOrders({
        query: { ...commandScope, outboundOrderId, skip: 0, take: 2 },
        throwOnError: true,
      })
      const authoritative = exactItem(
        data as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
        (item: BusinessConsoleWmsOutboundOrderItem) => item.outboundOrderId === outboundOrderId,
      )
      assertLifecycleActionExecutable({
        domain: 'wms-outbound',
        action: 'complete',
        facts: {
          status: authoritative?.status,
          idempotentReplay: isReplay,
        },
      })
      const freshPayload = restoredSnapshot ?? {
        ...payload,
        scopeKind: commandScope.scopeKind,
        scopeId: commandScope.scopeId,
        expectedVersion: requireVersion(authoritative),
      }
      const pending = acquirePendingBusinessIntent(intentScope, () => suppliedKey, freshPayload)
      if (!requireFrozenScope(pending.payloadSnapshot)) throw lifecycleUnavailable(authoritative)
      const frozen = pending.payloadSnapshot as typeof freshPayload
      const body = {
        ...frozen,
        idempotencyKey: pending.idempotencyKey,
      } satisfies BusinessConsoleCompleteWmsOutboundOrderRequest
      options.onCommandAttempt?.()
      return completePendingBusinessIntent(intentScope, async () =>
        confirmBusinessConsoleOperation(
          await completeMutation.mutateAsync({
            path: { outboundOrderId },
            query: scope.tenantQuery(),
            body,
          }),
          {
            expectedOperationType: 'wms.outbound-order.complete',
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceId: outboundOrderId,
            readbackScope: { scopeKind: frozen.scopeKind, scopeId: frozen.scopeId },
            // 库存过账是异步事件，通常几秒内落定：多等一会儿拿到过账结果（#3926）；
            // 仍在过账就以「已提交、正在过账」收尾，不按结果不明处理。
            attempts: 20,
            retryDelayMs: 500,
          },
        ),
      )
    },
    completePending: completeMutation.isLoading,
  }
}
