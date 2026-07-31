import {
  createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions,
  claimBusinessConsoleQualityInspectionTaskMutationOptions,
  confirmBusinessConsoleOperation,
  listBusinessConsoleQualityInspectionPlanCharacteristicsQueryOptions,
  listBusinessConsoleQualityInspectionTasks,
  listBusinessConsoleQualityInspectionTasksQueryOptions,
  listBusinessConsoleQualityReasonCodesQueryOptions,
  type BusinessConsoleInspectionCharacteristicResult,
  type BusinessConsoleInspectionPlanCharacteristicItem,
  type BusinessConsoleQualityInspectionTaskItem,
  type BusinessConsoleQualityReasonItem,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  completePendingBusinessIntent,
  peekPendingBusinessIntent,
  type QualityCharacteristicResultLine as ResultLine,
} from '@nerv-iip/business-core'
import { assertLifecycleActionExecutable } from '@/composables/lifecycleActionRecovery'
import { useAuthStore } from '@/stores/auth'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from '@/composables/useListFreshness'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { makeIdempotencyKey } from './makeIdempotencyKey'

const DEFAULT_TAKE = 100
/** facade / Quality 查询验证器的 take 上限——超页数据靠受限分页迭代聚合，不把 take 扩过上限。 */
const MAX_TAKE = 200

/** 待检工作台默认只呈现 pending（未检）任务；提交后任务转 completed 并从列表失效回落。 */
const PENDING_STATUS = 'pending'

export interface InspectionTaskFilters {
  status: string
  skip: number
  take: number
}

function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  if (!envelope?.success) return []
  return envelope.data?.items ?? []
}

function listTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) return 0
  return envelope.data?.total ?? 0
}

/** 谓词匹配检验任务列表读的查询键——提交后跨 composable 实例失效。 */
function isInspectionTasksQuery(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
  return keyParts.some(
    (part) =>
      typeof part === 'object' &&
      part !== null &&
      '_id' in part &&
      part._id === 'listBusinessConsoleQualityInspectionTasks',
  )
}

function ignoreBackgroundError(_error: unknown) {}

function claimBlockMessage(reason: string | undefined) {
  switch (reason) {
    case 'task-completed':
      return '任务已完成，仅可查看。'
    case 'task-already-claimed':
      return '任务已由其他检验员领取。'
    case 'task-assigned-to-another-inspector':
      return '任务已派给其他检验员，无法领取。'
    case 'task-assigned-to-another-team':
      return '任务已派给其他班组，无法领取。'
    case 'task-outside-selected-work-scope':
      return '任务不在当前工作范围内，无法领取。'
    default:
      return '当前任务不可领取，请刷新后重试。'
  }
}

/**
 * 检验任务（待检工作台）读 + 逐特性录结果提交数据封装（MAN-457 / #811，与 console C3-1 / #801 同源）。
 *
 * - org/env 与 `principalId` 均取登录主体；列表显式请求 Self scope，未登录或任一作用域值
 *   缺失时不发请求，且查询键绑定 principal，避免换人登录后复用旧缓存。
 * - 列表默认按 `status=pending` 服务端过滤；来源、关键字和超期条件均由 facade 下传 Quality
 *   服务端后再分页，扫码命中第一页之外时仍可依据权威 `total` 继续加载。
 * - 待领取任务先调用 claim；服务端以授权班组、expectedVersion 和稳定意图键原子领取，
 *   只有权威 allowedActions 包含 submit 后才能进入逐特性录入。
 * - 提交端点按显式 `idempotencyKey` 绑定请求指纹与权威 inspectionRecordId；超时后同一意图
 *   复用原键，成功后清除，下一次新意图换键。权威 pass/fail 仍由后端按检验计划规格计算。
 */
