import {
  completeBusinessConsoleWmsCountExecution,
  completeBusinessConsoleWmsInboundOrder,
  completeBusinessConsoleWmsOutboundOrder,
  confirmBusinessConsoleOperation,
  completeBusinessConsoleWmsWcsTaskMutationOptions,
  createBusinessConsoleWmsCountExecutionMutationOptions,
  createBusinessConsoleWmsInboundOrderMutationOptions,
  createBusinessConsoleWmsOutboundOrderMutationOptions,
  createBusinessConsoleWmsPickingTaskMutationOptions,
  createBusinessConsoleWmsPutawayTaskMutationOptions,
  dispatchBusinessConsoleWmsWcsTaskMutationOptions,
  failBusinessConsoleWmsWcsTaskMutationOptions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsCountExecutions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsReceivingQualityGatesQueryOptions,
  listBusinessConsoleWmsSupplierReturnRequestsQueryOptions,
  listBusinessConsoleWmsReceivingQualityGates,
  listBusinessConsoleWmsSupplierReturnRequests,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrders,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
  type BusinessConsoleCreateWmsCountExecutionRequest,
  type BusinessConsoleCreateWmsInboundOrderRequest,
  type BusinessConsoleCreateWmsOutboundOrderRequest,
  type BusinessConsoleCreateWmsPickingTaskRequest,
  type BusinessConsoleCreateWmsPutawayTaskRequest,
  type BusinessConsoleWmsCountExecutionItem,
  type BusinessConsoleWmsCountExecutionListEnvelope,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInboundOrderListEnvelope,
  type BusinessConsoleWmsReceivingQualityGateItem,
  type BusinessConsoleWmsReceivingQualityGateListEnvelope,
  type BusinessConsoleWmsSupplierReturnItem,
  type BusinessConsoleWmsSupplierReturnListEnvelope,
  type BusinessConsoleWmsInventoryContext,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsOutboundOrderListEnvelope,
  type BusinessConsoleWmsWarehouseTaskItem,
  type BusinessConsoleWmsWarehouseTaskListEnvelope,
  type BusinessConsoleWmsWcsTaskItem,
  type BusinessConsoleWmsWcsTaskListEnvelope,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'
import {
  bindBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from './businessContextBinding'
import { executeLifecycleAction } from './lifecycleAction'
import { useListFreshness } from './useListFreshness'

const DEFAULT_TAKE = 100
const RECEIVING_QUALITY_POLL_INTERVAL_MS = 10_000
const RECEIVING_QUALITY_PAGE_SIZE = 500

function requirePendingPayloadSnapshot<T extends object>(snapshot: unknown, operation: string): T {
  if (!snapshot || typeof snapshot !== 'object') {
    throw new Error(`${operation}缺少冻结的待处理载荷，请保留当前页面并人工核实。`)
  }
  return snapshot as T
}

type WmsListEnvelope<TItem> = {
  success?: boolean
  data?: { items?: TItem[]; total?: number } | null
}

async function fetchAllReceivingQualityGates(query: {
  organizationId: string
  environmentId: string
  includeNotRequired: boolean
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

async function fetchAllWmsPages<TItem>(
  fetchPage: (skip: number) => Promise<WmsListEnvelope<TItem>>,
  label: string,
): Promise<WmsListEnvelope<TItem>> {
  const items: TItem[] = []
  let total = 0
  let skip = 0
  let firstPage: WmsListEnvelope<TItem> | undefined

  while (true) {
    const page = await fetchPage(skip)
    firstPage ??= page
    if (!page.success) throw new Error(`${label}读取失败，请刷新后重试。`)

    const pageItems = page.data?.items ?? []
    total = page.data?.total ?? 0
    items.push(...pageItems)
    if (items.length >= total) break
    if (pageItems.length === 0) throw new Error(`${label}读取不完整，请刷新后重试。`)
    skip += pageItems.length
  }

  return {
    ...firstPage,
    success: true,
    data: { ...firstPage?.data, items, total },
  }
}

export interface WmsListFilters {
  organizationId: string
  environmentId: string
  skip: number
  take: number
  status?: string
  keyword?: string
}

export type WmsCompletionAttemptOptions = Readonly<{
  attempt: 'initial' | 'retry'
  onCommandAttempt?: () => void
}>

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

export interface WmsWcsTaskListFilters extends WmsListFilters {
  externalTaskId?: string
  warehouseTaskId?: string
  failed?: boolean
}

export interface WmsWarehouseTaskListFilters extends WmsListFilters {
  locationCode?: string
}

function defaultFilters<T extends WmsListFilters>(initial: Partial<T> = {}): T {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
      ...initial,
    }) as T,
  )
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function baseQuery(filters: WmsListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    skip: filters.skip,
    take: filters.take,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
  }
}

function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function listTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

// 写操作需要幂等键以防重复提交；浏览器原生 UUID，测试环境（jsdom）亦可用。
export function createWmsIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  return `idem-${Date.now()}-${Math.round(Math.random() * 1e9)}`
}

