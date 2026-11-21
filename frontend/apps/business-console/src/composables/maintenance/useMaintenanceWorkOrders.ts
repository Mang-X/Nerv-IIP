import {
  completeBusinessConsoleMaintenanceWorkOrderMutationOptions,
  confirmBusinessConsoleOperation,
  createBusinessConsoleMaintenanceWorkOrderV2MutationOptions,
  getBusinessConsolePrincipalWorkContextQueryOptions,
  transitionBusinessConsoleMaintenanceWorkOrderMutationOptions,
  getBusinessConsoleMaintenanceWorkOrderQueryOptions,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  type BusinessConsoleAuthorizedWorkScope,
  type BusinessConsoleCompleteMaintenanceWorkOrderRequest,
  type BusinessConsoleCreateMaintenanceWorkOrderV2Request,
  type BusinessConsoleMaintenanceWorkOrderItem,
  type BusinessConsoleMaintenanceWorkOrderListEnvelope,
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
import {
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../businessContextBinding'
import { executeLifecycleAction } from '../lifecycleAction'
import { assertEnvelopeSuccess } from '../serviceEnvelope'
import {
  defaultFilters,
  listItems,
  listTotal,
  type MaintenanceListFilters,
} from './maintenanceShared'

function requirePendingPayloadSnapshot<T extends object>(snapshot: unknown, operation: string): T {
  if (!snapshot || typeof snapshot !== 'object') {
    throw new Error(`${operation}缺少上次未完成提交的内容，请保留当前页面并人工核实。`)
  }
  return snapshot as T
}

type MaintenanceCreateIntent = Omit<
  BusinessConsoleCreateMaintenanceWorkOrderV2Request,
  'idempotencyKey'
> & {
  idempotencyKey?: string
}

type MaintenanceCompleteIntent = Omit<
  BusinessConsoleCompleteMaintenanceWorkOrderRequest,
  'idempotencyKey'
> & {
  idempotencyKey?: string
}

function newMaintenanceIntentKey(operation: string) {
  const cryptoApi = (globalThis as { crypto?: { randomUUID?: () => string } }).crypto
  const uniquePart =
    cryptoApi && typeof cryptoApi.randomUUID === 'function'
      ? cryptoApi.randomUUID()
      : `${Date.now()}-${Math.random().toString(36).slice(2)}`
  return `maintenance-${operation}-${uniquePart}`
}

function canonicalIntentValue(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(canonicalIntentValue)
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, item]) => [key, canonicalIntentValue(item)]),
    )
  }
  return value
}

function intentFingerprint(value: object) {
  return JSON.stringify(canonicalIntentValue(value))
}

