import {
  completeBusinessConsoleWmsCountExecutionMutationOptions,
  completeBusinessConsoleWmsInboundOrderMutationOptions,
  completeBusinessConsoleWmsOutboundOrderMutationOptions,
  confirmBusinessConsoleOperation,
  listBusinessConsoleWmsCountExecutions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrders,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  listBusinessConsoleWmsReceivingQualityGatesQueryOptions,
  type BusinessConsoleCompleteWmsCountExecutionRequest,
  type BusinessConsoleCompleteWmsInboundOrderRequest,
  type BusinessConsoleCompleteWmsOutboundOrderRequest,
  type BusinessConsoleWmsCountExecutionItem,
  type BusinessConsoleWmsCountExecutionListEnvelope,
  type BusinessConsoleWmsInboundLineCaptureInput,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInboundOrderListEnvelope,
  type BusinessConsoleWmsReceivingQualityGateItem,
  type BusinessConsoleWmsReceivingQualityGateListEnvelope,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsOutboundOrderListEnvelope,
  type BusinessConsoleWmsWarehouseTaskItem,
  type BusinessConsoleWmsWarehouseTaskListEnvelope,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'

import { assertLifecycleActionExecutable } from '@/composables/lifecycleActionRecovery'
import { useAuthStore } from '@/stores/auth'
import { useListFreshness } from '@/composables/useListFreshness'

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
export type WmsCompletionAttemptOptions = Readonly<{
  attempt: 'initial' | 'retry'
  onCommandAttempt?: () => void
}>
// 收货现场按行采集的批号/效期（#935 闭环）：随 completeInbound 落库。
export type InboundLineCapture = BusinessConsoleWmsInboundLineCaptureInput

function defaultFilters<T extends WmsScopeFilters>(initial: Partial<T> = {}): T {
  return reactive({ skip: 0, take: DEFAULT_TAKE, ...initial }) as T
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

function listTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined) {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

function exactItem<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
  matches: (item: TItem) => boolean,
) {
  const matchesById = listItems(envelope).filter(matches)
  return matchesById.length === 1 ? matchesById[0] : undefined
}

// DRY scope binding：org/env 一律取登录主体；空 scope → 不发请求（enabled:false）。
// 五个域共用，避免 5× 复制 org/env 接线。
function useWmsScope() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const hasScope = computed(() => Boolean(organizationId.value && environmentId.value))
  const principalId = computed(
    () => auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
  )
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
  return { organizationId, environmentId, principalId, hasScope, scopeQuery, scopeQueryWithPaging }
}

export function useWmsInbound(initialFilters: Partial<WmsScopeFilters> = {}) {
  const scope = useWmsScope()
  const filters = defaultFilters<WmsScopeFilters>(initialFilters)

  const ordersQuery = useQuery(() => ({
    ...listBusinessConsoleWmsInboundOrdersQueryOptions({
      query: scope.scopeQueryWithPaging(filters),
    }),
    enabled: scope.hasScope.value,
  }))
  const lastUpdatedAt = useListFreshness(() => ordersQuery.data.value, scope.hasScope)

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsInboundOrderMutationOptions(),
    onSuccess() {
      void ordersQuery.refetch()
    },
  })

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    scopeReady: scope.hasScope,
    filters,
    orders: computed<BusinessConsoleWmsInboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsInboundOrderItem>(
        ordersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
      ),
    ),
    total: computed(() =>
      listTotal(ordersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined),
    ),
    pending: ordersQuery.isLoading,
    error: ordersQuery.error,
    lastUpdatedAt,
    refresh: () => (scope.hasScope.value ? ordersQuery.refetch() : Promise.resolve()),
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
      const pending = acquirePendingBusinessIntent(intentScope, () => idempotencyKey, {
        lines: lines ?? [],
      })
      const isReplay =
        restoredPending !== undefined && restoredPending.idempotencyKey === pending.idempotencyKey
      try {
        const { data } = await listBusinessConsoleWmsInboundOrders({
          query: { ...scope.scopeQuery(), inboundOrderId, skip: 0, take: 2 },
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
      } catch (error) {
        if (!isReplay) clearPendingBusinessIntent(intentScope)
        throw error
      }
      const frozenLines =
        (pending.payloadSnapshot as { lines?: InboundLineCapture[] } | undefined)?.lines ??
        lines ??
        []
      const body = {
        idempotencyKey: pending.idempotencyKey,
        ...(frozenLines.length ? { lines: frozenLines } : {}),
      } satisfies BusinessConsoleCompleteWmsInboundOrderRequest
      options.onCommandAttempt?.()
      return completePendingBusinessIntent(intentScope, async () =>
        confirmBusinessConsoleOperation(
          await completeMutation.mutateAsync({
            path: { inboundOrderId },
            query: scope.scopeQuery(),
            body,
          }),
          {
            expectedOperationType: 'wms.inbound-order.complete',
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceId: inboundOrderId,
          },
        ),
      )
    },
    completePending: completeMutation.isLoading,
  }
}

