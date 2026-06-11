import {
  completeBusinessConsoleWmsCountExecutionMutationOptions,
  completeBusinessConsoleWmsInboundOrderMutationOptions,
  completeBusinessConsoleWmsOutboundOrderMutationOptions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  type BusinessConsoleCompleteWmsCountExecutionRequest,
  type BusinessConsoleCompleteWmsInboundOrderRequest,
  type BusinessConsoleCompleteWmsOutboundOrderRequest,
  type BusinessConsoleWmsCountExecutionItem,
  type BusinessConsoleWmsCountExecutionListEnvelope,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInboundOrderListEnvelope,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsOutboundOrderListEnvelope,
  type BusinessConsoleWmsWarehouseTaskItem,
  type BusinessConsoleWmsWarehouseTaskListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

import { useAuthStore } from '@/stores/auth'

const DEFAULT_TAKE = 100

export interface WmsScopeFilters {
  skip: number
  take: number
  status?: string
  keyword?: string
}

export interface WmsTaskFilters extends WmsScopeFilters {
  locationCode?: string
}

// outbound/count 写入参数：调用方传业务字段 + idempotencyKey（页面在用户发起操作时生成一次，
// 重试复用同一键以防丢响应导致重复入库；新操作才换新键）。org/env 不在 body，由本封装从主体注入。
export type CompleteOutboundInput = BusinessConsoleCompleteWmsOutboundOrderRequest
export type CompleteCountInput = BusinessConsoleCompleteWmsCountExecutionRequest

function defaultFilters<T extends WmsScopeFilters>(initial: Partial<T> = {}): T {
  return reactive({ skip: 0, take: DEFAULT_TAKE, ...initial }) as T
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function listItems<TItem>(envelope: { success?: boolean, data?: { items?: TItem[] } | null } | undefined) {
  return envelope?.success ? envelope.data?.items ?? [] : []
}

function listTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  return envelope?.success ? envelope.data?.total ?? 0 : 0
}

// DRY scope binding：org/env 一律取登录主体；空 scope → 不发请求（enabled:false）。
// 五个域共用，避免 5× 复制 org/env 接线。
function useWmsScope() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const hasScope = computed(() => Boolean(organizationId.value && environmentId.value))
  const scopeQuery = () => ({
    organizationId: organizationId.value,
    environmentId: environmentId.value,
  })
  const scopeQueryWithPaging = (filters: WmsScopeFilters) => ({
    ...scopeQuery(),
    skip: filters.skip,
    take: filters.take,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
  })
  return { organizationId, environmentId, hasScope, scopeQuery, scopeQueryWithPaging }
}

export function useWmsInbound(initialFilters: Partial<WmsScopeFilters> = {}) {
  const scope = useWmsScope()
  const filters = defaultFilters<WmsScopeFilters>(initialFilters)

  const ordersQuery = useQuery(() => ({
    ...listBusinessConsoleWmsInboundOrdersQueryOptions({ query: scope.scopeQueryWithPaging(filters) }),
    enabled: scope.hasScope.value,
  }))

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsInboundOrderMutationOptions(),
    onSuccess() {
      void ordersQuery.refetch()
    },
  })

  return {
    filters,
    orders: computed<BusinessConsoleWmsInboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsInboundOrderItem>(ordersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined),
    ),
    total: computed(() => listTotal(ordersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined)),
    pending: ordersQuery.isLoading,
    error: ordersQuery.error,
    refresh: ordersQuery.refetch,
    completeInbound: (inboundOrderId: string, idempotencyKey: string) => {
      // 幂等键由页面在用户发起操作时生成一次并跨重试复用（防丢响应重复入库）；
      // org/env 取登录主体注入 query，调用方无法影响。
      const body = { idempotencyKey } satisfies BusinessConsoleCompleteWmsInboundOrderRequest
      return completeMutation.mutateAsync({ path: { inboundOrderId }, query: scope.scopeQuery(), body })
    },
    completePending: completeMutation.isLoading,
  }
}