export function useMaintenanceWorkOrders(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const auth = useAuthStore()
  const filters = defaultFilters(initialFilters)
  const workOrdersQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMaintenanceWorkOrdersQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )
  const workOrdersScopeReady = computed(() => hasBusinessContext(filters))
  const workOrdersResponse = useScopeBoundListResponse(
    () => workOrdersQuery.data.value,
    () => `${filters.organizationId.trim()}:${filters.environmentId.trim()}`,
    workOrdersScopeReady,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleMaintenanceWorkOrderV2MutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, workOrdersQuery)
    },
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleMaintenanceWorkOrderMutationOptions(),
  })
  const expectedRestoreMutation = useMutation(
    transitionBusinessConsoleMaintenanceWorkOrderMutationOptions(),
  )
  const expectedRestorePending = shallowRef(false)

  async function readExpectedRestoreScopes() {
    const contextOptions = getBusinessConsolePrincipalWorkContextQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        permissionCode: 'business.maintenance.work-orders.manage',
      },
    })
    const context = assertEnvelopeSuccess(
      await contextOptions.query({ signal: new AbortController().signal } as Parameters<
        typeof contextOptions.query
      >[0]),
      '无法读取维修作业范围。',
    )
    return context.data?.authorizedScopes ?? []
  }

  async function updateExpectedRestore(
    row: BusinessConsoleMaintenanceWorkOrderItem,
    expectedRestoreAtUtc: string | null,
    selection: BusinessConsoleAuthorizedWorkScope,
  ) {
    expectedRestorePending.value = true
    try {
      const intent = {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        action: 'updateExpectedRestore' as const,
        reason: expectedRestoreAtUtc ? '更新预计恢复时间' : '清除预计恢复时间',
        expectedVersion: row.version,
        scopeKind: selection.kind!,
        scopeId: selection.id!,
        expectedRestoreAtUtc,
      }
      const scope = {
        principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        operationType: 'maintenance.work-order.updateexpectedrestore',
        payloadFingerprint: `${row.workOrderId}:${intentFingerprint(intent)}`,
      }
      const pending = acquirePendingBusinessIntent(
        scope,
        () => newMaintenanceIntentKey('expected-restore'),
        intent,
      )
      const stableIntent = requirePendingPayloadSnapshot<typeof intent>(
        pending.payloadSnapshot,
        '预计恢复时间更新',
      )
      await completePendingBusinessIntent(scope, async () =>
        confirmBusinessConsoleOperation(
          await expectedRestoreMutation.mutateAsync({
            path: { workOrderId: row.workOrderId! },
            body: { ...stableIntent, idempotencyKey: pending.idempotencyKey },
          }),
          {
            expectedOperationType: scope.operationType,
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceId: row.workOrderId!,
          },
        ),
      )
      const detailOptions = getBusinessConsoleMaintenanceWorkOrderQueryOptions({
        path: { workOrderId: row.workOrderId! },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          scopeKind: selection.kind!,
          scopeId: selection.id!,
        },
      })
      const detail = assertEnvelopeSuccess(
        await detailOptions.query({ signal: new AbortController().signal } as Parameters<
          typeof detailOptions.query
        >[0]),
        '无法读回预计恢复时间，请刷新工单。',
      )
      if (!detail.data) throw new Error('无法读回预计恢复时间，请刷新工单。')
      await refetchWithBusinessContext(filters, workOrdersQuery)
      return detail.data
    } finally {
      expectedRestorePending.value = false
    }
  }

  const completeWorkOrderPending = shallowRef(false)
  const completeWorkOrderError = shallowRef<unknown>()

  async function completeWorkOrder(workOrderId: string, body: MaintenanceCompleteIntent) {
    const { idempotencyKey: suppliedKey, ...intent } = body
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      operationType: 'maintenance.work-order.complete',
      payloadFingerprint: `${workOrderId}:${intentFingerprint(intent)}`,
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(
      scope,
      () => suppliedKey ?? newMaintenanceIntentKey(`complete-${workOrderId}`),
      intent,
    )
    const isReplay = restored?.idempotencyKey === pending.idempotencyKey
    const stableIntent = requirePendingPayloadSnapshot<
      Omit<MaintenanceCompleteIntent, 'idempotencyKey'>
    >(pending.payloadSnapshot, '维修工单完工')
    completeWorkOrderPending.value = true
    completeWorkOrderError.value = undefined
    try {
      const result = await completePendingBusinessIntent(scope, async () => {
        const envelope = await executeLifecycleAction({
          readLatest: async () => {
            const query = getBusinessConsoleMaintenanceWorkOrderQueryOptions({
              path: { workOrderId },
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
              },
            })
            const response = await query.query({
              signal: new AbortController().signal,
            } as Parameters<typeof query.query>[0])
            const item = response?.success ? response.data : undefined
            return item
              ? {
                  domain: 'maintenance-work-order' as const,
                  action: 'complete' as const,
                  facts: { status: item.status, idempotentReplay: isReplay },
                }
              : undefined
          },
          command: async () => ({
            data: await completeMutation.mutateAsync({
              path: { workOrderId },
              body: { ...stableIntent, idempotencyKey: pending.idempotencyKey },
            }),
            response: { status: 200 },
          }),
        })
        if (!envelope) throw new Error('维修工单完工未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'maintenance.work-order.complete',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: workOrderId,
        })
        return envelope
      })
      await refetchWithBusinessContext(filters, workOrdersQuery)
      return result
    } catch (error) {
      completeWorkOrderError.value = error
      throw error
    } finally {
      completeWorkOrderPending.value = false
    }
  }

  async function createWithStableIntent(body: MaintenanceCreateIntent) {
    const { idempotencyKey: suppliedKey, ...intent } = body
    const fingerprint = intentFingerprint(intent)
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: intent.organizationId ?? '',
      environmentId: intent.environmentId ?? '',
      operationType: 'maintenance.work-order.create',
      payloadFingerprint: fingerprint,
    }
    const pending = acquirePendingBusinessIntent(
      scope,
      () => suppliedKey ?? newMaintenanceIntentKey('create'),
      intent,
    )
    const stableIntent = requirePendingPayloadSnapshot<typeof intent>(
      pending.payloadSnapshot,
      '创建维修工单',
    )
    return completePendingBusinessIntent(scope, async () => {
      const result = await confirmBusinessConsoleOperation(
        await createMutation.mutateAsync({
          body: { ...stableIntent, idempotencyKey: pending.idempotencyKey },
        }),
        {
          expectedOperationType: 'maintenance.work-order.create',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceIdSelector: (envelope) => envelope.data?.workOrderId,
        },
      )
      return result
    })
  }

  return {
    filters,
    workOrders: computed<BusinessConsoleMaintenanceWorkOrderItem[]>(() =>
      listItems<BusinessConsoleMaintenanceWorkOrderItem>(
        workOrdersResponse.value as BusinessConsoleMaintenanceWorkOrderListEnvelope | undefined,
      ),
    ),
    workOrdersError: workOrdersQuery.error,
    workOrdersPending: workOrdersQuery.isLoading,
    workOrdersTotal: computed(() =>
      listTotal(
        workOrdersResponse.value as BusinessConsoleMaintenanceWorkOrderListEnvelope | undefined,
      ),
    ),
    refreshWorkOrders: () => refetchWithBusinessContext(filters, workOrdersQuery),
    createWorkOrder: createWithStableIntent,
    createWorkOrderPending: createMutation.isLoading,
    createWorkOrderError: createMutation.error,
    readExpectedRestoreScopes,
    updateExpectedRestore,
    expectedRestorePending,
    completeWorkOrder,
    completeWorkOrderPending,
    completeWorkOrderError,
  }
}
