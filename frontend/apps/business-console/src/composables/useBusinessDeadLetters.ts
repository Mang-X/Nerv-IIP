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

/** 一次重放的结果。行本身的 `status` 不足以表达它——见 `replayOutcomes`。 */
export interface DeadLetterReplayOutcome {
  status: IntegrationEventDeadLetterReplayStatus
  succeeded: boolean
  message?: string
}

/** 一行死信的定位坐标：id 只在它自己的服务内唯一。 */
export interface DeadLetterTarget {
  service: string
  deadLetterId: string
}

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
   * 自 PR #3742 第 2 轮审核的登记项）。
   */
  const replayOutcomes = reactive(new Map<string, DeadLetterReplayOutcome>())

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

  // 计数与列表使用同一个 service 口径：两边范围不一致时，概览卡说的就不是列表在说的那件事。
  const metricsQuery = useQuery(() => ({
    ...getBusinessConsoleDeadLetterMetricsQueryOptions({
      query: { ...scopeQuery.value, service: serviceFilter.value },
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
  const selectedTarget = computed<DeadLetterTarget | undefined>(() =>
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

  const availableServices = computed(() =>
    [
      ...new Set([
        ...(unwrapData(listEnvelope.value)?.sourceStatuses ?? []).flatMap((status) =>
          status.service ? [status.service] : [],
        ),
        ...(metrics.value?.sourceStatuses ?? []).flatMap((status) =>
          status.service ? [status.service] : [],
        ),
      ]),
    ].sort((a, b) => a.localeCompare(b)),
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
  async function replay(service: string, deadLetterId: string) {
    const result = unwrapData(
      (await replayMutation.mutateAsync({
        path: { service, deadLetterId },
        query: scopeQuery.value,
      })) as BusinessConsoleDeadLetterReplayEnvelope,
    )
    const outcome: DeadLetterReplayOutcome = {
      status: result?.status ?? 'failed',
      succeeded: result?.succeeded ?? false,
      message: result?.message ?? undefined,
    }
    replayOutcomes.set(deadLetterRowKey(service, deadLetterId), outcome)
    return outcome
  }

  /**
   * 依次重放给定的行，逐行记录结果并返回汇总。
   * 串行是为了让每一行的结果都能单独归位，也不对 10 个下游同时放大流量。
   */
  async function replaySelected(rows: DeadLetterTarget[]) {
    const outcomes: Array<{ rowKey: string; outcome: DeadLetterReplayOutcome }> = []
    for (const { service, deadLetterId } of rows) {
      outcomes.push({
        rowKey: deadLetterRowKey(service, deadLetterId),
        outcome: await replay(service, deadLetterId),
      })
    }
    await invalidateDeadLetters()
    return outcomes
  }

  async function replayOne(service: string, deadLetterId: string) {
    const outcome = await replay(service, deadLetterId)
    await invalidateDeadLetters()
    return outcome
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
    replayOutcomes,
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
