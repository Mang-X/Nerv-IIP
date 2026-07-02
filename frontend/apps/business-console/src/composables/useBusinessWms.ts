import {
  completeBusinessConsoleWmsCountExecutionMutationOptions,
  completeBusinessConsoleWmsInboundOrderMutationOptions,
  completeBusinessConsoleWmsOutboundOrderMutationOptions,
  completeBusinessConsoleWmsWcsTaskMutationOptions,
  createBusinessConsoleWmsCountExecutionMutationOptions,
  createBusinessConsoleWmsInboundOrderMutationOptions,
  createBusinessConsoleWmsOutboundOrderMutationOptions,
  createBusinessConsoleWmsPickingTaskMutationOptions,
  createBusinessConsoleWmsPutawayTaskMutationOptions,
  dispatchBusinessConsoleWmsWcsTaskMutationOptions,
  failBusinessConsoleWmsWcsTaskMutationOptions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
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
  type BusinessConsoleWmsInventoryContext,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsOutboundOrderListEnvelope,
  type BusinessConsoleWmsWarehouseTaskItem,
  type BusinessConsoleWmsWarehouseTaskListEnvelope,
  type BusinessConsoleWmsWcsTaskItem,
  type BusinessConsoleWmsWcsTaskListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { bindBusinessContext, withBusinessContextEnabled } from './businessContextBinding'

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

export interface WmsWarehouseTaskListFilters extends WmsListFilters {
  locationCode?: string
}

function defaultFilters<T extends WmsListFilters>(initial: Partial<T> = {}): T {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  }) as T)
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
    withBusinessContextEnabled(listBusinessConsoleWmsInboundOrdersQueryOptions({
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
    }), filters),
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
    withBusinessContextEnabled(listBusinessConsoleWmsOutboundOrdersQueryOptions({
      query: baseQuery(filters),
    }), filters),
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
    withBusinessContextEnabled(listBusinessConsoleWmsWcsTasksQueryOptions({
      query: {
        ...baseQuery(filters),
        ...optionalQuery('externalTaskId', filters.externalTaskId),
        ...optionalQuery('warehouseTaskId', filters.warehouseTaskId),
        ...optionalQuery('failed', filters.failed),
      },
    }), filters),
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
    withBusinessContextEnabled(listBusinessConsoleWmsPutawayTasksQueryOptions({
      query: warehouseTaskQuery(filters),
    }), filters),
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsPutawayTaskMutationOptions(),
    onSuccess() {
      void putawayTasksQuery.refetch()
    },
  })

  return {
    filters,
    putawayTasks: computed<BusinessConsoleWmsWarehouseTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWarehouseTaskItem>(putawayTasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined),
    ),
    putawayTasksError: putawayTasksQuery.error,
    putawayTasksPending: putawayTasksQuery.isLoading,
    putawayTasksTotal: computed(() => listTotal(putawayTasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined)),
    refreshPutawayTasks: putawayTasksQuery.refetch,
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
    withBusinessContextEnabled(listBusinessConsoleWmsPickingTasksQueryOptions({
      query: warehouseTaskQuery(filters),
    }), filters),
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsPickingTaskMutationOptions(),
    onSuccess() {
      void pickingTasksQuery.refetch()
    },
  })

  return {
    filters,
    pickingTasks: computed<BusinessConsoleWmsWarehouseTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWarehouseTaskItem>(pickingTasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined),
    ),
    pickingTasksError: pickingTasksQuery.error,
    pickingTasksPending: pickingTasksQuery.isLoading,
    pickingTasksTotal: computed(() => listTotal(pickingTasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined)),
    refreshPickingTasks: pickingTasksQuery.refetch,
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
  const filters = defaultFilters<WmsWarehouseTaskListFilters>(initialFilters)
  const countExecutionsQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleWmsCountExecutionsQueryOptions({
      query: {
        ...baseQuery(filters),
        ...optionalQuery('locationCode', filters.locationCode),
      },
    }), filters),
  )

  const createMutation = useMutation({
    ...createBusinessConsoleWmsCountExecutionMutationOptions(),
    onSuccess() {
      void countExecutionsQuery.refetch()
    },
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsCountExecutionMutationOptions(),
    onSuccess() {
      void countExecutionsQuery.refetch()
    },
  })

  return {
    filters,
    countExecutions: computed<BusinessConsoleWmsCountExecutionItem[]>(() =>
      listItems<BusinessConsoleWmsCountExecutionItem>(countExecutionsQuery.data.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined),
    ),
    countExecutionsError: countExecutionsQuery.error,
    countExecutionsPending: countExecutionsQuery.isLoading,
    countExecutionsTotal: computed(() => listTotal(countExecutionsQuery.data.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined)),
    refreshCountExecutions: countExecutionsQuery.refetch,
    createCountExecution: (body: BusinessConsoleCreateWmsCountExecutionRequest) =>
      createMutation.mutateAsync({ body }),
    createCountExecutionPending: createMutation.isLoading,
    createCountExecutionError: createMutation.error,
    completeCountExecution: (countExecutionId: string, countedQuantity: number) =>
      completeMutation.mutateAsync({
        path: { countExecutionId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { countedQuantity, idempotencyKey: makeIdempotencyKey() },
      }),
    completeCountExecutionPending: completeMutation.isLoading,
    completeCountExecutionError: completeMutation.error,
  }
}
