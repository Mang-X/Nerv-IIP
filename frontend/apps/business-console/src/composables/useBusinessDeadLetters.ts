import {
  getBusinessConsoleDeadLetterMetricsQueryOptions,
  getBusinessConsoleDeadLetterQueryOptions,
  ignoreBusinessConsoleDeadLetterMutationOptions,
  listBusinessConsoleDeadLettersQueryOptions,
  replayBusinessConsoleDeadLetterMutationOptions,
  type BusinessConsoleDeadLetterDetailEnvelope,
  type BusinessConsoleDeadLetterItem,
  type BusinessConsoleDeadLetterListEnvelope,
  type BusinessConsoleDeadLetterMetricsEnvelope,
  type BusinessConsoleDeadLetterReplayEnvelope,
  type BusinessConsoleDeadLetterServiceMetrics,
  type BusinessConsoleDeadLetterSourceFailureReason,
  type BusinessConsoleDeadLetterSourceStatus,
  type IntegrationEventDeadLetterDetailResponse,
  type IntegrationEventDeadLetterReplayStatus,
  type IntegrationEventDeadLetterStatus,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, ref } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { errorStatusCode } from '@/utils/notify'
import { hasBusinessContext } from './businessContextBinding'

/**
 * 逐来源取数上限。扇出没有跨服务游标（见网关 facade 的 `Take` 注释），
 * 这个数是**每个来源**的上限，不是全局条数。
 */
const TAKE_PER_SOURCE = 200

const DEAD_LETTER_QUERY_IDS = [
  'listBusinessConsoleDeadLetters',
  'getBusinessConsoleDeadLetterMetrics',
  'getBusinessConsoleDeadLetter',
]

/** 「不限」的取值。用哨兵而不是空串：reka 的 Select 把空串保留给「清空选择」。 */
export const DEAD_LETTER_FILTER_ALL = 'all'

export interface DeadLetterFilters {
  /** 服务名，或 `DEAD_LETTER_FILTER_ALL` 表示扇出全部来源。 */
  service: string
  eventType: string
  status: typeof DEAD_LETTER_FILTER_ALL | IntegrationEventDeadLetterStatus
}

/** 服务端对一次重放的答复。`status` 是 wire 受控枚举，只能取自服务端响应。 */
export interface DeadLetterReplayOutcome {
  status: IntegrationEventDeadLetterReplayStatus
  succeeded: boolean
  message?: string
}

/**
 * 一行最近一次重放在屏上的结果。
 *
 * - `answered: true`：收到了服务端答复，结果照受控枚举 `IntegrationEventDeadLetterReplayStatus`；
 * - `answered: false`：**没收到服务端答复**（网络中断、网关 5xx……），服务端做没做不得而知。
 *
 * 「未知」**只是前端展示态**，刻意不进 wire 枚举，也不写成 `failed`：服务端永远不会返回它，
 * 把它塞进受控枚举就是伪造一个服务端取值。它的产出方只有 `replay()` 的 catch 这一处。
 */
export type DeadLetterReplayResult =
  | { answered: true; outcome: DeadLetterReplayOutcome }
  | {
      answered: false
      /**
       * 发起这次重放时该行的**行状态**（`IntegrationEventDeadLetterStatus`，不是重放结果枚举）。
       * 让位判据看的是「状态是否发生了转移」，所以必须记下转移之前的值——只看当前值，
       * 点击前就是「重放失败」的行会被当成「服务端已给出确定结果」而错误让位（N1）。
       */
      statusAtAttempt: IntegrationEventDeadLetterStatus | undefined
    }

/** 一行死信的定位坐标：id 只在它自己的服务内唯一。 */
export interface DeadLetterCoordinate {
  service: string
  deadLetterId: string
}

/** 一次重放的目标：坐标 + 发起时的行状态（让位判据要用）。 */
export interface DeadLetterTarget extends DeadLetterCoordinate {
  /**
   * 调用方看到的该行当前状态。由调用方传入、而不是在这里按 rowKey 回查列表：
   * 回查会让 `replay()` 隐式依赖列表缓存的读取时机。
   */
  statusAtAttempt: IntegrationEventDeadLetterStatus | undefined
}

