import {
  completeBusinessConsoleWmsInboundOrderMutationOptions,
  completeBusinessConsoleWmsOutboundOrderMutationOptions,
  completeBusinessConsoleWmsWcsTaskMutationOptions,
  createBusinessConsoleWmsInboundOrderMutationOptions,
  createBusinessConsoleWmsOutboundOrderMutationOptions,
  dispatchBusinessConsoleWmsWcsTaskMutationOptions,
  failBusinessConsoleWmsWcsTaskMutationOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
  type BusinessConsoleCreateWmsInboundOrderRequest,
  type BusinessConsoleCreateWmsOutboundOrderRequest,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInboundOrderListEnvelope,
  type BusinessConsoleWmsInventoryContext,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsOutboundOrderListEnvelope,
  type BusinessConsoleWmsWcsTaskItem,
  type BusinessConsoleWmsWcsTaskListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

export interface WmsListFilters {
  organizationId: string
  environmentId: string
  skip: number
  take: number
  status?: string
  keyword?: string
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

export interface WmsWcsTaskListFilters extends WmsListFilters {
  externalTaskId?: string
  warehouseTaskId?: string
  failed?: boolean
}

function defaultFilters<T extends WmsListFilters>(initial: Partial<T> = {}): T {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  }) as T
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

function listItems<TItem>(envelope: { success?: boolean, data?: { items?: TItem[] } | null } | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function listTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

// 写操作需要幂等键以防重复提交；浏览器原生 UUID，测试环境（jsdom）亦可用。
function makeIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  return `idem-${Date.now()}-${Math.round(Math.random() * 1e9)}`
}

export function useWmsInboundOrders(initialFilters: Partial<WmsInboundListFilters> = {}) {
  const filters = defaultFilters<WmsInboundListFilters>(initialFilters)
  const inboundOrdersQuery = useQuery(() =>
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
  )

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsInboundOrderMutationOptions(),
    onSuccess() {
      void inboundOrdersQuery.refetch()
    },
  })
  const createMutation = useMutation({
    ...createBusinessConsoleWmsInboundOrderMutationOptions(),
    onSuccess() {
      void inboundOrdersQuery.refetch()
    },
  })

  return {
    filters,
    inboundOrders: computed<BusinessConsoleWmsInboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsInboundOrderItem>(inboundOrdersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined),
    ),
    inventoryContext: computed<BusinessConsoleWmsInventoryContext | undefined>(() => {
      const envelope = inboundOrdersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined
      return envelope?.success ? envelope.data?.inventoryContext ?? undefined : undefined
    }),
    inboundOrdersError: inboundOrdersQuery.error,
    inboundOrdersPending: inboundOrdersQuery.isLoading,
    inboundOrdersTotal: computed(() => listTotal(inboundOrdersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined)),
    refreshInboundOrders: inboundOrdersQuery.refetch,
    completeInbound: (inboundOrderId: string) =>
      completeMutation.mutateAsync({
        path: { inboundOrderId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { idempotencyKey: makeIdempotencyKey() },
      }),
    completeInboundPending: completeMutation.isLoading,
    completeInboundError: completeMutation.error,
    createInbound: (body: BusinessConsoleCreateWmsInboundOrderRequest) =>
      createMutation.mutateAsync({ body }),
    createInboundPending: createMutation.isLoading,
    createInboundError: createMutation.error,
  }
}

export function useWmsOutboundOrders(initialFilters: Partial<WmsListFilters> = {}) {
  const filters = defaultFilters<WmsListFilters>(initialFilters)
  const outboundOrdersQuery = useQuery(() =>
    listBusinessConsoleWmsOutboundOrdersQueryOptions({
      query: baseQuery(filters),
    }),
  )

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void outboundOrdersQuery.refetch()
    },
  })
  const createMutation = useMutation({
    ...createBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void outboundOrdersQuery.refetch()
    },
  })

  return {
    filters,
    outboundOrders: computed<BusinessConsoleWmsOutboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsOutboundOrderItem>(outboundOrdersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined),
    ),
    outboundOrdersError: outboundOrdersQuery.error,
    outboundOrdersPending: outboundOrdersQuery.isLoading,
    outboundOrdersTotal: computed(() => listTotal(outboundOrdersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined)),
    refreshOutboundOrders: outboundOrdersQuery.refetch,
    completeOutbound: (outboundOrderId: string, payload: { packReviewNo: string; passed: boolean }) =>
      completeMutation.mutateAsync({
        path: { outboundOrderId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { ...payload, idempotencyKey: makeIdempotencyKey() },
      }),
    completeOutboundPending: completeMutation.isLoading,
    completeOutboundError: completeMutation.error,
    createOutbound: (body: BusinessConsoleCreateWmsOutboundOrderRequest) =>
      createMutation.mutateAsync({ body }),
    createOutboundPending: createMutation.isLoading,
    createOutboundError: createMutation.error,
  }
}

export function useWmsWcsTasks(initialFilters: Partial<WmsWcsTaskListFilters> = {}) {
  const filters = defaultFilters<WmsWcsTaskListFilters>(initialFilters)
  const wcsTasksQuery = useQuery(() =>
    listBusinessConsoleWmsWcsTasksQueryOptions({
      query: {
        ...baseQuery(filters),
        ...optionalQuery('externalTaskId', filters.externalTaskId),
        ...optionalQuery('warehouseTaskId', filters.warehouseTaskId),
        ...optionalQuery('failed', filters.failed),
      },
    }),
  )

  function withQuery() {
    return { organizationId: filters.organizationId, environmentId: filters.environmentId }
  }
  const dispatchMutation = useMutation({
    ...dispatchBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void wcsTasksQuery.refetch()
    },
  })
  const failMutation = useMutation({
    ...failBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void wcsTasksQuery.refetch()
    },
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void wcsTasksQuery.refetch()
    },
  })

  return {
    filters,
    wcsTasks: computed<BusinessConsoleWmsWcsTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWcsTaskItem>(wcsTasksQuery.data.value as BusinessConsoleWmsWcsTaskListEnvelope | undefined),
    ),
    wcsTasksError: wcsTasksQuery.error,
    wcsTasksPending: wcsTasksQuery.isLoading,
    wcsTasksTotal: computed(() => listTotal(wcsTasksQuery.data.value as BusinessConsoleWmsWcsTaskListEnvelope | undefined)),
    refreshWcsTasks: wcsTasksQuery.refetch,
    dispatchWcs: (
      warehouseTaskId: string,
      payload: { adapterType: string; externalTaskId: string; payloadJson: string },
    ) => dispatchMutation.mutateAsync({ path: { warehouseTaskId }, query: withQuery(), body: payload }),
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