function requireSuccessfulList<TItem>(
  result: Readonly<{
    data?: { success?: boolean; data?: { items?: TItem[] } | null }
    error?: unknown
  }>,
): TItem[] {
  if (result.error !== undefined) throw result.error
  if (!result.data?.success) throw result.data ?? new Error('读取最新状态失败')
  return result.data.data?.items ?? []
}

export function useWmsInboundOrders(initialFilters: Partial<WmsInboundListFilters> = {}) {
  const auth = useAuthStore()
  const filters = defaultFilters<WmsInboundListFilters>(initialFilters)
  const inboundOrdersQuery = useQuery(() => ({
    ...withBusinessContextEnabled(
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
  const inboundOrdersLastUpdatedAt = useListFreshness(
    () => inboundOrdersQuery.data.value,
    () => filters.organizationId.trim().length > 0 && filters.environmentId.trim().length > 0,
  )
  const receivingQualityGatesQuery = useQuery(() =>
    withBusinessContextEnabled(
      {
        ...listBusinessConsoleWmsReceivingQualityGatesQueryOptions({
          query: {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
            skip: 0,
            take: RECEIVING_QUALITY_PAGE_SIZE,
            includeNotRequired: true,
          },
        }),
        query: () =>
          fetchAllReceivingQualityGates({
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
            includeNotRequired: true,
          }),
        autoRefetch: () => RECEIVING_QUALITY_POLL_INTERVAL_MS,
      },
      filters,
    ),
  )
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
    void refetchWithBusinessContext(filters, inboundOrdersQuery)
    void refetchWithBusinessContext(filters, receivingQualityGatesQuery)
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
    const pending = acquirePendingBusinessIntent(scope, () => idempotencyKey, {})
    requirePendingPayloadSnapshot<Record<string, never>>(pending.payloadSnapshot, '入库完成')
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
                inboundOrderId,
                skip: 0,
                take: 1,
              },
              throwOnError: false,
            })
            const item = requireSuccessfulList<BusinessConsoleWmsInboundOrderItem>(response).find(
              (candidate) => candidate.inboundOrderId === inboundOrderId,
            )
            return item
              ? {
                  domain: 'wms-inbound' as const,
                  action: 'complete' as const,
                  facts: {
                    status: item.status,
                    idempotentReplay: restored?.idempotencyKey === pending.idempotencyKey,
                  },
                }
              : undefined
          },
          command: () => {
            options.onCommandAttempt?.()
            return completeBusinessConsoleWmsInboundOrder({
              path: { inboundOrderId },
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
              },
              body: { idempotencyKey: pending.idempotencyKey },
              throwOnError: false,
            })
          },
        })
        if (!envelope) throw new Error('入库完成未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'wms.inbound-order.complete',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: inboundOrderId,
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
        inboundOrdersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
      ),
    ),
    inventoryContext: computed<BusinessConsoleWmsInventoryContext | undefined>(() => {
      const envelope = inboundOrdersQuery.data.value as
        | BusinessConsoleWmsInboundOrderListEnvelope
        | undefined
      return envelope?.success ? (envelope.data?.inventoryContext ?? undefined) : undefined
    }),
    inboundOrdersError: inboundOrdersQuery.error,
    inboundOrdersPending: inboundOrdersQuery.isLoading,
    inboundOrdersTotal: computed(() =>
      listTotal(
        inboundOrdersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
      ),
    ),
    inboundOrdersLastUpdatedAt,
    refreshInboundOrders: () => refetchWithBusinessContext(filters, inboundOrdersQuery),
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
        refetchWithBusinessContext(filters, receivingQualityGatesQuery),
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

