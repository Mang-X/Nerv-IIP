import {
  completeBusinessConsoleWmsInboundOrderMutationOptions,
  confirmBusinessConsoleOperation,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  type BusinessConsoleCompleteWmsInboundOrderRequest,
  type BusinessConsoleWmsInboundLineCaptureInput,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInboundOrderListEnvelope,
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
import { assertLifecycleActionExecutable } from '@/composables/lifecycleActionRecovery'
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
  type InboundLineCapture,
  type WmsCompletionAttemptOptions,
  type WmsInboundFilters,
  useAuthorizedWmsScope,
  usePagedAccumulator,
} from './wmsShared'

export function useWmsInbound(initialFilters: Partial<WmsInboundFilters> = {}) {
  const scope = useAuthorizedWmsScope('receipts')
  const filters = defaultFilters<WmsInboundFilters>({
    ...initialFilters,
    take: initialFilters.take ?? TASK_PAGE_SIZE,
  })
  const refreshing = shallowRef(false)
  const loadingMore = shallowRef(false)
  let activeLoadMoreToken: symbol | undefined

  const ordersQuery = useQuery(() =>
    scope.bindQuery(
      listBusinessConsoleWmsInboundOrdersQueryOptions({
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
    () => currentResponse.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
    scope.hasScope,
    (item: BusinessConsoleWmsInboundOrderItem) => item,
    (item) => stableItemKey(item, item.inboundOrderId, item.inboundOrderNo),
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
    const loadMoreToken = Symbol('wms-inbound-load-more')
    activeLoadMoreToken = loadMoreToken
    loadingMore.value = true
    const requestedSkip = page.nextSkip.value
    const requestScopeKey = scope.responseScopeKey.value
    const requestGeneration = pagingGeneration.value
    try {
      const { data } = await listBusinessConsoleWmsInboundOrders({
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
          data as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
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
    ...completeBusinessConsoleWmsInboundOrderMutationOptions(),
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
    sessionKey: scope.sessionKey,
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
    completeInbound: async (
      inboundOrderId: string,
      idempotencyKey: string,
      lines?: BusinessConsoleWmsInboundLineCaptureInput[],
      options: WmsCompletionAttemptOptions = { attempt: 'initial' },
    ) => {
      // 幂等键由页面在用户发起操作时生成一次并跨重试复用（防丢响应重复入库）；
      // org/env 取登录主体注入 query，调用方无法影响。lines 为收货现场采集的
      // 批号/效期（#935 闭环），随 complete 一并落库；无采集则不带 lines。
      const payloadFingerprint = `${inboundOrderId}:${JSON.stringify(lines ?? [])}`
      const intentScope = {
        principalId: scope.principalId.value,
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        operationType: 'wms.inbound-order.complete',
        payloadFingerprint,
      }
      const restoredPending = peekPendingBusinessIntent(intentScope)
      const isReplay = restoredPending !== undefined
      if (restoredPending && !requireFrozenScope(restoredPending.payloadSnapshot)) {
        throw lifecycleUnavailable()
      }
      const restoredSnapshot = restoredPending?.payloadSnapshot as
        | {
            lines?: InboundLineCapture[]
            scopeKind: string
            scopeId: string
            expectedVersion: number
          }
        | undefined
      const commandScope = restoredSnapshot
        ? {
            ...scope.tenantQuery(),
            scopeKind: restoredSnapshot.scopeKind,
            scopeId: restoredSnapshot.scopeId,
          }
        : scope.scopeQuery()
      const { data } = await listBusinessConsoleWmsInboundOrders({
        query: { ...commandScope, inboundOrderId, skip: 0, take: 2 },
        throwOnError: true,
      })
      const authoritative = exactItem(
        data as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
        (item: BusinessConsoleWmsInboundOrderItem) => item.inboundOrderId === inboundOrderId,
      )
      assertLifecycleActionExecutable({
        domain: 'wms-inbound',
        action: 'complete',
        facts: {
          status: authoritative?.status,
          idempotentReplay: isReplay,
        },
      })
      const freshPayload = restoredSnapshot ?? {
        lines: lines ?? [],
        scopeKind: commandScope.scopeKind,
        scopeId: commandScope.scopeId,
        expectedVersion: requireVersion(authoritative),
      }
      const pending = acquirePendingBusinessIntent(intentScope, () => idempotencyKey, freshPayload)
      if (!requireFrozenScope(pending.payloadSnapshot)) throw lifecycleUnavailable(authoritative)
      const frozen = pending.payloadSnapshot as typeof freshPayload
      const frozenLines = (frozen.lines as InboundLineCapture[] | undefined) ?? []
      const body = {
        idempotencyKey: pending.idempotencyKey,
        scopeKind: frozen.scopeKind,
        scopeId: frozen.scopeId,
        expectedVersion: frozen.expectedVersion,
        ...(frozenLines.length ? { lines: frozenLines } : {}),
      } satisfies BusinessConsoleCompleteWmsInboundOrderRequest
      options.onCommandAttempt?.()
      return completePendingBusinessIntent(intentScope, async () =>
        confirmBusinessConsoleOperation(
          await completeMutation.mutateAsync({
            path: { inboundOrderId },
            query: scope.tenantQuery(),
            body,
          }),
          {
            expectedOperationType: 'wms.inbound-order.complete',
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceId: inboundOrderId,
            readbackScope: { scopeKind: frozen.scopeKind, scopeId: frozen.scopeId },
          },
        ),
      )
    },
    completePending: completeMutation.isLoading,
  }
}
