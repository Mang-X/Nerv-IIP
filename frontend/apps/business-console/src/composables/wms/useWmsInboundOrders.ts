import {
  completeBusinessConsoleWmsInboundOrder,
  confirmBusinessConsoleOperation,
  createBusinessConsoleWmsInboundOrderMutationOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsReceivingQualityGatesQueryOptions,
  listBusinessConsoleWmsSupplierReturnRequestsQueryOptions,
  listBusinessConsoleWmsReceivingQualityGates,
  listBusinessConsoleWmsSupplierReturnRequests,
  type BusinessConsoleCreateWmsInboundOrderRequest,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInboundOrderListEnvelope,
  type BusinessConsoleWmsReceivingQualityGateItem,
  type BusinessConsoleWmsReceivingQualityGateListEnvelope,
  type BusinessConsoleWmsSupplierReturnItem,
  type BusinessConsoleWmsSupplierReturnListEnvelope,
  type BusinessConsoleWmsInventoryContext,
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
import { refetchWithBusinessContext, withBusinessContextEnabled } from '../businessContextBinding'
import { executeLifecycleAction } from '../lifecycleAction'
import {
  baseQuery,
  createWmsIdempotencyKey,
  defaultFilters,
  exactSuccessfulItem,
  fetchAllWmsPages,
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

const RECEIVING_QUALITY_POLL_INTERVAL_MS = 10_000

const RECEIVING_QUALITY_PAGE_SIZE = 500

async function fetchAllReceivingQualityGates(query: {
  organizationId: string
  environmentId: string
  includeNotRequired: boolean
  scopeKind?: string
  scopeId?: string
}) {
  return fetchAllWmsPages(
    (skip) =>
      listBusinessConsoleWmsReceivingQualityGates({
        query: { ...query, skip, take: RECEIVING_QUALITY_PAGE_SIZE },
        throwOnError: true,
      }).then(({ data }) => data),
    '收货质检门禁',
  )
}

async function fetchAllSupplierReturns(query: { organizationId: string; environmentId: string }) {
  return fetchAllWmsPages(
    (skip) =>
      listBusinessConsoleWmsSupplierReturnRequests({
        query: { ...query, skip, take: RECEIVING_QUALITY_PAGE_SIZE },
        throwOnError: true,
      }).then(({ data }) => data),
    '供应商退供',
  )
}

export interface WmsInboundListFilters extends WmsListFilters {
  skuCode?: string
  uomCode?: string
  siteCode?: string
  locationCode?: string
  lotNo?: string
  serialNo?: string
  qualityStatus?: string
  ownerType?: string
  ownerId?: string
}

export function useWmsInboundOrders(initialFilters: Partial<WmsInboundListFilters> = {}) {
  const auth = useAuthStore()
  const filters = defaultFilters<WmsInboundListFilters>(initialFilters)
  const inboundOrdersQuery = useQuery(() => ({
    ...withWmsListScopeEnabled(
      listBusinessConsoleWmsInboundOrdersQueryOptions({
        query: {
          ...baseQuery(filters),
          ...optionalQuery('skuCode', filters.skuCode),
          ...optionalQuery('uomCode', filters.uomCode),
          ...optionalQuery('siteCode', filters.siteCode),
          ...optionalQuery('locationCode', filters.locationCode),
          ...optionalQuery('lotNo', filters.lotNo),
          ...optionalQuery('serialNo', filters.serialNo),
          ...optionalQuery('qualityStatus', filters.qualityStatus),
          ...optionalQuery('ownerType', filters.ownerType),
          ...optionalQuery('ownerId', filters.ownerId),
        },
      }),
      filters,
    ),
    autoRefetch: () => RECEIVING_QUALITY_POLL_INTERVAL_MS,
  }))
  const inboundOrdersScopeReady = computed(() => listScopeReady(filters))
  const inboundOrdersResponse = useScopeBoundListResponse(
    () => inboundOrdersQuery.data.value,
    () => listScopeKey(filters),
    inboundOrdersScopeReady,
  )
  // 收货行读面与入库单列表同样按作业范围授权（网关 ResolveScope），不带范围会被下游拒绝。
  const receivingQualityGatesQuery = useQuery(() => {
    const scopedQuery = {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      includeNotRequired: true,
      ...optionalQuery('scopeKind', filters.scopeKind),
      ...optionalQuery('scopeId', filters.scopeId),
    }
    return withWmsListScopeEnabled(
      {
        ...listBusinessConsoleWmsReceivingQualityGatesQueryOptions({
          query: { ...scopedQuery, skip: 0, take: RECEIVING_QUALITY_PAGE_SIZE },
        }),
        query: () => fetchAllReceivingQualityGates(scopedQuery),
        autoRefetch: () => RECEIVING_QUALITY_POLL_INTERVAL_MS,
      },
      filters,
    )
  })
  const supplierReturnsQuery = useQuery(() =>
    withBusinessContextEnabled(
      {
        ...listBusinessConsoleWmsSupplierReturnRequestsQueryOptions({
          query: {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
            skip: 0,
            take: RECEIVING_QUALITY_PAGE_SIZE,
          },
        }),
        query: () =>
          fetchAllSupplierReturns({
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
          }),
        autoRefetch: () => RECEIVING_QUALITY_POLL_INTERVAL_MS,
      },
      filters,
    ),
  )

  function refreshAll() {
    void refetchWithWmsListScope(filters, inboundOrdersQuery)
    void refetchWithWmsListScope(filters, receivingQualityGatesQuery)
    void refetchWithBusinessContext(filters, supplierReturnsQuery)
  }

  const completeInboundPending = shallowRef(false)
  const completeInboundError = shallowRef<unknown>()
  const createMutation = useMutation({
    ...createBusinessConsoleWmsInboundOrderMutationOptions(),
    onSuccess() {
      refreshAll()
    },
  })

  async function completeInboundOrder(
    inboundOrderId: string,
    idempotencyKey = createWmsIdempotencyKey(),
    options: WmsCompletionAttemptOptions = { attempt: 'initial' },
  ) {
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      operationType: 'wms.inbound-order.complete',
      payloadFingerprint: inboundOrderId,
    }
    const restored = peekPendingBusinessIntent(scope)
    const commandScope = trustedWorkScope(filters, restored?.payloadSnapshot)
    let authoritative: BusinessConsoleWmsInboundOrderItem | undefined
    let pending: ReturnType<typeof acquirePendingBusinessIntent> | undefined
    completeInboundPending.value = true
    completeInboundError.value = undefined
    try {
      const result = await completePendingBusinessIntent(scope, async () => {
        const envelope = await executeLifecycleAction({
          readLatest: async () => {
            const response = await listBusinessConsoleWmsInboundOrders({
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
                ...commandScope,
                inboundOrderId,
                skip: 0,
                take: 2,
              },
              throwOnError: false,
            })
            authoritative = exactSuccessfulItem<BusinessConsoleWmsInboundOrderItem>(
              response,
              (candidate) => candidate.inboundOrderId === inboundOrderId,
            )
            return authoritative
              ? {
                  domain: 'wms-inbound' as const,
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
                ...commandScope,
                expectedVersion: requireVersion(authoritative),
              },
            )
            const stablePayload = requireFrozenCompletion<Record<string, never>>(
              pending.payloadSnapshot,
              '入库完成',
            )
            options.onCommandAttempt?.()
            return completeBusinessConsoleWmsInboundOrder({
              path: { inboundOrderId },
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
              },
              body: {
                idempotencyKey: pending.idempotencyKey,
                scopeKind: stablePayload.scopeKind,
                scopeId: stablePayload.scopeId,
                expectedVersion: stablePayload.expectedVersion,
              },
              throwOnError: false,
            })
          },
        })
        if (!envelope || !pending) throw new Error('入库完成未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'wms.inbound-order.complete',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: inboundOrderId,
          // 回读走的是范围受限的列表读面，不带范围必 403 → 成功也会被报成「尚未确认」（#1397）。
          readbackScope: commandScope,
        })
        return envelope
      })
      refreshAll()
      return result
    } catch (error) {
      completeInboundError.value = error
      throw error
    } finally {
      completeInboundPending.value = false
    }
  }

  return {
    filters,
    inboundOrders: computed<BusinessConsoleWmsInboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsInboundOrderItem>(
        inboundOrdersResponse.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
      ),
    ),
    inventoryContext: computed<BusinessConsoleWmsInventoryContext | undefined>(() => {
      const envelope = inboundOrdersResponse.value as
        | BusinessConsoleWmsInboundOrderListEnvelope
        | undefined
      return envelope?.success ? (envelope.data?.inventoryContext ?? undefined) : undefined
    }),
    inboundOrdersError: inboundOrdersQuery.error,
    inboundOrdersPending: inboundOrdersQuery.isLoading,
    inboundOrdersTotal: computed(() =>
      listTotal(
        inboundOrdersResponse.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
      ),
    ),
    refreshInboundOrders: () => refetchWithWmsListScope(filters, inboundOrdersQuery),
    receivingQualityGates: computed<BusinessConsoleWmsReceivingQualityGateItem[]>(() =>
      listItems<BusinessConsoleWmsReceivingQualityGateItem>(
        receivingQualityGatesQuery.data.value as
          | BusinessConsoleWmsReceivingQualityGateListEnvelope
          | undefined,
      ),
    ),
    receivingQualityGatesPending: receivingQualityGatesQuery.isLoading,
    receivingQualityGatesError: receivingQualityGatesQuery.error,
    supplierReturns: computed<BusinessConsoleWmsSupplierReturnItem[]>(() =>
      listItems<BusinessConsoleWmsSupplierReturnItem>(
        supplierReturnsQuery.data.value as BusinessConsoleWmsSupplierReturnListEnvelope | undefined,
      ),
    ),
    supplierReturnsPending: supplierReturnsQuery.isLoading,
    supplierReturnsError: supplierReturnsQuery.error,
    refreshReceivingQuality: () =>
      Promise.all([
        refetchWithWmsListScope(filters, receivingQualityGatesQuery),
        refetchWithBusinessContext(filters, supplierReturnsQuery),
      ]),
    completeInbound: completeInboundOrder,
    completeInboundPending,
    completeInboundError,
    createInbound: (body: BusinessConsoleCreateWmsInboundOrderRequest) =>
      createMutation.mutateAsync({ body }),
    createInboundPending: createMutation.isLoading,
    createInboundError: createMutation.error,
  }
}