export function useWmsOutbound(initialFilters: Partial<WmsScopeFilters> = {}) {
  const scope = useWmsScope()
  const filters = defaultFilters<WmsScopeFilters>(initialFilters)

  const ordersQuery = useQuery(() => ({
    ...listBusinessConsoleWmsOutboundOrdersQueryOptions({
      query: scope.scopeQueryWithPaging(filters),
    }),
    enabled: scope.hasScope.value,
  }))
  const lastUpdatedAt = useListFreshness(() => ordersQuery.data.value, scope.hasScope)

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void ordersQuery.refetch()
    },
  })

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    scopeReady: scope.hasScope,
    filters,
    orders: computed<BusinessConsoleWmsOutboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsOutboundOrderItem>(
        ordersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
      ),
    ),
    total: computed(() =>
      listTotal(ordersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined),
    ),
    pending: ordersQuery.isLoading,
    error: ordersQuery.error,
    lastUpdatedAt,
    refresh: () => (scope.hasScope.value ? ordersQuery.refetch() : Promise.resolve()),
    completeOutbound: async (
      outboundOrderId: string,
      input: CompleteOutboundInput,
      options: WmsCompletionAttemptOptions = { attempt: 'initial' },
    ) => {
      // 页面提供 packReviewNo/passed/idempotencyKey（幂等键跨重试复用）；
      // org/env 不取自 input，恒由登录主体注入 query，敌意 org/env 永远落空。
      const { idempotencyKey: suppliedKey, ...payload } = input
      const intentScope = {
        principalId: scope.principalId.value,
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        operationType: 'wms.outbound-order.complete',
        payloadFingerprint: `${outboundOrderId}:${JSON.stringify(payload)}`,
      }
      const restoredPending = peekPendingBusinessIntent(intentScope)
      const pending = acquirePendingBusinessIntent(intentScope, () => suppliedKey, payload)
      const isReplay =
        restoredPending !== undefined && restoredPending.idempotencyKey === pending.idempotencyKey
      try {
        const { data } = await listBusinessConsoleWmsOutboundOrders({
          query: { ...scope.scopeQuery(), outboundOrderId, skip: 0, take: 2 },
          throwOnError: true,
        })
        const authoritative = exactItem(
          data as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
          (item: BusinessConsoleWmsOutboundOrderItem) => item.outboundOrderId === outboundOrderId,
        )
        assertLifecycleActionExecutable({
          domain: 'wms-outbound',
          action: 'complete',
          facts: {
            status: authoritative?.status,
            idempotentReplay: isReplay,
          },
        })
      } catch (error) {
        if (!isReplay) clearPendingBusinessIntent(intentScope)
        throw error
      }
      const body = {
        ...((pending.payloadSnapshot ?? payload) as Omit<CompleteOutboundInput, 'idempotencyKey'>),
        idempotencyKey: pending.idempotencyKey,
      } satisfies BusinessConsoleCompleteWmsOutboundOrderRequest
      options.onCommandAttempt?.()
      return completePendingBusinessIntent(intentScope, async () =>
        confirmBusinessConsoleOperation(
          await completeMutation.mutateAsync({
            path: { outboundOrderId },
            query: scope.scopeQuery(),
            body,
          }),
          {
            expectedOperationType: 'wms.outbound-order.complete',
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceId: outboundOrderId,
          },
        ),
      )
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
  const lastUpdatedAt = useListFreshness(() => tasksQuery.data.value, scope.hasScope)

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    scopeReady: scope.hasScope,
    filters,
    tasks: computed<BusinessConsoleWmsWarehouseTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWarehouseTaskItem>(
        tasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
      ),
    ),
    total: computed(() =>
      listTotal(tasksQuery.data.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined),
    ),
    pending: tasksQuery.isLoading,
    error: tasksQuery.error,
    lastUpdatedAt,
    refresh: () => (scope.hasScope.value ? tasksQuery.refetch() : Promise.resolve()),
  }
}

export function useWmsPicking(initialFilters: Partial<WmsTaskFilters> = {}) {
  return useWmsWarehouseTasks(listBusinessConsoleWmsPickingTasksQueryOptions, initialFilters)
}

export type ReceivingQualityGateLine = BusinessConsoleWmsReceivingQualityGateItem

