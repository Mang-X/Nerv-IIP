import {
  completeBusinessConsoleWmsCountExecution,
  confirmBusinessConsoleOperation,
  createBusinessConsoleWmsCountExecutionMutationOptions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsCountExecutions,
  type BusinessConsoleCreateWmsCountExecutionRequest,
  type BusinessConsoleWmsCountExecutionItem,
  type BusinessConsoleWmsCountExecutionListEnvelope,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
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
  type WmsWarehouseTaskListFilters,
  withWmsListScopeEnabled,
} from './wmsShared'

// 盘点执行（库位 × SKU 的账面 vs 实盘）。完成盘点按差额触发库存调整移动。
export function useWmsCountExecutions(initialFilters: Partial<WmsWarehouseTaskListFilters> = {}) {
  const auth = useAuthStore()
  const filters = defaultFilters<WmsWarehouseTaskListFilters>(initialFilters)
  const countExecutionsQuery = useQuery(() =>
    withWmsListScopeEnabled(
      listBusinessConsoleWmsCountExecutionsQueryOptions({
        query: {
          ...baseQuery(filters),
          ...optionalQuery('locationCode', filters.locationCode),
        },
      }),
      filters,
    ),
  )
  const countExecutionsScopeReady = computed(() => listScopeReady(filters))
  const countExecutionsResponse = useScopeBoundListResponse(
    () => countExecutionsQuery.data.value,
    () => listScopeKey(filters),
    countExecutionsScopeReady,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsCountExecutionMutationOptions(),
    onSuccess() {
      void refetchWithWmsListScope(filters, countExecutionsQuery)
    },
  })
  const completeCountExecutionPending = shallowRef(false)
  const completeCountExecutionError = shallowRef<unknown>()

  function completionIntent(countExecutionId: string, countedQuantity: number) {
    return {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      operationType: 'wms.count-execution.complete',
      payloadFingerprint: `${countExecutionId}:${countedQuantity}`,
    }
  }

  async function readCountExecution(
    countExecutionId: string,
    tenant: { organizationId: string; environmentId: string },
    commandScope: ReturnType<typeof trustedWorkScope>,
  ) {
    const response = await listBusinessConsoleWmsCountExecutions({
      query: {
        organizationId: tenant.organizationId,
        environmentId: tenant.environmentId,
        ...commandScope,
        countExecutionId,
        skip: 0,
        take: 2,
      },
      throwOnError: false,
    })
    return exactSuccessfulItem<BusinessConsoleWmsCountExecutionItem>(
      response,
      (candidate) => candidate.countExecutionId === countExecutionId,
    )
  }

  async function verifyCountExecution(countExecutionId: string, countedQuantity: number) {
    const scope = completionIntent(countExecutionId, countedQuantity)
    const restored = peekPendingBusinessIntent(scope)
    const commandScope = trustedWorkScope(filters, restored?.payloadSnapshot)
    const found = await readCountExecution(countExecutionId, scope, commandScope)
    const current = completionIntent(countExecutionId, countedQuantity)
    if (
      !found ||
      current.principalId !== scope.principalId ||
      current.organizationId !== scope.organizationId ||
      current.environmentId !== scope.environmentId
    ) {
      throw new Error('无法核验当前盘点单，请重试。')
    }
    const postingStatus = found.inventoryPostingStatus?.toLowerCase()
    const postingFailed = postingStatus === 'failed'
    const matchesSubmittedQuantity =
      typeof found.countedQuantity === 'number' && found.countedQuantity === countedQuantity
    const settled =
      matchesSubmittedQuantity &&
      (postingStatus === 'posted' ||
        postingFailed ||
        found.status === 'PendingApproval' ||
        found.status === 'RecountRequired')
    if (settled) {
      clearPendingBusinessIntent(scope)
      void refetchWithWmsListScope(filters, countExecutionsQuery)
    }
    return { row: found, settled, postingFailed }
  }

  async function completeCountExecution(
    countExecutionId: string,
    countedQuantity: number,
    idempotencyKey = createWmsIdempotencyKey(),
    options: WmsCompletionAttemptOptions = { attempt: 'initial' },
  ) {
    const scope = completionIntent(countExecutionId, countedQuantity)
    const restored = peekPendingBusinessIntent(scope)
    const commandScope = trustedWorkScope(filters, restored?.payloadSnapshot)
    let authoritative: BusinessConsoleWmsCountExecutionItem | undefined
    let pending: ReturnType<typeof acquirePendingBusinessIntent> | undefined
    completeCountExecutionPending.value = true
    completeCountExecutionError.value = undefined
    try {
      const result = await completePendingBusinessIntent(scope, async () => {
        const envelope = await executeLifecycleAction({
          readLatest: async () => {
            authoritative = await readCountExecution(countExecutionId, scope, commandScope)
            return authoritative
              ? {
                  domain: 'wms-count' as const,
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
                countedQuantity,
                ...commandScope,
                expectedVersion: requireVersion(authoritative),
              },
            )
            const stablePayload = requireFrozenCompletion<{ countedQuantity: number }>(
              pending.payloadSnapshot,
              '盘点完成',
            )
            options.onCommandAttempt?.()
            return completeBusinessConsoleWmsCountExecution({
              path: { countExecutionId },
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
              },
              body: {
                countedQuantity: stablePayload.countedQuantity,
                idempotencyKey: pending.idempotencyKey,
                scopeKind: stablePayload.scopeKind,
                scopeId: stablePayload.scopeId,
                expectedVersion: stablePayload.expectedVersion,
              },
              throwOnError: false,
            })
          },
        })
        if (!envelope || !pending) throw new Error('盘点完成未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'wms.count-execution.complete',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: countExecutionId,
          // 回读走的是范围受限的列表读面，不带范围必 403 → 成功也会被报成「尚未确认」（#1397）。
          readbackScope: commandScope,
        })
        return envelope
      })
      await refetchWithWmsListScope(filters, countExecutionsQuery)
      return result
    } catch (error) {
      completeCountExecutionError.value = error
      throw error
    } finally {
      completeCountExecutionPending.value = false
    }
  }

  return {
    filters,
    countExecutions: computed<BusinessConsoleWmsCountExecutionItem[]>(() =>
      listItems<BusinessConsoleWmsCountExecutionItem>(
        countExecutionsResponse.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
      ),
    ),
    countExecutionsError: countExecutionsQuery.error,
    countExecutionsPending: countExecutionsQuery.isLoading,
    countExecutionsTotal: computed(() =>
      listTotal(
        countExecutionsResponse.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
      ),
    ),
    refreshCountExecutions: () => refetchWithWmsListScope(filters, countExecutionsQuery),
    createCountExecution: (body: BusinessConsoleCreateWmsCountExecutionRequest) =>
      createMutation.mutateAsync({ body }),
    createCountExecutionPending: createMutation.isLoading,
    createCountExecutionError: createMutation.error,
    completeCountExecution,
    verifyCountExecution,
    completeCountExecutionPending,
    completeCountExecutionError,
  }
}