export function useBusinessQualityInspectionTasks() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const inspectorUserId = computed(() => auth.principal?.principalId ?? '')
  const scopeReady = computed(() =>
    Boolean(organizationId.value && environmentId.value && inspectorUserId.value),
  )
  const scopeKey = computed(
    () =>
      `${organizationId.value.trim()}:${environmentId.value.trim()}:${inspectorUserId.value.trim()}`,
  )

  const queryCache = useQueryCache()
  const filters = reactive<InspectionTaskFilters>({
    status: PENDING_STATUS,
    skip: 0,
    take: DEFAULT_TAKE,
  })

  const listQuery = useQuery(() => ({
    ...listBusinessConsoleQualityInspectionTasksQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        scopeKind: 'self',
        scopeId: inspectorUserId.value,
        status: filters.status,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: scopeReady.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => listQuery.data.value,
    scopeKey,
    scopeReady,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, scopeReady)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    listQuery.isLoading,
  )

  // 原因码目录（计数特性判不合格时的 Picker 数据源）：只取启用项，小目录一次拉全。
  const reasonCodesQuery = useQuery(() => ({
    ...listBusinessConsoleQualityReasonCodesQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        enabled: true,
        skip: 0,
        take: 200,
      },
    }),
    enabled: scopeReady.value,
  }))

  const reasonCodes = computed<BusinessConsoleQualityReasonItem[]>(() =>
    listItems<BusinessConsoleQualityReasonItem>(reasonCodesQuery.data.value),
  )

  // 超出基础查询（take ≤ MAX_TAKE）之外、按页聚合的补充任务页——「加载更多 / 扫码全量」共用。
  const extraTasks = shallowRef<BusinessConsoleQualityInspectionTaskItem[]>([])
  let paginationEpoch = 0
  watch(
    [scopeKey, () => filters.status],
    () => {
      paginationEpoch += 1
      extraTasks.value = []
    },
    { flush: 'sync' },
  )

  const submitMutation = useMutation({
    ...createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions(),
    onSuccess() {
      // 基础页失效重取；聚合补充页会 stale，一并丢弃（需要时再按页重聚合）。
      paginationEpoch += 1
      extraTasks.value = []
      void queryCache
        .invalidateQueries({ predicate: isInspectionTasksQuery })
        .catch(ignoreBackgroundError)
    },
  })
  const claimMutation = useMutation({
    ...claimBusinessConsoleQualityInspectionTaskMutationOptions(),
    onSuccess() {
      paginationEpoch += 1
      extraTasks.value = []
      void queryCache
        .invalidateQueries({ predicate: isInspectionTasksQuery })
        .catch(ignoreBackgroundError)
    },
  })

  const baseTasks = computed<BusinessConsoleQualityInspectionTaskItem[]>(() =>
    listItems<BusinessConsoleQualityInspectionTaskItem>(currentResponse.value),
  )
  const tasks = computed<BusinessConsoleQualityInspectionTaskItem[]>(() => {
    if (extraTasks.value.length === 0) return baseTasks.value
    const seen = new Set(baseTasks.value.map((t) => t.inspectionTaskId))
    return [...baseTasks.value, ...extraTasks.value.filter((t) => !seen.has(t.inspectionTaskId))]
  })
  const total = computed(() => listTotal(currentResponse.value))
  const loaded = computed(() => tasks.value.length)
  const hasMore = computed(() => loaded.value < total.value)

  function capturePaginationScope() {
    return {
      epoch: paginationEpoch,
      key: scopeKey.value,
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      principalId: inspectorUserId.value,
      status: filters.status,
    }
  }

  function isCurrentPaginationScope(execution: ReturnType<typeof capturePaginationScope>) {
    return (
      scopeReady.value &&
      paginationEpoch === execution.epoch &&
      scopeKey.value === execution.key &&
      filters.status === execution.status &&
      inspectorUserId.value === execution.principalId
    )
  }

  /** 受限拉取一页（take 不超上限），返回该页 items；失败抛错由调用方处理。 */
  async function fetchPage(
    skip: number,
    take: number,
    execution: ReturnType<typeof capturePaginationScope>,
  ) {
    const { data } = await listBusinessConsoleQualityInspectionTasks({
      query: {
        organizationId: execution.organizationId,
        environmentId: execution.environmentId,
        scopeKind: 'self',
        scopeId: execution.principalId,
        status: execution.status,
        skip,
        take: Math.min(Math.max(take, 1), MAX_TAKE),
      },
    })
    if (!isCurrentPaginationScope(execution)) return null
    if (data?.success !== true) {
      throw new Error(data?.message?.trim() || '待检任务分页查询失败，请刷新重试。')
    }
    return listItems<BusinessConsoleQualityInspectionTaskItem>(data)
  }

  /**
   * 加载更多（facade 无关键字/来源过滤，客户端筛选命中首页之外时据 total 加载）。基础查询 take
   * 封顶 MAX_TAKE（后端验证器上限），超出部分按页拉取聚合到 `extraTasks`，不把 take 扩过上限。
   */
  async function loadMore() {
    if (!scopeReady.value || !hasMore.value) return
    if (filters.take < MAX_TAKE) {
      filters.take = Math.min(filters.take + DEFAULT_TAKE, MAX_TAKE)
      return
    }
    const execution = capturePaginationScope()
    const page = await fetchPage(loaded.value, MAX_TAKE, execution)
    if (!page || !isCurrentPaginationScope(execution)) return
    if (page.length > 0) extraTasks.value = [...extraTasks.value, ...page]
  }

  /**
   * 加载全部待检任务后返回最新集合。扫码直达用：facade 无 sourceDocumentId/关键字服务端过滤，
   * 目标任务可能落在未加载分页；按 **受限分页迭代**（每页 ≤ MAX_TAKE）聚合覆盖全量再匹配——
   * 不把 take 直接扩到 total（超过后端验证器上限会整段失败）。
   */
  async function ensureAllLoaded() {
    if (!scopeReady.value) return tasks.value
    const execution = capturePaginationScope()
    // 防御：空页即止（total 与实际漂移时不空转）。
    while (hasMore.value && isCurrentPaginationScope(execution)) {
      const page = await fetchPage(
        loaded.value,
        Math.min(MAX_TAKE, total.value - loaded.value),
        execution,
      )
      if (!page || !isCurrentPaginationScope(execution)) break
      if (page.length === 0) break
      extraTasks.value = [...extraTasks.value, ...page]
    }
    return tasks.value
  }

  /**
   * 提交检验结果。`resultLines` 由 `@nerv-iip/business-core` 归一（业务口径），此处仅注入
   * org/env（query）；检验员身份只由网关从认证主体注入，不进入公开请求体。
   *
   * `dispositionReason`（处置原因）：检验结果**不合格时后端必填**（`InspectionRecord` 领域校验），
   * 合格时可省。由调用页在判不合格时收集并传入。
   */
  async function submitInspection(
    inspectionTaskId: string,
    resultLines: readonly ResultLine[],
    dispositionReason?: string,
  ) {
    if (!scopeReady.value || !inspectorUserId.value) {
      throw new Error('登录态未就绪，请稍后重试')
    }
    const reason = (dispositionReason ?? '').trim()
    const fingerprint = JSON.stringify({
      inspectionTaskId,
      inspectorUserId: inspectorUserId.value,
      resultLines,
      dispositionReason: reason,
    })
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      operationType: 'quality.inspection-task.submit',
      payloadFingerprint: fingerprint,
    }
    const isReplay = Boolean(peekPendingBusinessIntent(scope))
    const { idempotencyKey } = acquirePendingBusinessIntent(
      scope,
      () => `quality-submit-${makeIdempotencyKey()}`,
    )
    try {
      const { data: authoritativeEnvelope } = await listBusinessConsoleQualityInspectionTasks({
        query: {
          organizationId: organizationId.value,
          environmentId: environmentId.value,
          scopeKind: 'self',
          scopeId: inspectorUserId.value,
          inspectionTaskId,
          skip: 0,
          take: 2,
        },
        throwOnError: true,
      })
      const exactMatches = listItems<BusinessConsoleQualityInspectionTaskItem>(
        authoritativeEnvelope,
      ).filter((task) => task.inspectionTaskId === inspectionTaskId)
      const authoritative = exactMatches.length === 1 ? exactMatches[0] : undefined
      assertLifecycleActionExecutable({
        domain: 'quality-inspection-task',
        action: 'create-record',
        facts: {
          status: authoritative?.status,
          inspectionRecordId: authoritative?.inspectionRecordId,
          idempotentReplay: isReplay,
        },
      })
    } catch (error) {
      if (!isReplay) clearPendingBusinessIntent(scope)
      throw error
    }
    return completePendingBusinessIntent(scope, async () =>
      confirmBusinessConsoleOperation(
        await submitMutation.mutateAsync({
          path: { inspectionTaskId },
          query: {
            organizationId: organizationId.value,
            environmentId: environmentId.value,
          },
          body: {
            // business-core 的行结构与 api-client `InspectionCharacteristicResult` 同形，直接透传。
            resultLines: resultLines as BusinessConsoleInspectionCharacteristicResult[],
            ...(reason ? { dispositionReason: reason } : {}),
            idempotencyKey,
          },
        }),
        {
          expectedOperationType: 'quality.inspection-task.submit',
          expectedIdempotencyKey: idempotencyKey,
          expectedResourceIdSelector: (envelope) => envelope.data?.inspectionRecordId,
        },
      ),
    )
  }

  async function claimTask(task: BusinessConsoleQualityInspectionTaskItem) {
    if (!scopeReady.value || !inspectorUserId.value) {
      throw new Error('登录态未就绪，请稍后重试')
    }
    if (task.status === 'in-progress' && task.assignedInspectorUserId === inspectorUserId.value) {
      return task
    }
    if (!task.allowedActions?.includes('claim')) {
      throw new Error(claimBlockMessage(task.blockReasons?.[0]))
    }
    if (!task.inspectionTaskId || !task.version) {
      throw new Error('任务版本信息缺失，请刷新后重试。')
    }
    const scope = {
      principalId: inspectorUserId.value,
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      operationType: 'quality.inspection-task.claim',
      payloadFingerprint: `${task.inspectionTaskId}:${task.version}`,
    }
    const { idempotencyKey } = acquirePendingBusinessIntent(
      scope,
      () => `quality-claim-${makeIdempotencyKey()}`,
    )
    const envelope = await completePendingBusinessIntent(scope, () =>
      claimMutation.mutateAsync({
        path: { inspectionTaskId: task.inspectionTaskId! },
        query: {
          organizationId: organizationId.value,
          environmentId: environmentId.value,
          scopeKind: 'self',
          scopeId: inspectorUserId.value,
        },
        body: {
          idempotencyKey,
          expectedVersion: task.version,
        },
      }),
    )
    if (envelope.success !== true || !envelope.data) {
      throw new Error(envelope.message?.trim() || '领取检验任务失败，请重试。')
    }
    return {
      ...task,
      ...envelope.data,
      allowedActions: ['submit-inspection'],
      blockReasons: [],
    }
  }

  return {
    filters,
    tasks,
    total,
    loaded,
    hasMore,
    loadMore,
    ensureAllLoaded,
    pending: listQuery.isLoading,
    error: listQuery.error,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (scopeReady.value ? listQuery.refetch() : Promise.resolve()),
    reasonCodes,
    submitInspection,
    claimTask,
    claimPending: claimMutation.isLoading,
    submitPending: submitMutation.isLoading,
    scopeReady,
  }
}

