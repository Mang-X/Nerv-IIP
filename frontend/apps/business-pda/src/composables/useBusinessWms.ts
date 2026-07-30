import {
  completeBusinessConsoleWmsPickingTask,
  completeBusinessConsoleWmsPutawayTask,
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
  listBusinessConsoleWmsPickingTasks,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasks,
  listBusinessConsoleWmsReceivingQualityGatesQueryOptions,
  recordBusinessConsoleWmsPickingTaskProgress,
  recordBusinessConsoleWmsPutawayTaskProgress,
  reportBusinessConsoleWmsPickingTaskException,
  reportBusinessConsoleWmsPutawayTaskException,
  startBusinessConsoleWmsPickingTask,
  startBusinessConsoleWmsPutawayTask,
  type BusinessConsoleCompleteWmsWarehouseTaskRequest,
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
  type BusinessConsoleWmsWarehouseTaskActionEnvelope,
  type BusinessConsoleWmsWarehouseTaskActionResult,
  type BusinessConsoleWmsWarehouseTaskListEnvelope,
  type BusinessConsoleRecordWmsWarehouseTaskProgressRequest,
  type BusinessConsoleReportWmsWarehouseTaskExceptionRequest,
  type BusinessConsoleStartWmsWarehouseTaskRequest,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
  type PendingBusinessIntentScope,
} from '@nerv-iip/business-core'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'

import {
  assertLifecycleActionExecutable,
  LifecycleActionUnavailableError,
} from '@/composables/lifecycleActionRecovery'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useAuthStore } from '@/stores/auth'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from '@/composables/useListFreshness'
import { useWmsWorkScope, type WmsWorkScopeCatalogKind } from '@/composables/useWmsWorkScope'

const DEFAULT_TAKE = 100
const TASK_PAGE_SIZE = 20

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
export type CompleteOutboundInput = Omit<
  BusinessConsoleCompleteWmsOutboundOrderRequest,
  'expectedVersion' | 'scopeKind' | 'scopeId'
>
export type CompleteCountInput = Omit<
  BusinessConsoleCompleteWmsCountExecutionRequest,
  'expectedVersion' | 'scopeKind' | 'scopeId'
>
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

function lifecycleUnavailable(resource?: { status?: string }) {
  const status = resource?.status?.trim().toLowerCase()
  const terminal = new Set(['completed', 'completedwithdifference', 'cancelled']).has(status ?? '')
  return new LifecycleActionUnavailableError({
    known: resource !== undefined,
    terminal,
    executable: false,
    legalNoop: false,
    reason:
      resource === undefined
        ? 'unknown-status'
        : terminal
          ? 'terminal-status'
          : 'incompatible-state',
  })
}

function requireVersion(resource: { status?: string; version?: number } | undefined) {
  const version = resource?.version
  if (!Number.isInteger(version)) throw lifecycleUnavailable(resource)
  return version!
}

function requireFrozenScope(
  snapshot: unknown,
): snapshot is { expectedVersion: number; scopeKind: string; scopeId: string } {
  if (!snapshot || typeof snapshot !== 'object') return false
  const value = snapshot as Record<string, unknown>
  return (
    Number.isInteger(value.expectedVersion) &&
    typeof value.scopeKind === 'string' &&
    value.scopeKind.trim().length > 0 &&
    typeof value.scopeId === 'string' &&
    value.scopeId.trim().length > 0
  )
}

// 明细接口仅需要租户边界；作业列表另由 WMS 可信目录绑定 self/work-pool/site。
function useWmsTenantScope() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const hasScope = computed(() => Boolean(organizationId.value && environmentId.value))
  const principalId = computed(
    () => auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
  )
  const scopeKey = computed(() => `${organizationId.value.trim()}:${environmentId.value.trim()}`)
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
  return {
    organizationId,
    environmentId,
    principalId,
    hasScope,
    scopeKey,
    scopeQuery,
    scopeQueryWithPaging,
  }
}

