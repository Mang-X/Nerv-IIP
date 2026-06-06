import {
  completeBusinessConsoleWmsInboundOrderMutationOptions,
  completeBusinessConsoleWmsOutboundOrderMutationOptions,
  completeBusinessConsoleWmsWcsTaskMutationOptions,
  dispatchBusinessConsoleWmsWcsTaskMutationOptions,
  failBusinessConsoleWmsWcsTaskMutationOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInventoryContext,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsWcsTaskItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

// WMS facade 当前的 list 端点只接受组织/环境（入库另含库存维度过滤），
// 不返回 skip/take/total，因此前端按完整列表渲染，不做假分页（见 nav-map 与后端跟进 issue）。
const ORG = 'org-001'
const ENV = 'env-dev'

function optionalQuery<T>(key: string, value: T | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function unwrap<T>(envelope: { success?: boolean; data?: T | null } | undefined): T | undefined {
  return envelope?.success ? envelope.data ?? undefined : undefined
}

// 写操作需要幂等键以防重复提交；浏览器原生 UUID，测试环境（jsdom）亦可用。
function makeIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  return `idem-${Date.now()}-${Math.round(Math.random() * 1e9)}`
}

export interface WmsInboundFilters {
  organizationId: string
  environmentId: string
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

export function useWmsInboundOrders() {
  const filters = reactive<WmsInboundFilters>({ organizationId: ORG, environmentId: ENV })

  const query = useQuery(() =>
    listBusinessConsoleWmsInboundOrdersQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
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

  const data = computed(() => unwrap(query.data.value))

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsInboundOrderMutationOptions(),
    onSuccess() {
      void query.refetch()
    },
  })

  return {
    filters,
    inboundOrders: computed<BusinessConsoleWmsInboundOrderItem[]>(() => data.value?.items ?? []),
    inventoryContext: computed<BusinessConsoleWmsInventoryContext | undefined>(
      () => data.value?.inventoryContext ?? undefined,
    ),
    inboundError: query.error,
    inboundPending: query.isLoading,
    refreshInbound: query.refetch,
    completeInbound: (inboundOrderId: string) =>
      completeMutation.mutateAsync({
        path: { inboundOrderId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { idempotencyKey: makeIdempotencyKey() },
      }),
    completeInboundPending: completeMutation.isLoading,
    completeInboundError: completeMutation.error,
  }
}

export function useWmsOutboundOrders() {
  const query = useQuery(() =>
    listBusinessConsoleWmsOutboundOrdersQueryOptions({
      query: { organizationId: ORG, environmentId: ENV },
    }),
  )

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void query.refetch()
    },
  })

  return {
    outboundOrders: computed<BusinessConsoleWmsOutboundOrderItem[]>(
      () => unwrap(query.data.value)?.items ?? [],
    ),
    outboundError: query.error,
    outboundPending: query.isLoading,
    refreshOutbound: query.refetch,
    completeOutbound: (outboundOrderId: string, payload: { packReviewNo: string; passed: boolean }) =>
      completeMutation.mutateAsync({
        path: { outboundOrderId },
        query: { organizationId: ORG, environmentId: ENV },
        body: { ...payload, idempotencyKey: makeIdempotencyKey() },
      }),
    completeOutboundPending: completeMutation.isLoading,
    completeOutboundError: completeMutation.error,
  }
}

export interface WmsWcsFilters {
  organizationId: string
  environmentId: string
  externalTaskId?: string
  warehouseTaskId?: string
}

export function useWmsWcsTasks() {
  const filters = reactive<WmsWcsFilters>({ organizationId: ORG, environmentId: ENV })

  const query = useQuery(() =>
    listBusinessConsoleWmsWcsTasksQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('externalTaskId', filters.externalTaskId),
        ...optionalQuery('warehouseTaskId', filters.warehouseTaskId),
      },
    }),
  )

  function withQuery() {
    return { organizationId: filters.organizationId, environmentId: filters.environmentId }
  }

  const dispatchMutation = useMutation({
    ...dispatchBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void query.refetch()
    },
  })
  const failMutation = useMutation({
    ...failBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void query.refetch()
    },
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsWcsTaskMutationOptions(),
    onSuccess() {
      void query.refetch()
    },
  })

  return {
    filters,
    wcsTasks: computed<BusinessConsoleWmsWcsTaskItem[]>(() => unwrap(query.data.value)?.items ?? []),
    wcsError: query.error,
    wcsPending: query.isLoading,
    refreshWcs: query.refetch,
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