export function useWmsOutbound(initialFilters: Partial<WmsScopeFilters> = {}) {
  const scope = useWmsScope()
  const filters = defaultFilters<WmsScopeFilters>(initialFilters)

  const ordersQuery = useQuery(() => ({
    ...listBusinessConsoleWmsOutboundOrdersQueryOptions({ query: scope.scopeQueryWithPaging(filters) }),
    enabled: scope.hasScope.value,
  }))

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void ordersQuery.refetch()
    },
  })

  return {
    filters,
    orders: computed<BusinessConsoleWmsOutboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsOutboundOrderItem>(ordersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined),
    ),
    total: computed(() => listTotal(ordersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined)),
    pending: ordersQuery.isLoading,
    error: ordersQuery.error,
    refresh: ordersQuery.refetch,
    completeOutbound: (outboundOrderId: string, input: CompleteOutboundInput) => {
      // 页面提供 packReviewNo/passed/idempotencyKey（幂等键跨重试复用）；
      // org/env 不取自 input，恒由登录主体注入 query，敌意 org/env 永远落空。
      const body = { ...input } satisfies BusinessConsoleCompleteWmsOutboundOrderRequest
      return completeMutation.mutateAsync({ path: { outboundOrderId }, query: scope.scopeQuery(), body })
    },
    completePending: completeMutation.isLoading,
  }
}

function useWmsWarehouseTasks(
  queryOptionsFactory:
    | typeof listBusinessConsoleWmsPickingTasksQueryOptions
    | typeof listBusinessConsoleWmsPutawayTasksQueryOptions,
  initialFilters: Partial<WmsTaskFilters> = {},
) {
  const scope = useWmsScope()
  const filters = defaultFilters<WmsTaskFilters>(initialFilters)

  const tasksQuery = useQuery(() => ({
    // 注意：不传 operatorUserId——P1 未实装，传非空会返回空集。
    ...queryOptionsFactory({
      query: {
        ...scope.scopeQueryWithPaging(filters),
        ...optionalQuery('locationCode', filters.locationCode),
      },
    }),
    enabled: scope.hasScope.value,
  }))

  return {
    filters,
    tasks: computed<BusinessConsoleWmsWarehouseTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWarehouseTaskItem>(tasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined),
    ),
    total: computed(() => listTotal(tasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined)),
    pending: tasksQuery.isLoading,
    error: tasksQuery.error,
    refresh: tasksQuery.refetch,
  }
}

export function useWmsPicking(initialFilters: Partial<WmsTaskFilters> = {}) {
  return useWmsWarehouseTasks(listBusinessConsoleWmsPickingTasksQueryOptions, initialFilters)
}

export function useWmsPutaway(initialFilters: Partial<WmsTaskFilters> = {}) {
  return useWmsWarehouseTasks(listBusinessConsoleWmsPutawayTasksQueryOptions, initialFilters)
}

export function useWmsCount(initialFilters: Partial<WmsTaskFilters> = {}) {
  const scope = useWmsScope()
  const filters = defaultFilters<WmsTaskFilters>(initialFilters)

  const executionsQuery = useQuery(() => ({
    ...listBusinessConsoleWmsCountExecutionsQueryOptions({
      query: {
        ...scope.scopeQueryWithPaging(filters),
        ...optionalQuery('locationCode', filters.locationCode),
      },
    }),
    enabled: scope.hasScope.value,
  }))

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsCountExecutionMutationOptions(),
    onSuccess() {
      void executionsQuery.refetch()
    },
  })

  return {
    filters,
    executions: computed<BusinessConsoleWmsCountExecutionItem[]>(() =>
      listItems<BusinessConsoleWmsCountExecutionItem>(executionsQuery.data.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined),
    ),
    total: computed(() => listTotal(executionsQuery.data.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined)),
    pending: executionsQuery.isLoading,
    error: executionsQuery.error,
    refresh: executionsQuery.refetch,
    completeCount: (countExecutionId: string, input: CompleteCountInput) => {
      // 页面提供 countedQuantity/idempotencyKey（幂等键跨重试复用）；
      // org/env 不取自 input，恒由登录主体注入 query，敌意 org/env 永远落空。
      const body = { ...input } satisfies BusinessConsoleCompleteWmsCountExecutionRequest
      return completeMutation.mutateAsync({ path: { countExecutionId }, query: scope.scopeQuery(), body })
    },
    completePending: completeMutation.isLoading,
  }
}