/**
 * 选中任务后按其 `inspectionPlanId` 懒加载检验计划特性（MAN-457 反馈：检验特性「可选可搜」、
 * 单位「直接匹配特性」）。特性来自计划本身（code/name/类型 variable-attribute/公差/单位），
 * 因此录入端选到的特性码必然与计划匹配（提交不会漏特性），超差也用计划的权威公差判定。
 *
 * facade：`GET /quality/inspection-plans/{id}/characteristics`。planId 为空时不发请求。
 */
export function useInspectionPlanCharacteristics(planId: MaybeRefOrGetter<string | undefined>) {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const resolvedPlanId = computed(() => (toValue(planId) ?? '').trim())
  const enabled = computed(() =>
    Boolean(organizationId.value && environmentId.value && resolvedPlanId.value),
  )

  const query = useQuery(() => ({
    ...listBusinessConsoleQualityInspectionPlanCharacteristicsQueryOptions({
      path: { inspectionPlanId: resolvedPlanId.value },
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
      },
    }),
    enabled: enabled.value,
  }))

  const characteristics = computed<BusinessConsoleInspectionPlanCharacteristicItem[]>(() => {
    const envelope = query.data.value
    if (!envelope?.success) return []
    return envelope.data?.items ?? []
  })

  // 计划编号（人读，优于任务上携带的计划 GUID）；未加载时为空。
  const planCode = computed(() => {
    const envelope = query.data.value
    return envelope?.success ? (envelope.data?.planCode ?? '') : ''
  })

  return {
    characteristics,
    planCode,
    pending: query.isLoading,
    error: query.error,
    refresh: () => (enabled.value ? query.refetch() : Promise.resolve()),
  }
}