// 按「选中收货单」查完整收货行（#705 投影 + includeNotRequired=true 含免检行）。
// 单据级质检状态标/上架门禁改由 ListInboundOrders 单据级派生字段驱动（避免按分页门禁
// 行跨页聚合出错）；本 composable 只在打开某单明细时查该单的全部行，用于展示/采集
// 批号效期与逐行门禁。用服务端精确 inboundOrderNo 过滤（非 keyword——keyword 亦命中
// sku/检验号会跨单串扰）；再暴露 total → complete 判据，行数被 take 截断（未证明完整）
// 时调用方 fail closed 禁止提交，避免以不完整行完成收货静默漏采集。
const RECEIVING_LINES_TAKE = 500
export function useWmsReceivingLines(inboundOrderNo: MaybeRefOrGetter<string>) {
  const scope = useWmsScope()
  const orderNo = computed(() => toValue(inboundOrderNo).trim())

  const linesQuery = useQuery(() => ({
    ...listBusinessConsoleWmsReceivingQualityGatesQueryOptions({
      query: {
        ...scope.scopeQuery(),
        skip: 0,
        take: RECEIVING_LINES_TAKE,
        inboundOrderNo: orderNo.value,
        includeNotRequired: true,
      },
    }),
    enabled: scope.hasScope.value && orderNo.value.length > 0,
  }))

  const envelope = computed(
    () => linesQuery.data.value as BusinessConsoleWmsReceivingQualityGateListEnvelope | undefined,
  )
  // 服务端已精确按单过滤；客户端再按精确单号防御性过滤（不改变结果，纯保险）。
  const lines = computed<ReceivingQualityGateLine[]>(() =>
    listItems<ReceivingQualityGateLine>(envelope.value).filter(
      (l) => (l.inboundOrderNo ?? '') === orderNo.value,
    ),
  )
  const total = computed(() => listTotal(envelope.value))
  // 完整性 fail-closed：入库单按域约束必有 ≥1 行，故要求 total>0 且已取回行数覆盖 total
  // （未被 take 截断）。total=0/空集（精确单号未命中、投影暂不一致、数据异常）判为不完整，
  // 调用方禁止提交并展示可重试异常态，避免以空采集行静默完成收货。
  const complete = computed(() => total.value > 0 && lines.value.length >= total.value)

  return {
    lines,
    total,
    complete,
    pending: linesQuery.isLoading,
    error: linesQuery.error,
    refresh: () =>
      scope.hasScope.value && orderNo.value.length > 0 ? linesQuery.refetch() : Promise.resolve(),
  }
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
  const lastUpdatedAt = useListFreshness(() => executionsQuery.data.value, scope.hasScope)

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsCountExecutionMutationOptions(),
    onSuccess() {
      void executionsQuery.refetch()
    },
  })

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    scopeReady: scope.hasScope,
    filters,
    executions: computed<BusinessConsoleWmsCountExecutionItem[]>(() =>
      listItems<BusinessConsoleWmsCountExecutionItem>(
        executionsQuery.data.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
      ),
    ),
    total: computed(() =>
      listTotal(
        executionsQuery.data.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
      ),
    ),
    pending: executionsQuery.isLoading,
    error: executionsQuery.error,
    lastUpdatedAt,
    refresh: () => (scope.hasScope.value ? executionsQuery.refetch() : Promise.resolve()),
    completeCount: async (
      countExecutionId: string,
      input: CompleteCountInput,
      options: WmsCompletionAttemptOptions = { attempt: 'initial' },
    ) => {
      // 页面提供 countedQuantity/idempotencyKey（幂等键跨重试复用）；
      // org/env 不取自 input，恒由登录主体注入 query，敌意 org/env 永远落空。
      const { idempotencyKey: suppliedKey, ...payload } = input
      const intentScope = {
        principalId: scope.principalId.value,
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        operationType: 'wms.count-execution.complete',
        payloadFingerprint: `${countExecutionId}:${JSON.stringify(payload)}`,
      }
      const restoredPending = peekPendingBusinessIntent(intentScope)
      const pending = acquirePendingBusinessIntent(intentScope, () => suppliedKey, payload)
      const isReplay =
        restoredPending !== undefined && restoredPending.idempotencyKey === pending.idempotencyKey
      try {
        const { data } = await listBusinessConsoleWmsCountExecutions({
          query: { ...scope.scopeQuery(), countExecutionId, skip: 0, take: 2 },
          throwOnError: true,
        })
        const authoritative = exactItem(
          data as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
          (item: BusinessConsoleWmsCountExecutionItem) =>
            item.countExecutionId === countExecutionId,
        )
        assertLifecycleActionExecutable({
          domain: 'wms-count',
          action: 'complete',
          facts: {
            status: authoritative?.status,
            idempotentReplay: isReplay,
          },
        })
      } catch (error) {
        if (!isReplay) clearPendingBusinessIntent(intentScope)
        throw error
      }
      const body = {
        ...((pending.payloadSnapshot ?? payload) as Omit<CompleteCountInput, 'idempotencyKey'>),
        idempotencyKey: pending.idempotencyKey,
      } satisfies BusinessConsoleCompleteWmsCountExecutionRequest
      options.onCommandAttempt?.()
      return completePendingBusinessIntent(intentScope, async () =>
        confirmBusinessConsoleOperation(
          await completeMutation.mutateAsync({
            path: { countExecutionId },
            query: scope.scopeQuery(),
            body,
          }),
          {
            expectedOperationType: 'wms.count-execution.complete',
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceId: countExecutionId,
          },
        ),
      )
    },
    completePending: completeMutation.isLoading,
  }
}