function useAuthorizedWmsScope(catalog: WmsWorkScopeCatalogKind) {
  const scope = useWmsWorkScope(catalog)
  const responseScopeKey = computed(
    () =>
      `${scope.organizationId.value.trim()}:${scope.environmentId.value.trim()}:${scope.scopeKey.value ?? ''}`,
  )
  const tenantQuery = () => ({
    organizationId: scope.organizationId.value,
    environmentId: scope.environmentId.value,
  })
  const scopeQuery = () => ({
    ...tenantQuery(),
    scopeKind: scope.scopeKind.value!,
    scopeId: scope.scopeId.value!,
  })
  const scopeQueryWithPaging = (filters: WmsScopeFilters) => ({
    ...scopeQuery(),
    skip: filters.skip,
    take: filters.take,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
  })

  return {
    ...scope,
    hasScope: scope.hasSelection,
    selectedScopeKey: scope.scopeKey,
    responseScopeKey,
    tenantQuery,
    scopeQuery,
    scopeQueryWithPaging,
  }
}

export function useWmsInbound(initialFilters: Partial<WmsScopeFilters> = {}) {
  const scope = useAuthorizedWmsScope('receipts')
  const filters = defaultFilters<WmsScopeFilters>({
    ...initialFilters,
    take: initialFilters.take ?? TASK_PAGE_SIZE,
  })
  const refreshing = shallowRef(false)
  const loadingMore = shallowRef(false)

  const ordersQuery = useQuery(() => ({
    ...listBusinessConsoleWmsInboundOrdersQueryOptions({
      query: scope.scopeQueryWithPaging(filters),
    }),
    enabled: scope.hasScope.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => ordersQuery.data.value,
    scope.responseScopeKey,
    scope.hasScope,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scope.hasScope)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scope.hasScope,
    ordersQuery.isLoading,
  )
  const orders = computed<BusinessConsoleWmsInboundOrderItem[]>(() =>
    listItems<BusinessConsoleWmsInboundOrderItem>(
      currentResponse.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined,
    ),
  )
  const total = computed(() =>
    listTotal(currentResponse.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined),
  )

  watch([scope.selectedScopeKey, () => filters.status, () => filters.keyword], () => {
    filters.skip = 0
    filters.take = TASK_PAGE_SIZE
  })

  async function refresh() {
    if (!scope.hasScope.value) return
    refreshing.value = true
    try {
      await ordersQuery.refetch()
    } finally {
      refreshing.value = false
    }
  }

  async function loadMore() {
    if (
      !scope.hasScope.value ||
      ordersQuery.isLoading.value ||
      loadingMore.value ||
      orders.value.length >= total.value
    ) {
      return
    }
    loadingMore.value = true
    filters.take += TASK_PAGE_SIZE
    try {
      await ordersQuery.refetch()
    } finally {
      loadingMore.value = false
    }
  }

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsInboundOrderMutationOptions(),
    onSuccess() {
      void ordersQuery.refetch()
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
    orders,
    total,
    pending: computed(() => ordersQuery.isLoading.value || scope.pending.value),
    error: computed(() => ordersQuery.error.value ?? scope.error.value),
    refreshing,
    loadingMore,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh,
    loadMore,
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
      const isReplay = restoredPending !== undefined
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
      const freshPayload = {
        lines: lines ?? [],
        scopeKind: scope.scopeKind.value!,
        scopeId: scope.scopeId.value!,
        expectedVersion: requireVersion(authoritative),
      }
      const pending = acquirePendingBusinessIntent(
        intentScope,
        () => idempotencyKey,
        restoredPending?.payloadSnapshot ?? freshPayload,
      )
      if (!requireFrozenScope(pending.payloadSnapshot)) throw lifecycleUnavailable(authoritative)
      const frozen = pending.payloadSnapshot as typeof freshPayload
      const frozenLines = (frozen.lines as InboundLineCapture[] | undefined) ?? []
      const body = {
        idempotencyKey: pending.idempotencyKey,
        scopeKind: frozen.scopeKind,
        scopeId: frozen.scopeId,
        expectedVersion: frozen.expectedVersion,
        ...(frozenLines.length ? { lines: frozenLines } : {}),
      } satisfies BusinessConsoleCompleteWmsInboundOrderRequest
      options.onCommandAttempt?.()
      return completePendingBusinessIntent(intentScope, async () =>
        confirmBusinessConsoleOperation(
          await completeMutation.mutateAsync({
            path: { inboundOrderId },
            query: scope.tenantQuery(),
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
  const scope = useAuthorizedWmsScope('shipments')
  const filters = defaultFilters<WmsScopeFilters>({
    ...initialFilters,
    take: initialFilters.take ?? TASK_PAGE_SIZE,
  })
  const refreshing = shallowRef(false)
  const loadingMore = shallowRef(false)

  const ordersQuery = useQuery(() => ({
    ...listBusinessConsoleWmsOutboundOrdersQueryOptions({
      query: scope.scopeQueryWithPaging(filters),
    }),
    enabled: scope.hasScope.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => ordersQuery.data.value,
    scope.responseScopeKey,
    scope.hasScope,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scope.hasScope)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scope.hasScope,
    ordersQuery.isLoading,
  )
  const orders = computed<BusinessConsoleWmsOutboundOrderItem[]>(() =>
    listItems<BusinessConsoleWmsOutboundOrderItem>(
      currentResponse.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined,
    ),
  )
  const total = computed(() =>
    listTotal(currentResponse.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined),
  )

  watch([scope.selectedScopeKey, () => filters.status, () => filters.keyword], () => {
    filters.skip = 0
    filters.take = TASK_PAGE_SIZE
  })

  async function refresh() {
    if (!scope.hasScope.value) return
    refreshing.value = true
    try {
      await ordersQuery.refetch()
    } finally {
      refreshing.value = false
    }
  }

  async function loadMore() {
    if (
      !scope.hasScope.value ||
      ordersQuery.isLoading.value ||
      loadingMore.value ||
      orders.value.length >= total.value
    ) {
      return
    }
    loadingMore.value = true
    filters.take += TASK_PAGE_SIZE
    try {
      await ordersQuery.refetch()
    } finally {
      loadingMore.value = false
    }
  }

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsOutboundOrderMutationOptions(),
    onSuccess() {
      void ordersQuery.refetch()
    },
  })

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    scopeKey: scope.selectedScopeKey,
    scopeOptions: scope.scopeOptions,
    selectedScopeLabel: scope.selectedScopeLabel,
    scopeReady: scope.hasScope,
    filters,
    orders,
    total,
    pending: computed(() => ordersQuery.isLoading.value || scope.pending.value),
    error: computed(() => ordersQuery.error.value ?? scope.error.value),
    refreshing,
    loadingMore,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh,
    loadMore,
    completeOutbound: async (
      outboundOrderId: string,
      input: CompleteOutboundInput,
      options: WmsCompletionAttemptOptions = { attempt: 'initial' },
    ) => {
      // 页面提供 packReviewNo/passed/idempotencyKey（幂等键跨重试复用）；
      // org/env 不取自 input，恒由登录主体注入 query，敌意 org/env 永远落空。
      const suppliedKey = input.idempotencyKey
      const payload = {
        packReviewNo: input.packReviewNo,
        ...(input.passed === undefined ? {} : { passed: input.passed }),
      }
      const intentScope = {
        principalId: scope.principalId.value,
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        operationType: 'wms.outbound-order.complete',
        payloadFingerprint: `${outboundOrderId}:${JSON.stringify(payload)}`,
      }
      const restoredPending = peekPendingBusinessIntent(intentScope)
      const isReplay = restoredPending !== undefined
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
      const freshPayload = {
        ...payload,
        scopeKind: scope.scopeKind.value!,
        scopeId: scope.scopeId.value!,
        expectedVersion: requireVersion(authoritative),
      }
      const pending = acquirePendingBusinessIntent(
        intentScope,
        () => suppliedKey,
        restoredPending?.payloadSnapshot ?? freshPayload,
      )
      if (!requireFrozenScope(pending.payloadSnapshot)) throw lifecycleUnavailable(authoritative)
      const frozen = pending.payloadSnapshot as typeof freshPayload
      const body = {
        ...frozen,
        idempotencyKey: pending.idempotencyKey,
      } satisfies BusinessConsoleCompleteWmsOutboundOrderRequest
      options.onCommandAttempt?.()
      return completePendingBusinessIntent(intentScope, async () =>
        confirmBusinessConsoleOperation(
          await completeMutation.mutateAsync({
            path: { outboundOrderId },
            query: scope.tenantQuery(),
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

export interface WmsWarehouseTask extends Omit<
  BusinessConsoleWmsWarehouseTaskItem,
  'warehouseTaskId' | 'allowedActions' | 'blockReasons'
> {
  warehouseTaskId: string
  allowedActions: string[]
  blockReasons: string[]
}

export interface WmsWarehouseTaskExecutionIntent {
  action: 'start' | 'progress' | 'exception' | 'complete'
  task: BusinessConsoleWmsWarehouseTaskItem
  executedQuantity?: number
  exceptionCode?: string
  reason?: string
}

function normalizeWarehouseTask(
  task: BusinessConsoleWmsWarehouseTaskItem,
): WmsWarehouseTask | undefined {
  const warehouseTaskId = task.warehouseTaskId?.trim()
  if (!warehouseTaskId) return undefined
  return {
    ...task,
    warehouseTaskId,
    allowedActions: task.allowedActions ?? [],
    blockReasons: task.blockReasons ?? [],
  }
}

function taskLifecycleError(task: BusinessConsoleWmsWarehouseTaskItem | undefined) {
  return lifecycleUnavailable(task)
}

function unwrapTaskActionResult(
  envelope: BusinessConsoleWmsWarehouseTaskActionEnvelope | undefined,
  warehouseTaskId: string,
) {
  if (
    envelope?.success !== true ||
    !envelope.data ||
    envelope.data.warehouseTaskId !== warehouseTaskId
  ) {
    throw new Error('WMS 作业动作回执无效，请刷新后重试')
  }
  return envelope.data
}

async function invokeWarehouseTaskAction(
  taskType: 'picking' | 'putaway',
  action: WmsWarehouseTaskExecutionIntent['action'],
  warehouseTaskId: string,
  query: {
    organizationId: string
    environmentId: string
    scopeKind: string
    scopeId: string
  },
  body:
    | BusinessConsoleStartWmsWarehouseTaskRequest
    | BusinessConsoleRecordWmsWarehouseTaskProgressRequest
    | BusinessConsoleReportWmsWarehouseTaskExceptionRequest
    | BusinessConsoleCompleteWmsWarehouseTaskRequest,
): Promise<BusinessConsoleWmsWarehouseTaskActionResult> {
  const common = {
    path: { warehouseTaskId },
    query,
    throwOnError: true as const,
  }
  const response =
    taskType === 'picking'
      ? action === 'start'
        ? await startBusinessConsoleWmsPickingTask({
            ...common,
            body: body as BusinessConsoleStartWmsWarehouseTaskRequest,
          })
        : action === 'progress'
          ? await recordBusinessConsoleWmsPickingTaskProgress({
              ...common,
              body: body as BusinessConsoleRecordWmsWarehouseTaskProgressRequest,
            })
          : action === 'exception'
            ? await reportBusinessConsoleWmsPickingTaskException({
                ...common,
                body: body as BusinessConsoleReportWmsWarehouseTaskExceptionRequest,
              })
            : await completeBusinessConsoleWmsPickingTask({
                ...common,
                body: body as BusinessConsoleCompleteWmsWarehouseTaskRequest,
              })
      : action === 'start'
        ? await startBusinessConsoleWmsPutawayTask({
            ...common,
            body: body as BusinessConsoleStartWmsWarehouseTaskRequest,
          })
        : action === 'progress'
          ? await recordBusinessConsoleWmsPutawayTaskProgress({
              ...common,
              body: body as BusinessConsoleRecordWmsWarehouseTaskProgressRequest,
            })
          : action === 'exception'
            ? await reportBusinessConsoleWmsPutawayTaskException({
                ...common,
                body: body as BusinessConsoleReportWmsWarehouseTaskExceptionRequest,
              })
            : await completeBusinessConsoleWmsPutawayTask({
                ...common,
                body: body as BusinessConsoleCompleteWmsWarehouseTaskRequest,
              })

  return unwrapTaskActionResult(
    response.data as BusinessConsoleWmsWarehouseTaskActionEnvelope | undefined,
    warehouseTaskId,
  )
}

function useWmsWarehouseTasks(
  taskType: 'picking' | 'putaway',
  queryOptionsFactory:
    | typeof listBusinessConsoleWmsPickingTasksQueryOptions
    | typeof listBusinessConsoleWmsPutawayTasksQueryOptions,
  initialFilters: Partial<WmsTaskFilters> = {},
) {
  const scope = useAuthorizedWmsScope(taskType === 'picking' ? 'shipments' : 'receipts')
  const filters = defaultFilters<WmsTaskFilters>({
    ...initialFilters,
    take: initialFilters.take ?? TASK_PAGE_SIZE,
  })
  const refreshing = shallowRef(false)
  const loadingMore = shallowRef(false)
  const actionPending = shallowRef(false)
  const actionError = shallowRef<unknown>()
  const pendingTaskIntentScopes = new Map<string, PendingBusinessIntentScope>()

  const tasksQuery = useQuery(() => ({
    ...queryOptionsFactory({
      query: {
        ...scope.scopeQueryWithPaging(filters),
        ...optionalQuery('locationCode', filters.locationCode),
      },
    }),
    enabled: scope.hasScope.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => tasksQuery.data.value,
    scope.responseScopeKey,
    scope.hasScope,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scope.hasScope)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scope.hasScope,
    tasksQuery.isLoading,
  )
  const tasks = computed<WmsWarehouseTask[]>(() =>
    listItems<BusinessConsoleWmsWarehouseTaskItem>(
      currentResponse.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
    )
      .map(normalizeWarehouseTask)
      .filter((task): task is WmsWarehouseTask => task !== undefined),
  )
  const total = computed(() =>
    listTotal(currentResponse.value as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined),
  )

  watch(
    [
      scope.selectedScopeKey,
      () => filters.status,
      () => filters.keyword,
      () => filters.locationCode,
    ],
    () => {
      filters.skip = 0
      filters.take = TASK_PAGE_SIZE
    },
  )

  async function refresh() {
    if (!scope.hasScope.value) return
    refreshing.value = true
    try {
      await tasksQuery.refetch()
    } finally {
      refreshing.value = false
    }
  }

  async function loadMore() {
    if (
      !scope.hasScope.value ||
      tasksQuery.isLoading.value ||
      loadingMore.value ||
      tasks.value.length >= total.value
    ) {
      return
    }
    loadingMore.value = true
    filters.take += TASK_PAGE_SIZE
    try {
      await tasksQuery.refetch()
    } finally {
      loadingMore.value = false
    }
  }

  async function executeTask(intent: WmsWarehouseTaskExecutionIntent) {
    if (!scope.hasScope.value) throw taskLifecycleError(undefined)
    const warehouseTaskId = intent.task.warehouseTaskId?.trim()
    const expectedVersion = intent.task.version
    if (!warehouseTaskId || !Number.isInteger(expectedVersion)) {
      throw taskLifecycleError(intent.task)
    }

    const actionPayload =
      intent.action === 'start'
        ? { expectedVersion: expectedVersion! }
        : intent.action === 'progress'
          ? { expectedVersion: expectedVersion!, executedQuantity: intent.executedQuantity }
          : intent.action === 'exception'
            ? {
                expectedVersion: expectedVersion!,
                exceptionCode: intent.exceptionCode?.trim(),
                reason: intent.reason?.trim(),
              }
            : {
                expectedVersion: expectedVersion!,
                executedQuantity: intent.executedQuantity,
                differenceReason: intent.reason?.trim() || undefined,
              }

    if (
      ((intent.action === 'progress' || intent.action === 'complete') &&
        !Number.isFinite(intent.executedQuantity)) ||
      (intent.action === 'exception' && (!actionPayload.exceptionCode || !actionPayload.reason))
    ) {
      throw new Error('作业动作参数不完整，请重新输入')
    }

    const liveCommandScope = scope.scopeQuery()
    const payloadSnapshot = {
      ...actionPayload,
      scopeKind: liveCommandScope.scopeKind,
      scopeId: liveCommandScope.scopeId,
    }
    const pendingLookupKey = [
      scope.principalId.value,
      liveCommandScope.organizationId,
      liveCommandScope.environmentId,
      warehouseTaskId,
      intent.action,
      JSON.stringify(actionPayload),
    ].join(':')
    let intentScope = pendingTaskIntentScopes.get(pendingLookupKey)
    if (intentScope && !peekPendingBusinessIntent(intentScope)) {
      pendingTaskIntentScopes.delete(pendingLookupKey)
      intentScope = undefined
    }
    intentScope ??= {
      principalId: scope.principalId.value,
      organizationId: liveCommandScope.organizationId,
      environmentId: liveCommandScope.environmentId,
      operationType: `wms.${taskType}-task.${intent.action}`,
      payloadFingerprint: `${warehouseTaskId}:${JSON.stringify(payloadSnapshot)}`,
    }
    const restoredPending = peekPendingBusinessIntent(intentScope)
    const pending = acquirePendingBusinessIntent(intentScope, makeIdempotencyKey, payloadSnapshot)
    pendingTaskIntentScopes.set(pendingLookupKey, intentScope)
    const isReplay =
      restoredPending !== undefined && restoredPending.idempotencyKey === pending.idempotencyKey
    if (!requireFrozenScope(pending.payloadSnapshot)) {
      throw taskLifecycleError(intent.task)
    }
    const frozenSnapshot = pending.payloadSnapshot as typeof payloadSnapshot
    const frozenCommandScope = {
      organizationId: intentScope.organizationId,
      environmentId: intentScope.environmentId,
      scopeKind: frozenSnapshot.scopeKind,
      scopeId: frozenSnapshot.scopeId,
    }

    actionError.value = undefined
    actionPending.value = true
    let mutationStarted = false
    try {
      const listOperation =
        taskType === 'picking'
          ? listBusinessConsoleWmsPickingTasks
          : listBusinessConsoleWmsPutawayTasks
      const { data } = await listOperation({
        query: {
          ...frozenCommandScope,
          keyword: intent.task.taskNo?.trim() || warehouseTaskId,
          skip: 0,
          take: TASK_PAGE_SIZE,
        },
        throwOnError: true,
      })
      const authoritative = exactItem(
        data as BusinessConsoleWmsWarehouseTaskListEnvelope | undefined,
        (item: BusinessConsoleWmsWarehouseTaskItem) => item.warehouseTaskId === warehouseTaskId,
      )
      if (
        !isReplay &&
        (!authoritative ||
          authoritative.version !== frozenSnapshot.expectedVersion ||
          !authoritative.allowedActions?.includes(intent.action))
      ) {
        clearPendingBusinessIntent(intentScope)
        throw taskLifecycleError(authoritative)
      }

      const { scopeKind: _scopeKind, scopeId: _scopeId, ...frozenPayload } = frozenSnapshot
      const body = {
        ...frozenPayload,
        idempotencyKey: pending.idempotencyKey,
      } as
        | BusinessConsoleStartWmsWarehouseTaskRequest
        | BusinessConsoleRecordWmsWarehouseTaskProgressRequest
        | BusinessConsoleReportWmsWarehouseTaskExceptionRequest
        | BusinessConsoleCompleteWmsWarehouseTaskRequest
      mutationStarted = true
      const result = await completePendingBusinessIntent(intentScope, () =>
        invokeWarehouseTaskAction(
          taskType,
          intent.action,
          warehouseTaskId,
          frozenCommandScope,
          body,
        ),
      )
      await tasksQuery.refetch()
      return result
    } catch (error) {
      if (!isReplay && !mutationStarted && peekPendingBusinessIntent(intentScope)) {
        clearPendingBusinessIntent(intentScope)
      }
      actionError.value = error
      throw error
    } finally {
      if (!peekPendingBusinessIntent(intentScope)) {
        pendingTaskIntentScopes.delete(pendingLookupKey)
      }
      actionPending.value = false
    }
  }

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    principalId: scope.principalId,
    scopeKey: scope.selectedScopeKey,
    scopeOptions: scope.scopeOptions,
    selectedScopeLabel: scope.selectedScopeLabel,
    scopeReady: scope.hasScope,
    filters,
    tasks,
    total,
    pending: computed(() => tasksQuery.isLoading.value || scope.pending.value),
    error: computed(() => actionError.value ?? tasksQuery.error.value ?? scope.error.value),
    refreshing,
    loadingMore,
    actionPending,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh,
    loadMore,
    executeTask,
  }
}

export function useWmsPicking(initialFilters: Partial<WmsTaskFilters> = {}) {
  return useWmsWarehouseTasks(
    'picking',
    listBusinessConsoleWmsPickingTasksQueryOptions,
    initialFilters,
  )
}

export type ReceivingQualityGateLine = BusinessConsoleWmsReceivingQualityGateItem

// 按「选中收货单」查完整收货行（#705 投影 + includeNotRequired=true 含免检行）。
// 单据级质检状态标/上架门禁改由 ListInboundOrders 单据级派生字段驱动（避免按分页门禁
// 行跨页聚合出错）；本 composable 只在打开某单明细时查该单的全部行，用于展示/采集
// 批号效期与逐行门禁。用服务端精确 inboundOrderNo 过滤（非 keyword——keyword 亦命中
// sku/检验号会跨单串扰）；再暴露 total → complete 判据，行数被 take 截断（未证明完整）
// 时调用方 fail closed 禁止提交，避免以不完整行完成收货静默漏采集。
const RECEIVING_LINES_TAKE = 500
export interface WmsReceivingLinesScope {
  scopeKind: MaybeRefOrGetter<string | undefined>
  scopeId: MaybeRefOrGetter<string | undefined>
}

export function useWmsReceivingLines(
  inboundOrderNo: MaybeRefOrGetter<string>,
  selectedScope: WmsReceivingLinesScope,
) {
  const scope = useWmsTenantScope()
  const orderNo = computed(() => toValue(inboundOrderNo).trim())
  const scopeKind = computed(() => toValue(selectedScope.scopeKind)?.trim() ?? '')
  const scopeId = computed(() => toValue(selectedScope.scopeId)?.trim() ?? '')
  const enabled = computed(
    () =>
      scope.hasScope.value &&
      scopeKind.value.length > 0 &&
      scopeId.value.length > 0 &&
      orderNo.value.length > 0,
  )
  const responseScopeKey = computed(
    () => `${scope.scopeKey.value}:${scopeKind.value}:${scopeId.value}:${orderNo.value}`,
  )

  const linesQuery = useQuery(() => ({
    ...listBusinessConsoleWmsReceivingQualityGatesQueryOptions({
      query: {
        ...scope.scopeQuery(),
        scopeKind: scopeKind.value,
        scopeId: scopeId.value,
        skip: 0,
        take: RECEIVING_LINES_TAKE,
        inboundOrderNo: orderNo.value,
        includeNotRequired: true,
      },
    }),
    enabled: enabled.value,
  }))

  const currentResponse = useScopeBoundListResponse(
    () => linesQuery.data.value,
    responseScopeKey,
    enabled,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    enabled,
    linesQuery.isLoading,
  )
  const envelope = computed(
    () => currentResponse.value as BusinessConsoleWmsReceivingQualityGateListEnvelope | undefined,
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
    hasSuccessfulResponse,
    hasFailedResponse,
    pending: linesQuery.isLoading,
    error: linesQuery.error,
    refresh: () => (enabled.value ? linesQuery.refetch() : Promise.resolve()),
  }
}

export function useWmsPutaway(initialFilters: Partial<WmsTaskFilters> = {}) {
  return useWmsWarehouseTasks(
    'putaway',
    listBusinessConsoleWmsPutawayTasksQueryOptions,
    initialFilters,
  )
}

export function useWmsCount(initialFilters: Partial<WmsTaskFilters> = {}) {
  const scope = useAuthorizedWmsScope('counts')
  const filters = defaultFilters<WmsTaskFilters>({
    ...initialFilters,
    take: initialFilters.take ?? TASK_PAGE_SIZE,
  })
  const refreshing = shallowRef(false)
  const loadingMore = shallowRef(false)

  const executionsQuery = useQuery(() => ({
    ...listBusinessConsoleWmsCountExecutionsQueryOptions({
      query: {
        ...scope.scopeQueryWithPaging(filters),
        ...optionalQuery('locationCode', filters.locationCode),
      },
    }),
    enabled: scope.hasScope.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => executionsQuery.data.value,
    scope.responseScopeKey,
    scope.hasScope,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scope.hasScope)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scope.hasScope,
    executionsQuery.isLoading,
  )
  const executions = computed<BusinessConsoleWmsCountExecutionItem[]>(() =>
    listItems<BusinessConsoleWmsCountExecutionItem>(
      currentResponse.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined,
    ),
  )
  const total = computed(() =>
    listTotal(currentResponse.value as BusinessConsoleWmsCountExecutionListEnvelope | undefined),
  )

  watch(
    [
      scope.selectedScopeKey,
      () => filters.status,
      () => filters.keyword,
      () => filters.locationCode,
    ],
    () => {
      filters.skip = 0
      filters.take = TASK_PAGE_SIZE
    },
  )

  async function refresh() {
    if (!scope.hasScope.value) return
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
      loadingMore.value ||
      executions.value.length >= total.value
    ) {
      return
    }
    loadingMore.value = true
    filters.take += TASK_PAGE_SIZE
    try {
      await executionsQuery.refetch()
    } finally {
      loadingMore.value = false
    }
  }

  const completeMutation = useMutation({
    ...completeBusinessConsoleWmsCountExecutionMutationOptions(),
    onSuccess() {
      void executionsQuery.refetch()
    },
  })

  return {
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    scopeKey: scope.selectedScopeKey,
    scopeOptions: scope.scopeOptions,
    selectedScopeLabel: scope.selectedScopeLabel,
    scopeReady: scope.hasScope,
    filters,
    executions,
    total,
    pending: computed(() => executionsQuery.isLoading.value || scope.pending.value),
    error: computed(() => executionsQuery.error.value ?? scope.error.value),
    refreshing,
    loadingMore,
    lastUpdatedAt,
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
      const { data } = await listBusinessConsoleWmsCountExecutions({
        query: { ...scope.scopeQuery(), countExecutionId, skip: 0, take: 2 },
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
      const freshPayload = {
        ...payload,
        scopeKind: scope.scopeKind.value!,
        scopeId: scope.scopeId.value!,
        expectedVersion: requireVersion(authoritative),
      }
      const pending = acquirePendingBusinessIntent(
        intentScope,
        () => suppliedKey,
        restoredPending?.payloadSnapshot ?? freshPayload,
      )
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
          },
        ),
      )
    },
    completePending: completeMutation.isLoading,
  }
}