export function useWmsOutboundOrders(initialFilters: Partial<WmsListFilters> = {}) {
  const auth = useAuthStore()
  const filters = defaultFilters<WmsListFilters>(initialFilters)
  const outboundOrdersQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleWmsOutboundOrdersQueryOptions({
        query: baseQuery(filters),
      }),
      filters,
    ),
  )
  const outboundOrdersLastUpdatedAt = useListFreshness(
    () => outboundOrdersQuery.data.value,
    () => filters.organizationId.trim().length > 0 && filters.environmentId.trim().length > 0,
  )

  const completeOutboundPending = shallowRef(false)
  const completeOutboundError = shallowRef<unknown>()
  const createMutation = useMutation({
    ...createBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, outboundOrdersQuery)
    },
  })

  async function completeOutboundOrder(
    outboundOrderId: string,
    payload: { packReviewNo: string; passed: boolean },
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
    const pending = acquirePendingBusinessIntent(scope, () => idempotencyKey, payload)
    const stablePayload = requirePendingPayloadSnapshot<typeof payload>(
      pending.payloadSnapshot,
      '出库完成',
    )
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
                outboundOrderId,
                skip: 0,
                take: 1,
              },
              throwOnError: false,
            })
            const item = requireSuccessfulList<BusinessConsoleWmsOutboundOrderItem>(response).find(
              (candidate) => candidate.outboundOrderId === outboundOrderId,
            )
            return item
              ? {
                  domain: 'wms-outbound' as const,
                  action: 'complete' as const,
                  facts: {
                    status: item.status,
                    idempotentReplay: restored?.idempotencyKey === pending.idempotencyKey,
                  },
                }
              : undefined
          },
          command: () => {
            options.onCommandAttempt?.()
            return completeBusinessConsoleWmsOutboundOrder({
              path: { outboundOrderId },
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
              },
              body: { ...stablePayload, idempotencyKey: pending.idempotencyKey },
              throwOnError: false,
            })
          },
        })
        if (!envelope) throw new Error('出库完成未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'wms.outbound-order.complete',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: outboundOrderId,
        })
        return envelope
      })
      await refetchWithBusinessContext(filters, outboundOrdersQuery)
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
        outboundOrdersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
      ),
    ),
    outboundOrdersError: outboundOrdersQuery.error,
    outboundOrdersPending: outboundOrdersQuery.isLoading,
    outboundOrdersTotal: computed(() =>
      listTotal(
        outboundOrdersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
      ),
    ),
    outboundOrdersLastUpdatedAt,
    refreshOutboundOrders: () => refetchWithBusinessContext(filters, outboundOrdersQuery),
    completeOutbound: completeOutboundOrder,
    completeOutboundPending,
    completeOutboundError,
    createOutbound: (body: BusinessConsoleCreateWmsOutboundOrderRequest) =>
      createMutation.mutateAsync({ body }),
    createOutboundPending: createMutation.isLoading,
    createOutboundError: createMutation.error,
  }
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
      payload: { adapterType: string; externalTaskId: string; payloadJson: string },
    ) =>
      dispatchMutation.mutateAsync({
        path: { warehouseTaskId },
        query: withQuery(),
        body: payload,
      }),
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
  }
}