/**
 * 整批里一行的归类，三类互斥且穷尽：
 * - `answered`：收到服务端答复（行上按受控枚举记录）；
 * - `unanswered`：未收到答复（行上记「未知」）；
 * - `rejected`：网关明确拒绝（4xx）——**确定没有重放**，行上不写记录，
 *   改为批次结束后**保留选中**，让运维能直接再点一次。当前链路上可由界面触发的 4xx 是 429 限流；
 *   401 会先跳登录、离开本页；403 只在会话中途被撤权时出现，按边界不追，不为它单设分支。
 *
 * 汇总 toast 的计数与保留选中都从这一份列表派生，不另记计数。
 */
export type DeadLetterBatchRowResult =
  | { rowKey: string; kind: 'answered'; outcome: DeadLetterReplayOutcome }
  | { rowKey: string; kind: 'unanswered' | 'rejected'; error: unknown }

export interface DeadLetterUnavailableSource {
  service: string
  reason?: BusinessConsoleDeadLetterSourceFailureReason
}

/** 行在表格里的稳定键：死信 id 只在**它自己的服务**内唯一，跨来源合并后必须带上服务名。 */
export function deadLetterRowKey(service: string, deadLetterId: string) {
  return `${service}/${deadLetterId}`
}

export function useBusinessDeadLetters() {
  const businessContext = useBusinessContextStore()
  const queryCache = useQueryCache()

  const filters = reactive<DeadLetterFilters>({
    service: DEAD_LETTER_FILTER_ALL,
    eventType: '',
    status: DEAD_LETTER_FILTER_ALL,
  })
  const selectedRowKey = ref('')
  const selectedRowKeys = ref<string[]>([])
  /**
   * 每行最近一次重放的结果，按行键保存。
   *
   * 为什么不能只看刷新后行上的 `status`：`noHandler`（该服务没有这个事件的重放处理器）与
   * `notFound` 都**不会**改写死信行——服务端正确地不去伪造一个「试过了」的状态。只看行状态，
   * 这两种结果和「还没点过」完全无法区分，操作者会反复点一个必然无效的按钮（#3740 承接
   * 自 PR #3742 第 2 轮审核的登记项）。「未收到答复」同理，也不会出现在行状态上。
   */
  const replayResults = reactive(new Map<string, DeadLetterReplayResult>())

  const contextReady = computed(() => hasBusinessContext(businessContext))

  const scopeQuery = computed(() => ({
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }))

  const serviceFilter = computed(() =>
    filters.service === DEAD_LETTER_FILTER_ALL ? undefined : optionalText(filters.service),
  )
  const statusFilter = computed(() =>
    filters.status === DEAD_LETTER_FILTER_ALL ? undefined : filters.status,
  )

  const listQuery = useQuery(() => ({
    ...listBusinessConsoleDeadLettersQueryOptions({
      query: {
        ...scopeQuery.value,
        service: serviceFilter.value,
        eventType: optionalText(filters.eventType),
        status: statusFilter.value,
        take: TAKE_PER_SOURCE,
      },
    }),
    enabled: contextReady.value,
  }))

  /**
   * 概览**始终扇出全部来源**，不跟随 service 筛选。
   *
   * 这一条同时解决两件事：
   * 1. 「有哪些服务可选」是一个与当次筛选无关的事实，产出方是网关扇出的来源表
   *    （`IntegrationEventDeadLetterServices.All`）。网关在 `service` 非空时只扇出那一个来源
   *    （`BusinessConsoleDeadLetterService.FanOutAsync`），所以带筛选的响应**不能**充当服务清单：
   *    选中某个服务后下拉会塌缩成只剩它自己，用户再也切不到别的服务。
   * 2. 概览的语义本就是「全局还积压多少」，筛选中的列表不应把它一起缩掉——
   *    否则筛到一个干净的服务时，屏上会显示全局积压为 0。
   */
  const metricsQuery = useQuery(() => ({
    ...getBusinessConsoleDeadLetterMetricsQueryOptions({
      query: scopeQuery.value,
    }),
    enabled: contextReady.value,
  }))

  const detailQuery = useQuery(() => {
    const selected = parseRowKey(selectedRowKey.value)
    return {
      ...getBusinessConsoleDeadLetterQueryOptions({
        path: {
          service: selected?.service ?? '',
          deadLetterId: selected?.deadLetterId ?? '',
        },
        query: scopeQuery.value,
      }),
      enabled: contextReady.value && selected !== undefined,
    }
  })

  const replayMutation = useMutation(replayBusinessConsoleDeadLetterMutationOptions())
  const ignoreMutation = useMutation(ignoreBusinessConsoleDeadLetterMutationOptions())

  const listEnvelope = computed(
    () => listQuery.data.value as BusinessConsoleDeadLetterListEnvelope | undefined,
  )
  const metricsEnvelope = computed(
    () => metricsQuery.data.value as BusinessConsoleDeadLetterMetricsEnvelope | undefined,
  )

  const items = computed<BusinessConsoleDeadLetterItem[]>(
    () => unwrapData(listEnvelope.value)?.items ?? [],
  )
  const metrics = computed(() => unwrapData(metricsEnvelope.value))
  const serviceMetrics = computed<BusinessConsoleDeadLetterServiceMetrics[]>(
    () => metrics.value?.services ?? [],
  )
  const selectedDeadLetter = computed<IntegrationEventDeadLetterDetailResponse | undefined>(() =>
    unwrapData(detailQuery.data.value as BusinessConsoleDeadLetterDetailEnvelope | undefined),
  )
  /** 当前选中行的坐标。行键的格式由本文件拥有，调用方不再各自拆一遍。 */
  const selectedTarget = computed<DeadLetterCoordinate | undefined>(() =>
    parseRowKey(selectedRowKey.value),
  )

  /**
   * 本次**没答上来**的来源。列表与计数各自报一份逐源状态，这里取并集：
   * 只看其中一份会出现「表格提示了、概览卡没提示」这种半边真相。
   */
  const unavailableSources = computed<DeadLetterUnavailableSource[]>(() => {
    const merged = new Map<string, DeadLetterUnavailableSource>()
    for (const status of [
      ...(unwrapData(listEnvelope.value)?.sourceStatuses ?? []),
      ...(metrics.value?.sourceStatuses ?? []),
    ]) {
      if (status.status !== 'unavailable' || !status.service) continue
      if (!merged.has(status.service)) {
        merged.set(status.service, { service: status.service, reason: status.reason ?? undefined })
      }
    }
    return [...merged.values()].sort((a, b) => a.service.localeCompare(b.service))
  })

  const hasUnavailableSource = computed(() => unavailableSources.value.length > 0)

  /** 当前筛选的那个服务本身没答上来——「读不到」与「它是干净的」必须分开说。 */
  const filteredServiceUnavailable = computed(() => {
    const service = serviceFilter.value
    if (!service) return false
    return unavailableSources.value.some((source) => source.service === service)
  })

  /**
   * 可选服务清单。**只取自不带筛选的概览响应**——列表响应会随 service 筛选收缩到一条，
   * 拿它当清单会让下拉在选中后塌缩。这里不缓存上一次的结果：那只是让两份副本相等，
   * 真正的产出方是网关那张来源表。
   */
  const availableServices = computed(() =>
    (metrics.value?.sourceStatuses ?? [])
      .flatMap((status) => (status.service ? [status.service] : []))
      .sort((a, b) => a.localeCompare(b)),
  )

  async function refresh() {
    await Promise.all([
      listQuery.refetch(),
      metricsQuery.refetch(),
      selectedRowKey.value ? detailQuery.refetch() : Promise.resolve(),
    ])
  }

  function invalidateDeadLetters() {
    return queryCache.invalidateQueries({ predicate: isDeadLetterQuery })
  }

  /**
   * 重放一行，返回这次的结果。
   *
   * 逐行调单条重放、而不是调 `replay-batch`：批量端点按**筛选条件**重放（服务 + 条件 + 上限），
   * 表达不了「就这几行」。用它来兑现复选框会连用户没勾的行一起重放。
   */
  async function replay({ service, deadLetterId, statusAtAttempt }: DeadLetterTarget) {
    const rowKey = deadLetterRowKey(service, deadLetterId)
    let envelope: BusinessConsoleDeadLetterReplayEnvelope
    try {
      envelope = (await replayMutation.mutateAsync({
        path: { service, deadLetterId },
        query: scopeQuery.value,
      })) as BusinessConsoleDeadLetterReplayEnvelope
    } catch (error) {
      // 单条与整批共用这一处，两条路径因此同形：谁也不会漏记、也不会一个记一个不记。
      if (isReplayUnanswered(error)) {
        replayResults.set(rowKey, { answered: false, statusAtAttempt })
      }
      throw error
    }
    const result = unwrapData(envelope)
    const outcome: DeadLetterReplayOutcome = {
      status: result?.status ?? 'failed',
      succeeded: result?.succeeded ?? false,
      message: result?.message ?? undefined,
    }
    replayResults.set(rowKey, { answered: true, outcome })
    return outcome
  }

  /**
   * 依次重放给定的行，逐行记录结果并返回汇总。
   * 串行是为了不对 10 个下游同时放大流量。
   *
   * **一行失败不中断整批**：中断会留下「前几行后端已经重放、而列表与计数不刷新」的半应用状态。
   *
   * 批次不变量：所选的每一行，批次结束后**要么有「重放结果」列的记录，要么仍在选中状态**。
   * `answered` / `unanswered` 由 `replay()` 写行上记录；`rejected` 没有记录，所以保留选中。
   * 错误原文随每行带回，由调用方透传上屏，不吞。
   */
  async function replaySelected(rows: DeadLetterTarget[]) {
    const results: DeadLetterBatchRowResult[] = []
    try {
      for (const target of rows) {
        const rowKey = deadLetterRowKey(target.service, target.deadLetterId)
        try {
          results.push({ rowKey, kind: 'answered', outcome: await replay(target) })
        } catch (error) {
          // 与 replay() 写行上记录用的是同一个判定，两边的分类因此不会分叉。
          results.push({
            rowKey,
            kind: isReplayUnanswered(error) ? 'unanswered' : 'rejected',
            error,
          })
        }
      }
    } finally {
      await invalidateDeadLetters()
    }
    selectedRowKeys.value = results.filter((r) => r.kind === 'rejected').map((r) => r.rowKey)
    return results
  }

  async function replayOne(target: DeadLetterTarget) {
    try {
      return await replay(target)
    } finally {
      // 与整批同一条不变量：无论这次成不成，列表与计数都要回到服务端的说法。
      await invalidateDeadLetters()
    }
  }

  async function ignore(service: string, deadLetterId: string, reason: string) {
    const result = unwrapData(
      (await ignoreMutation.mutateAsync({
        body: { ...scopeQuery.value, reason: reason.trim() },
        path: { service, deadLetterId },
      })) as BusinessConsoleDeadLetterDetailEnvelope,
    )
    await invalidateDeadLetters()
    return result
  }

  return {
    availableServices,
    contextReady,
    filteredServiceUnavailable,
    filters,
    hasUnavailableSource,
    ignore,
    ignorePending: ignoreMutation.isLoading,
    items,
    listError: listQuery.error,
    listPending: listQuery.isLoading,
    metrics,
    metricsError: metricsQuery.error,
    metricsPending: metricsQuery.isLoading,
    refresh,
    replayOne,
    replayResults,
    replayPending: replayMutation.isLoading,
    replaySelected,
    selectedDeadLetter,
    detailError: detailQuery.error,
    detailPending: detailQuery.isLoading,
    selectedRowKey,
    selectedRowKeys,
    selectedTarget,
    serviceMetrics,
    unavailableSources,
  }
}

