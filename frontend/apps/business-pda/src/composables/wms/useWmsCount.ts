import {
  completeBusinessConsoleWmsCountExecutionMutationOptions,
  confirmBusinessConsoleOperation,
  listBusinessConsoleWmsCountExecutions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  type BusinessConsoleCompleteWmsCountExecutionRequest,
  type BusinessConsoleWmsCountExecutionItem,
  type BusinessConsoleWmsCountExecutionListEnvelope,
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
  type CompleteCountInput,
  type WmsCompletionAttemptOptions,
  type WmsTaskFilters,
  useAuthorizedWmsScope,
  usePagedAccumulator,
} from './wmsShared'

export function useWmsCount(initialFilters: Partial<WmsTaskFilters> = {}) {
  const scope = useAuthorizedWmsScope('counts')
  const filters = defaultFilters<WmsTaskFilters>({
    ...initialFilters,
    take: initialFilters.take ?? TASK_PAGE_SIZE,
  })
  const refreshing = shallowRef(false)
  const loadingMore = shallowRef(false)
  let activeLoadMoreToken: symbol | undefined

  const executionsQuery = useQuery(() =>
    scope.bindQuery(
      listBusinessConsoleWmsCountExecutionsQueryOptions({
        query: {
          ...scope.scopeQueryWithPaging(filters),
          ...optionalQuery('locationCode', filters.locationCode),
        },
      }),
    ),
  )
  const currentResponse = useScopeBoundListResponse(
    () => executionsQuery.data.value,
    scope.responseScopeKey,
    scope.hasScope,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scope.hasScope,
    executionsQuery.isLoading,
  )
  const page = usePagedAccumulator(
    () => currentResponse.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
    scope.hasScope,
    (item: BusinessConsoleWmsCountExecutionItem) => item,
    (item) => stableItemKey(item, item.countExecutionId, item.countNo),
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
      await executionsQuery.refetch()
    } finally {
      refreshing.value = false
    }
  }

  async function loadMore() {
    if (
      !scope.hasScope.value ||
      executionsQuery.isLoading.value ||
      refreshing.value ||
      loadingMore.value ||
      page.exhausted.value
    ) {
      return
    }
    const loadMoreToken = Symbol('wms-count-load-more')
    activeLoadMoreToken = loadMoreToken
    loadingMore.value = true
    const requestedSkip = page.nextSkip.value
    const requestScopeKey = scope.responseScopeKey.value
    const requestGeneration = pagingGeneration.value
    try {
      const { data } = await listBusinessConsoleWmsCountExecutions({
        query: {
          ...scope.scopeQuery(),
          skip: requestedSkip,
          take: TASK_PAGE_SIZE,
          ...optionalQuery('status', filters.status),
          ...optionalQuery('keyword', filters.keyword),
          ...optionalQuery('locationCode', filters.locationCode),
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
          data as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
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
    ...completeBusinessConsoleWmsCountExecutionMutationOptions(),
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
    executions: computed(() => page.items.value),
    total: computed(() => page.total.value),
    pending: computed(() => executionsQuery.isLoading.value || scope.pending.value),
    error: computed(() => executionsQuery.error.value ?? scope.error.value),
    loadMoreError,
    refreshing,
    loadingMore,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh,
    loadMore,
    completeCount: async (
      countExecutionId: string,
      input: CompleteCountInput,
      options: WmsCompletionAttemptOptions = { attempt: 'initial' },
    ) => {
      // 页面提供 countedQuantity/idempotencyKey（幂等键跨重试复用）；
      // org/env 不取自 input，恒由登录主体注入 query，敌意 org/env 永远落空。
      const suppliedKey = input.idempotencyKey
      const payload =
        input.countedQuantity === undefined ? {} : { countedQuantity: input.countedQuantity }
      const intentScope = {
        principalId: scope.principalId.value,
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        operationType: 'wms.count-execution.complete',
        payloadFingerprint: `${countExecutionId}:${JSON.stringify(payload)}`,
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
      const { data } = await listBusinessConsoleWmsCountExecutions({
        query: { ...commandScope, countExecutionId, skip: 0, take: 2 },
        throwOnError: true,
      })
      const authoritative = exactItem(
        data as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
        (item: BusinessConsoleWmsCountExecutionItem) => item.countExecutionId === countExecutionId,
      )
      assertLifecycleActionExecutable({
        domain: 'wms-count',
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
      } satisfies BusinessConsoleCompleteWmsCountExecutionRequest
      options.onCommandAttempt?.()
      return completePendingBusinessIntent(intentScope, async () =>
        confirmBusinessConsoleOperation(
          await completeMutation.mutateAsync({
            path: { countExecutionId },
            query: scope.tenantQuery(),
            body,
          }),
          {
            expectedOperationType: 'wms.count-execution.complete',
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceId: countExecutionId,
            readbackScope: { scopeKind: frozen.scopeKind, scopeId: frozen.scopeId },
          },
        ),
      )
    },
    completePending: completeMutation.isLoading,
  }
}