// 上架任务（完工入库 → 上架增量）。后端在收货入库单下挂上架任务；创建需绑定 inboundOrderId。
export function useWmsPutawayTasks(initialFilters: Partial<WmsWarehouseTaskListFilters> = {}) {
  const filters = defaultFilters<WmsWarehouseTaskListFilters>(initialFilters)
  const putawayTasksQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleWmsPutawayTasksQueryOptions({
        query: warehouseTaskQuery(filters),
      }),
      filters,
    ),
  )
  const putawayTasksLastUpdatedAt = useListFreshness(
    () => putawayTasksQuery.data.value,
    () => filters.organizationId.trim().length > 0 && filters.environmentId.trim().length > 0,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsPutawayTaskMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, putawayTasksQuery)
    },
  })

  return {
    filters,
    putawayTasks: computed<BusinessConsoleWmsWarehouseTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWarehouseTaskItem>(
        putawayTasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    putawayTasksError: putawayTasksQuery.error,
    putawayTasksPending: putawayTasksQuery.isLoading,
    putawayTasksTotal: computed(() =>
      listTotal(
        putawayTasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    putawayTasksLastUpdatedAt,
    refreshPutawayTasks: () => refetchWithBusinessContext(filters, putawayTasksQuery),
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
    withBusinessContextEnabled(
      listBusinessConsoleWmsPickingTasksQueryOptions({
        query: warehouseTaskQuery(filters),
      }),
      filters,
    ),
  )
  const pickingTasksLastUpdatedAt = useListFreshness(
    () => pickingTasksQuery.data.value,
    () => filters.organizationId.trim().length > 0 && filters.environmentId.trim().length > 0,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsPickingTaskMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, pickingTasksQuery)
    },
  })

  return {
    filters,
    pickingTasks: computed<BusinessConsoleWmsWarehouseTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWarehouseTaskItem>(
        pickingTasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    pickingTasksError: pickingTasksQuery.error,
    pickingTasksPending: pickingTasksQuery.isLoading,
    pickingTasksTotal: computed(() =>
      listTotal(
        pickingTasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    pickingTasksLastUpdatedAt,
    refreshPickingTasks: () => refetchWithBusinessContext(filters, pickingTasksQuery),
    createPicking: (outboundOrderId: string, body: BusinessConsoleCreateWmsPickingTaskRequest) =>
      createMutation.mutateAsync({
        path: { outboundOrderId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    createPickingPending: createMutation.isLoading,
    createPickingError: createMutation.error,
  }
}

// 盘点执行（库位 × SKU 的账面 vs 实盘）。完成盘点按差额触发库存调整移动。
export function useWmsCountExecutions(initialFilters: Partial<WmsWarehouseTaskListFilters> = {}) {
  const auth = useAuthStore()
  const filters = defaultFilters<WmsWarehouseTaskListFilters>(initialFilters)
  const countExecutionsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleWmsCountExecutionsQueryOptions({
        query: {
          ...baseQuery(filters),
          ...optionalQuery('locationCode', filters.locationCode),
        },
      }),
      filters,
    ),
  )
  const countExecutionsLastUpdatedAt = useListFreshness(
    () => countExecutionsQuery.data.value,
    () => filters.organizationId.trim().length > 0 && filters.environmentId.trim().length > 0,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsCountExecutionMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, countExecutionsQuery)
    },
  })
  const completeCountExecutionPending = shallowRef(false)
  const completeCountExecutionError = shallowRef<unknown>()

  async function completeCountExecution(
    countExecutionId: string,
    countedQuantity: number,
    idempotencyKey = createWmsIdempotencyKey(),
    options: WmsCompletionAttemptOptions = { attempt: 'initial' },
  ) {
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      operationType: 'wms.count-execution.complete',
      payloadFingerprint: `${countExecutionId}:${countedQuantity}`,
    }
    const restored = peekPendingBusinessIntent(scope)
    const pending = acquirePendingBusinessIntent(scope, () => idempotencyKey, { countedQuantity })
    const stablePayload = requirePendingPayloadSnapshot<{ countedQuantity: number }>(
      pending.payloadSnapshot,
      '盘点完成',
    )
    completeCountExecutionPending.value = true
    completeCountExecutionError.value = undefined
    try {
      const result = await completePendingBusinessIntent(scope, async () => {
        const envelope = await executeLifecycleAction({
          readLatest: async () => {
            const response = await listBusinessConsoleWmsCountExecutions({
              query: {
                organizationId: filters.organizationId,
                environmentId: filters.environmentId,
                countExecutionId,
                skip: 0,
                take: 1,
              },
              throwOnError: false,
            })
            const item = requireSuccessfulList<BusinessConsoleWmsCountExecutionItem>(response).find(
              (candidate) => candidate.countExecutionId === countExecutionId,
            )
            return item
              ? {
                  domain: 'wms-count' as const,
                  action: 'complete' as const,
                  facts: {
                    status: item.status,
                    idempotentReplay: restored?.idempotencyKey === pending.idempotencyKey,
                  },
                }
              : undefined
          },
          command: () => {
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
              },
              throwOnError: false,
            })
          },
        })
        if (!envelope) throw new Error('盘点完成未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'wms.count-execution.complete',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceId: countExecutionId,
        })
        return envelope
      })
      await refetchWithBusinessContext(filters, countExecutionsQuery)
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
        countExecutionsQuery.data.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
      ),
    ),
    countExecutionsError: countExecutionsQuery.error,
    countExecutionsPending: countExecutionsQuery.isLoading,
    countExecutionsTotal: computed(() =>
      listTotal(
        countExecutionsQuery.data.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
      ),
    ),
    countExecutionsLastUpdatedAt,
    refreshCountExecutions: () => refetchWithBusinessContext(filters, countExecutionsQuery),
    createCountExecution: (body: BusinessConsoleCreateWmsCountExecutionRequest) =>
      createMutation.mutateAsync({ body }),
    createCountExecutionPending: createMutation.isLoading,
    createCountExecutionError: createMutation.error,
    completeCountExecution,
    completeCountExecutionPending,
    completeCountExecutionError,
  }
}