/**
 * 这次失败是不是「没收到服务端答复」。
 *
 * - 没有 HTTP 状态码（网络中断、请求没发出去/没回来）⇒ 未知；
 * - 5xx ⇒ 未知：网关在下游**已经重放之后**才出错是可达的（例如下游成功、网关返 502），
 *   此时说「失败」就是在替服务端下结论；
 * - 4xx ⇒ **不是**未知：网关收到并拒绝了这次请求（无权限、服务名不认识、校验不过），
 *   可以确定没有重放。把它写成「未知」是反方向的同一种错——把已知说成未知。
 */
export function isReplayUnanswered(error: unknown) {
  const status = errorStatusCode(error)
  return status === undefined || status >= 500
}

function parseRowKey(rowKey: string) {
  const separator = rowKey.indexOf('/')
  if (separator <= 0 || separator === rowKey.length - 1) return undefined
  return {
    service: rowKey.slice(0, separator),
    deadLetterId: rowKey.slice(separator + 1),
  }
}

function isDeadLetterQuery(entry: UseQueryEntry) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
  return keyParts.some(
    (part) =>
      typeof part === 'object' &&
      part !== null &&
      '_id' in part &&
      DEAD_LETTER_QUERY_IDS.includes(String(part._id)),
  )
}

function unwrapData<T>(
  envelope: { success?: boolean; data?: T | null } | undefined,
): T | undefined {
  return envelope?.success ? (envelope.data ?? undefined) : undefined
}

function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
