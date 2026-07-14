import {
  createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions,
  listBusinessConsoleQualityInspectionPlanCharacteristicsQueryOptions,
  listBusinessConsoleQualityInspectionTasks,
  listBusinessConsoleQualityInspectionTasksQueryOptions,
  listBusinessConsoleQualityReasonCodesQueryOptions,
  type BusinessConsoleInspectionCharacteristicResult,
  type BusinessConsoleInspectionPlanCharacteristicItem,
  type BusinessConsoleQualityInspectionTaskItem,
  type BusinessConsoleQualityReasonItem,
} from '@nerv-iip/api-client'
import type { QualityCharacteristicResultLine as ResultLine } from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef, toValue, type MaybeRefOrGetter } from 'vue'

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

/**
 * 检验任务（待检工作台）读 + 逐特性录结果提交数据封装（MAN-457 / #811，与 console C3-1 / #801 同源）。
 *
 * - org/env 取登录主体 `useAuthStore().principal`（PDA 无 business-context store）；
 *   `inspectorUserId` = `principal.principalId`（写面必填的检验员身份，服务端审计操作人）。
 *   scope 空（未登录 / 缺 org/env）时列表不发请求（`enabled:false`）。
 * - 列表默认按 `status=pending` 服务端过滤；来源类型筛选、超期置顶排序、扫码直达均为
 *   **客户端**逻辑（facade 仅支持 org/env/status/skuCode/skip/take，无 sourceType/超期/关键字参数），
 *   因此当扫码/来源筛选命中第一页之外时以 `total` 判断是否可 `loadMore`，不把"页内未命中"当"不存在"。
 * - 提交端点 `CreateInspectionRecordFromTask` **按任务生命周期天然幂等**：任务已 completed 则
 *   返回同一 inspectionRecordId（first-write-wins keyed on task），故重试安全；权威 pass/fail 由
 *   后端按检验计划规格计算并在不合格时自动发起 NCR。请求体无幂等键字段，无需携带。
 */
export function useBusinessQualityInspectionTasks() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const inspectorUserId = computed(() => auth.principal?.principalId ?? '')
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))

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
        status: filters.status,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: scopeReady.value,
  }))

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

  const submitMutation = useMutation({
    ...createBusinessConsoleQualityInspectionRecordFromTaskMutationOptions(),
    onSuccess() {
      // 基础页失效重取；聚合补充页会 stale，一并丢弃（需要时再按页重聚合）。
      extraTasks.value = []
      void queryCache
        .invalidateQueries({ predicate: isInspectionTasksQuery })
        .catch(ignoreBackgroundError)
    },
  })

  const baseTasks = computed<BusinessConsoleQualityInspectionTaskItem[]>(() =>
    listItems<BusinessConsoleQualityInspectionTaskItem>(listQuery.data.value),
  )
  const tasks = computed<BusinessConsoleQualityInspectionTaskItem[]>(() => {
    if (extraTasks.value.length === 0) return baseTasks.value
    const seen = new Set(baseTasks.value.map((t) => t.inspectionTaskId))
    return [...baseTasks.value, ...extraTasks.value.filter((t) => !seen.has(t.inspectionTaskId))]
  })
  const total = computed(() => listTotal(listQuery.data.value))
  const loaded = computed(() => tasks.value.length)
  const hasMore = computed(() => loaded.value < total.value)

  /** 受限拉取一页（take 不超上限），返回该页 items；失败抛错由调用方处理。 */
  async function fetchPage(skip: number, take: number) {
    const { data } = await listBusinessConsoleQualityInspectionTasks({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        status: filters.status,
        skip,
        take: Math.min(Math.max(take, 1), MAX_TAKE),
      },
    })
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
    const page = await fetchPage(loaded.value, MAX_TAKE)
    if (page.length > 0) extraTasks.value = [...extraTasks.value, ...page]
  }

  /**
   * 加载全部待检任务后返回最新集合。扫码直达用：facade 无 sourceDocumentId/关键字服务端过滤，
   * 目标任务可能落在未加载分页；按 **受限分页迭代**（每页 ≤ MAX_TAKE）聚合覆盖全量再匹配——
   * 不把 take 直接扩到 total（超过后端验证器上限会整段失败）。
   */
  async function ensureAllLoaded() {
    if (!scopeReady.value) return tasks.value
    // 防御：空页即止（total 与实际漂移时不空转）。
    while (hasMore.value) {
      const page = await fetchPage(loaded.value, Math.min(MAX_TAKE, total.value - loaded.value))
      if (page.length === 0) break
      extraTasks.value = [...extraTasks.value, ...page]
    }
    return tasks.value
  }

  /**
   * 提交检验结果。`resultLines` 由 `@nerv-iip/business-core` 归一（业务口径），此处仅注入
   * org/env（query）与 `inspectorUserId`（body）——调用方不可覆盖检验员身份。
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
    return submitMutation.mutateAsync({
      path: { inspectionTaskId },
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
      },
      body: {
        inspectorUserId: inspectorUserId.value,
        // business-core 的行结构与 api-client `InspectionCharacteristicResult` 同形，直接透传。
        resultLines: resultLines as BusinessConsoleInspectionCharacteristicResult[],
        ...(reason ? { dispositionReason: reason } : {}),
      },
    })
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
    refresh: () => (scopeReady.value ? listQuery.refetch() : Promise.resolve()),
    reasonCodes,
    submitInspection,
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
