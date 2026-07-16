import {
  createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions,
  getBusinessConsoleTelemetryTagCurrentValueQueryOptions,
  listBusinessConsoleTelemetryAlarmRulesQueryOptions,
  listBusinessConsoleTelemetryConnectorCollectionHealthQueryOptions,
  listBusinessConsoleTelemetryTagsQueryOptions,
  queryBusinessConsoleTelemetryDeviceHistoryQueryOptions,
  queryBusinessConsoleTelemetryOeeQueryOptions,
  queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions,
  queryBusinessConsoleTelemetryRuntimeHours,
  queryBusinessConsoleTelemetryRuntimeHoursQueryOptions,
  type BusinessConsoleConnectorCollectionHealthListEnvelope,
  type BusinessConsoleConnectorCollectionHealthListItem,
  type BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest,
  type BusinessConsoleTelemetryRuntimeHoursEnvelope,
  type BusinessConsoleTelemetryRuntimeHoursResponse,
  type BusinessConsoleTelemetryTagCurrentValueEnvelope,
  type BusinessConsoleTelemetryTagCurrentValueResponse,
  type BusinessConsoleTelemetryAlarmRuleItem,
  type BusinessConsoleTelemetryAlarmRuleListEnvelope,
  type BusinessConsoleTelemetryHistoryEnvelope,
  type BusinessConsoleTelemetryHistoryItem,
  type BusinessConsoleTelemetryOeeEnvelope,
  type BusinessConsoleTelemetryOeeResponse,
  type BusinessConsoleTelemetryTagItem,
  type BusinessConsoleTelemetryTagListEnvelope,
  type EquipmentRuntimeAvailabilityEnvelope,
  type EquipmentRuntimeAvailabilityWindow,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive, ref, watch, type Ref } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext, refetchWithBusinessContext } from './businessContextBinding'

const DEFAULT_TAKE = 100

export type TelemetryEnabledFilter = 'all' | 'enabled' | 'disabled'

export interface TelemetryListFilters {
  deviceAssetId: string
  isEnabled: TelemetryEnabledFilter
  skip: number
  take: number
}

export interface TelemetryWindowFilters {
  deviceAssetId: string
  tagKey: string
  windowStartUtc: string
  windowEndUtc: string
}

export type SaveTelemetryAlarmRuleInput = Omit<
  BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest,
  'organizationId' | 'environmentId'
>

function defaultListFilters(initial: Partial<TelemetryListFilters> = {}): TelemetryListFilters {
  return reactive({
    deviceAssetId: '',
    isEnabled: 'all',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  })
}

function defaultWindowFilters(
  initial: Partial<TelemetryWindowFilters> = {},
): TelemetryWindowFilters {
  const end = new Date()
  const start = new Date(end)
  start.setHours(start.getHours() - 8)

  return reactive({
    deviceAssetId: '',
    tagKey: '',
    windowStartUtc: start.toISOString(),
    windowEndUtc: end.toISOString(),
    ...initial,
  })
}

function trimOptional(value: string | null | undefined) {
  const normalized = value?.trim() ?? ''
  return normalized.length > 0 ? normalized : undefined
}

function enabledQuery(value: TelemetryEnabledFilter) {
  if (value === 'enabled') return true
  if (value === 'disabled') return false
  return undefined
}

function toContextQuery(businessContext: ReturnType<typeof useBusinessContextStore>) {
  return {
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }
}

function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

function listTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined) {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

function unwrapData<TData>(envelope: { success?: boolean; data?: TData | null } | undefined) {
  return envelope?.success ? (envelope.data ?? undefined) : undefined
}

export function formatOeeRate(value: number | null | undefined) {
  if (value === null || value === undefined) return '无数据'
  return `${(value * 100).toFixed(1)}%`
}

export function formatOeeQuantity(value: number | null | undefined, uomCode?: string | null) {
  if (value === null || value === undefined) return '无数据'
  return `${value.toLocaleString(undefined, { maximumFractionDigits: 3 })}${uomCode ? ` ${uomCode}` : ''}`
}

export function describeTelemetryOeeLimitations() {
  return 'OEE = 可用率 × 性能率 × 质量率。性能率使用 MES 报工总产出与工序标准速率计算，质量率使用良品 ÷（良品 + 报废 + 返工）计算；任一来源不足时明确标记为数据不完整，不以 1 替代。'
}

export function describeTelemetryOeeDegradation(reason: string) {
  const labels: Record<string, string> = {
    'runtime-state-facts-missing': '缺少设备运行状态事实',
    'production-facts-missing': '缺少 MES 报工事实',
    'production-uom-ambiguous': '报工单位不一致，无法合并',
    'production-output-missing': '报工总产出不为正，无法计算质量率',
    'theoretical-rate-missing-or-ambiguous': '缺少或存在冲突的工序标准速率',
    'productive-runtime-missing': '当前窗口没有有效的生产运行时长',
  }
  return labels[reason] ?? reason
}

export function useBusinessTelemetryTags(initialFilters: Partial<TelemetryListFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultListFilters(initialFilters)
  const tagsQuery = useQuery(() => ({
    ...listBusinessConsoleTelemetryTagsQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: trimOptional(filters.deviceAssetId),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))

  return {
    filters,
    refreshTags: () => refetchWithBusinessContext(businessContext, tagsQuery),
    tags: computed<BusinessConsoleTelemetryTagItem[]>(() =>
      listItems<BusinessConsoleTelemetryTagItem>(
        tagsQuery.data.value as BusinessConsoleTelemetryTagListEnvelope | undefined,
      ),
    ),
    tagsError: tagsQuery.error,
    tagsPending: tagsQuery.isLoading,
    tagsTotal: computed(() =>
      listTotal(tagsQuery.data.value as BusinessConsoleTelemetryTagListEnvelope | undefined),
    ),
  }
}

export function useBusinessTelemetryAlarmRules(initialFilters: Partial<TelemetryListFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultListFilters(initialFilters)
  const alarmRulesQuery = useQuery(() => ({
    ...listBusinessConsoleTelemetryAlarmRulesQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: trimOptional(filters.deviceAssetId),
        isEnabled: enabledQuery(filters.isEnabled),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))
  const saveMutation = useMutation({
    ...createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(businessContext, alarmRulesQuery)
    },
  })

  return {
    alarmRules: computed<BusinessConsoleTelemetryAlarmRuleItem[]>(() =>
      listItems<BusinessConsoleTelemetryAlarmRuleItem>(
        alarmRulesQuery.data.value as BusinessConsoleTelemetryAlarmRuleListEnvelope | undefined,
      ),
    ),
    alarmRulesError: alarmRulesQuery.error,
    alarmRulesPending: alarmRulesQuery.isLoading,
    alarmRulesTotal: computed(() =>
      listTotal(
        alarmRulesQuery.data.value as BusinessConsoleTelemetryAlarmRuleListEnvelope | undefined,
      ),
    ),
    filters,
    refreshAlarmRules: () => refetchWithBusinessContext(businessContext, alarmRulesQuery),
    saveAlarmRule: (input: SaveTelemetryAlarmRuleInput) =>
      saveMutation.mutateAsync({
        body: {
          ...input,
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
        },
      }),
    saveAlarmRuleError: saveMutation.error,
    saveAlarmRulePending: saveMutation.isLoading,
  }
}

/**
 * 某设备某 tag 的当前值（最新原始采样 LastValue）。用于设备控制写值表单展示真实当前值，区别于历史
 * 曲线的 bucket 均值。tagKey 为空时不发请求。
 */
export function useBusinessTelemetryTagCurrentValue(
  deviceAssetId: Ref<string>,
  tagKey: Ref<string>,
) {
  const businessContext = useBusinessContextStore()
  const enabled = computed(
    () =>
      hasBusinessContext(businessContext) &&
      deviceAssetId.value.trim().length > 0 &&
      tagKey.value.trim().length > 0,
  )
  const currentValueQuery = useQuery(() => ({
    ...getBusinessConsoleTelemetryTagCurrentValueQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: deviceAssetId.value,
        tagKey: tagKey.value,
      },
    }),
    enabled: enabled.value,
  }))
  const currentValue = computed<BusinessConsoleTelemetryTagCurrentValueResponse | undefined>(() =>
    unwrapData<BusinessConsoleTelemetryTagCurrentValueResponse>(
      currentValueQuery.data.value as BusinessConsoleTelemetryTagCurrentValueEnvelope | undefined,
    ),
  )

  return {
    currentValue,
    currentValueError: currentValueQuery.error,
    currentValuePending: currentValueQuery.isLoading,
    refreshCurrentValue: () => (enabled.value ? currentValueQuery.refetch() : Promise.resolve()),
  }
}

export function useBusinessTelemetryHistory(initialFilters: Partial<TelemetryWindowFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultWindowFilters(initialFilters)
  const deviceAssetId = computed(() => filters.deviceAssetId.trim())
  const historyEnabled = computed(
    () => hasBusinessContext(businessContext) && deviceAssetId.value.length > 0,
  )
  const historyQuery = useQuery(() => ({
    ...queryBusinessConsoleTelemetryDeviceHistoryQueryOptions({
      path: { deviceAssetId: deviceAssetId.value },
      query: {
        ...toContextQuery(businessContext),
        fromUtc: trimOptional(filters.windowStartUtc),
        toUtc: trimOptional(filters.windowEndUtc),
      },
    }),
    enabled: historyEnabled.value,
  }))
  const historyItems = computed<BusinessConsoleTelemetryHistoryItem[]>(() =>
    listItems<BusinessConsoleTelemetryHistoryItem>(
      historyQuery.data.value as BusinessConsoleTelemetryHistoryEnvelope | undefined,
    ),
  )
  const visibleHistoryItems = computed(() => {
    const tagKey = filters.tagKey.trim()
    if (!tagKey) return historyItems.value
    // History facade currently accepts device/time only; tag filtering stays local until tag/paging query params exist.
    return historyItems.value.filter((item) => item.tagKey === tagKey)
  })

  return {
    filters,
    historyError: historyQuery.error,
    historyItems,
    historyPending: historyQuery.isLoading,
    refreshHistory: () => (historyEnabled.value ? historyQuery.refetch() : Promise.resolve()),
    visibleHistoryItems,
  }
}

export function useBusinessTelemetryOee(initialFilters: Partial<TelemetryWindowFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultWindowFilters(initialFilters)
  const deviceAssetId = computed(() => filters.deviceAssetId.trim())
  const oeeEnabled = computed(
    () => hasBusinessContext(businessContext) && deviceAssetId.value.length > 0,
  )
  const oeeQuery = useQuery(() => ({
    ...queryBusinessConsoleTelemetryOeeQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: deviceAssetId.value,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
      },
    }),
    enabled: oeeEnabled.value,
  }))
  const runtimeAvailabilityQuery = useQuery(() => ({
    ...queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetIds: deviceAssetId.value || undefined,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
      },
    }),
    enabled: oeeEnabled.value,
  }))

  return {
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(() =>
      listItems<EquipmentRuntimeAvailabilityWindow>(
        runtimeAvailabilityQuery.data.value as EquipmentRuntimeAvailabilityEnvelope | undefined,
      ),
    ),
    filters,
    oee: computed<BusinessConsoleTelemetryOeeResponse | undefined>(() =>
      unwrapData<BusinessConsoleTelemetryOeeResponse>(
        oeeQuery.data.value as BusinessConsoleTelemetryOeeEnvelope | undefined,
      ),
    ),
    oeeError: oeeQuery.error,
    oeePending: computed(
      () => oeeQuery.isLoading.value || runtimeAvailabilityQuery.isLoading.value,
    ),
    refreshOee: () =>
      oeeEnabled.value
        ? Promise.all([oeeQuery.refetch(), runtimeAvailabilityQuery.refetch()])
        : Promise.resolve(),
    runtimeAvailabilityError: runtimeAvailabilityQuery.error,
  }
}

/**
 * 设备累计运行小时（IIoT #688/#884 facade）。返回值是 [windowStartUtc, windowEndUtc] 窗口内累计，不是
 * 终身表底，调用方须按窗口口径展示。窗口起点对齐运行小时型保养计划的起算日时，`totalRuntimeHours`
 * 即等于计划推算所用的累计口径，可与计划 `nextDueRuntimeHours` 相减得到「距下次保养还需 X 小时」。
 * org/env + 设备 + 窗口齐备才发请求，缺任一即静默空态。
 */
export function useBusinessTelemetryRuntimeHours(
  deviceAssetId: Ref<string>,
  windowStartUtc: Ref<string>,
  windowEndUtc: Ref<string>,
) {
  const businessContext = useBusinessContextStore()
  const enabled = computed(
    () =>
      hasBusinessContext(businessContext) &&
      deviceAssetId.value.trim().length > 0 &&
      windowStartUtc.value.trim().length > 0 &&
      windowEndUtc.value.trim().length > 0,
  )
  const runtimeHoursQuery = useQuery(() => ({
    ...queryBusinessConsoleTelemetryRuntimeHoursQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: deviceAssetId.value.trim(),
        windowStartUtc: windowStartUtc.value,
        windowEndUtc: windowEndUtc.value,
      },
    }),
    enabled: enabled.value,
  }))
  const runtimeHours = computed<BusinessConsoleTelemetryRuntimeHoursResponse | undefined>(() =>
    unwrapData<BusinessConsoleTelemetryRuntimeHoursResponse>(
      runtimeHoursQuery.data.value as BusinessConsoleTelemetryRuntimeHoursEnvelope | undefined,
    ),
  )

  return {
    runtimeHours,
    totalRuntimeHours: computed(() => runtimeHours.value?.totalRuntimeHours ?? null),
    hasRuntimeSamples: computed(() => runtimeHours.value?.hasRuntimeSamples ?? false),
    runtimeHoursError: runtimeHoursQuery.error,
    runtimeHoursPending: runtimeHoursQuery.isLoading,
    runtimeHoursEnabled: enabled,
    refreshRuntimeHours: () => (enabled.value ? runtimeHoursQuery.refetch() : Promise.resolve()),
  }
}

/** Minimal shape needed to derive remaining runtime hours from a maintenance plan list row. */
export interface RuntimeRemainingPlan {
  planId?: string
  deviceAssetId?: string
  startsOn?: string
  runtimeHourInterval?: number | null
  nextDueRuntimeHours?: number | null
}

/**
 * Per-plan remaining-runtime-hours outcome. `error` (telemetry read failed) is kept distinct from
 * `no-samples` (facade responded, but the device has no real runtime samples yet) so the UI never
 * mislabels a transient failure as "no samples".
 */
export type RuntimeRemainingEntry =
  | { status: 'ok'; hours: number }
  | { status: 'no-samples' }
  | { status: 'error' }

// Bound the client-side telemetry fan-out: a full page of runtime plans issues at most this many
// concurrent runtime-hours reads, so the derivation never turns into a 100-way burst against the Gateway.
const RUNTIME_REMAINING_CONCURRENCY = 6

async function mapWithConcurrency<T, R>(
  items: readonly T[],
  limit: number,
  fn: (item: T) => Promise<R>,
  shouldStop?: () => boolean,
): Promise<R[]> {
  const results = new Array<R>(items.length)
  let cursor = 0
  async function worker() {
    // Stop pulling new work once superseded, so overlapping rounds never keep `limit` workers each
    // running — the stale round drains instead of adding to the global concurrency.
    while (cursor < items.length && !shouldStop?.()) {
      const index = cursor++
      results[index] = await fn(items[index])
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, () => worker()))
  return results
}

/**
 * Derives per-plan remaining runtime hours on the client from the runtime-hours read facade — one call
 * per runtime-hour plan over that plan's own [startsOn, now] window, so each plan uses its own accrual
 * basis (windows are never shared). The maintenance plan list query stays a fast DB projection; this
 * keeps the telemetry fan-out on the client where it is bounded (see RUNTIME_REMAINING_CONCURRENCY) and
 * non-blocking. remaining = nextDueRuntimeHours − accumulated runtime. The result recomputes whenever a
 * plan's next runtime threshold advances (e.g. after generating due work orders), not only when the set
 * of plan ids changes, so a stale remaining is never shown after the threshold moves.
 */
export function useMaintenancePlanRuntimeRemaining(plans: Ref<RuntimeRemainingPlan[]>) {
  const businessContext = useBusinessContextStore()
  const remainingByPlanId = ref<Record<string, RuntimeRemainingEntry>>({})
  const pending = ref(false)
  // Guards against overlapping rounds (watch fires, manual refresh, paging / org-env switch): only the
  // latest generation may commit results or clear pending, and the previous round's in-flight reads are
  // aborted so overlapping rounds never exceed the concurrency cap.
  let generation = 0
  let inFlight: AbortController | undefined

  async function recompute() {
    const myGeneration = ++generation
    inFlight?.abort()
    const controller = new AbortController()
    inFlight = controller
    const isCurrent = () => myGeneration === generation

    if (!hasBusinessContext(businessContext)) {
      if (isCurrent()) remainingByPlanId.value = {}
      return
    }
    const runtimePlans = plans.value.filter(
      (p) =>
        !!p.planId &&
        !!p.deviceAssetId &&
        !!p.startsOn &&
        p.runtimeHourInterval != null &&
        p.nextDueRuntimeHours != null,
    )
    if (runtimePlans.length === 0) {
      if (isCurrent()) remainingByPlanId.value = {}
      return
    }

    pending.value = true
    const nowUtc = new Date().toISOString()
    const organizationId = businessContext.organizationId
    const environmentId = businessContext.environmentId
    try {
      const entries = await mapWithConcurrency(
        runtimePlans,
        RUNTIME_REMAINING_CONCURRENCY,
        async (p): Promise<readonly [string, RuntimeRemainingEntry]> => {
          try {
            const result = await queryBusinessConsoleTelemetryRuntimeHours({
              query: {
                organizationId,
                environmentId,
                deviceAssetId: p.deviceAssetId!,
                windowStartUtc: `${p.startsOn}T00:00:00.000Z`,
                windowEndUtc: nowUtc,
              },
              signal: controller.signal,
            })
            if (result.error) return [p.planId!, { status: 'error' }] as const
            const data = result.data?.data
            if (!data?.hasRuntimeSamples) return [p.planId!, { status: 'no-samples' }] as const
            const remaining = p.nextDueRuntimeHours! - (data.totalRuntimeHours ?? 0)
            return [p.planId!, { status: 'ok', hours: remaining < 0 ? 0 : remaining }] as const
          } catch {
            return [p.planId!, { status: 'error' }] as const
          }
        },
        () => !isCurrent(),
      )
      // A superseded round must never overwrite the newer round's results or clear its pending flag.
      if (isCurrent()) {
        remainingByPlanId.value = Object.fromEntries(entries)
      }
    } finally {
      if (isCurrent()) pending.value = false
    }
  }

  watch(
    [
      // Include the fields that move the remaining value (next runtime threshold + accrual window start),
      // so advancing a plan's threshold via generate-due triggers a fresh read instead of a stale display.
      () => plans.value.map((p) => `${p.planId}:${p.nextDueRuntimeHours}:${p.startsOn}`).join(','),
      () => businessContext.organizationId,
      () => businessContext.environmentId,
    ],
    () => void recompute(),
    { immediate: true },
  )

  return { remainingByPlanId, remainingPending: pending, refreshRemaining: recompute }
}

/**
 * 采集连接器状态墙轮询周期。读面在心跳超窗后派生 offline，本页每个周期重取以在「1 个轮询周期内」反映。
 * 用官方 auto-refetch 表达，不手写 setInterval。
 */
export const CONNECTOR_HEALTH_POLL_INTERVAL_MS = 10_000

/**
 * stale 有两种成因，展示口径不同：`offline`=心跳超窗停报（真断线，显示断线时长并红色置顶，时长基于冻结的
 * 最后心跳单调增长）；`fault`=仍在心跳但连接器自报终态停止（异常停止，成因未必是断连，不显示断线时长）。
 */
export function connectorHealthStatusLabel(status?: string | null, staleReason?: string | null) {
  if (status === 'current') return '在线'
  if (status === 'stale') return staleReason === 'fault' ? '异常停止' : '断线'
  // heartbeating/running but no sampling evidence yet (no mapping / nothing collected) — not "collecting"
  return '待采集'
}

/** 只有 offline（心跳超窗）才是真断线，显示断线时长。 */
export function isConnectorOffline(status?: string | null, staleReason?: string | null) {
  return status === 'stale' && staleReason === 'offline'
}

/** fault=连接器自报异常停止（成因未必是断连），单列区分，不显示断线时长。 */
export function isConnectorFault(status?: string | null, staleReason?: string | null) {
  return status === 'stale' && staleReason === 'fault'
}

/** 采集连接器的协议类型来自采集健康的 sourceSystem（opcua/modbus/mqtt）。 */
export function connectorSourceSystemLabel(sourceSystem?: string | null) {
  if (!sourceSystem) return '未知类型'
  const labels: Record<string, string> = {
    opcua: 'OPC UA',
    modbus: 'Modbus',
    mqtt: 'MQTT',
  }
  return labels[sourceSystem.toLowerCase()] ?? sourceSystem
}

export interface ConnectorRateSample {
  counterEpoch?: string | null
  receivedCount?: number | null
  metricsReportedAtUtc?: string | null
}

/**
 * 采样吞吐速率（samples/s）= 同一 counter epoch 内连续两次上报的 receivedCount 差 ÷ 上报时间差。
 * epoch 变更（计数器重置）、计数回退、时间未推进或缺首个样本时返回 null（页面显示「计算中」而非伪造速率）。
 */
export function computeConnectorSampleRate(
  previous: ConnectorRateSample | undefined,
  current: ConnectorRateSample,
): number | null {
  if (!previous) return null
  if ((previous.counterEpoch ?? null) !== (current.counterEpoch ?? null)) return null
  const prevReceived = previous.receivedCount
  const curReceived = current.receivedCount
  if (prevReceived == null || curReceived == null || curReceived < prevReceived) return null
  const prevAt = Date.parse(previous.metricsReportedAtUtc ?? '')
  const curAt = Date.parse(current.metricsReportedAtUtc ?? '')
  if (!Number.isFinite(prevAt) || !Number.isFinite(curAt) || curAt <= prevAt) return null
  return (curReceived - prevReceived) / ((curAt - prevAt) / 1000)
}

export function formatSampleRate(rate: number | null | undefined) {
  if (rate === null || rate === undefined) return '计算中…'
  if (rate < 1) return `${rate.toFixed(2)} /秒`
  return `${rate.toLocaleString(undefined, { maximumFractionDigits: 1 })} /秒`
}

/**
 * 采集连接器健康墙：消费连接器采集健康列表 facade，按周期轮询。断线/异常连接器由后端排序置顶。
 * 业务上下文为空时不发请求（空态由页面处理）。采样速率由相邻两次轮询在前端按 epoch 差值计算。
 */
export function useBusinessTelemetryConnectors() {
  const businessContext = useBusinessContextStore()
  const connectorsQuery = useQuery(() => ({
    ...listBusinessConsoleTelemetryConnectorCollectionHealthQueryOptions({
      query: toContextQuery(businessContext),
    }),
    enabled: hasBusinessContext(businessContext),
    autoRefetch: () => CONNECTOR_HEALTH_POLL_INTERVAL_MS,
  }))

  const connectors = computed<BusinessConsoleConnectorCollectionHealthListItem[]>(() =>
    listItems<BusinessConsoleConnectorCollectionHealthListItem>(
      connectorsQuery.data.value as
        | BusinessConsoleConnectorCollectionHealthListEnvelope
        | undefined,
    ),
  )

  const previousSamples = new Map<string, ConnectorRateSample>()
  const sampleRateByConnector = ref<Record<string, number | null>>({})
  watch(connectors, (list) => {
    const next: Record<string, number | null> = {}
    for (const connector of list) {
      const id = connector.connectorId ?? ''
      if (!id) continue
      const sample: ConnectorRateSample = {
        counterEpoch: connector.counterEpoch,
        receivedCount: connector.receivedCount,
        metricsReportedAtUtc: connector.metricsReportedAtUtc,
      }
      next[id] = computeConnectorSampleRate(previousSamples.get(id), sample)
      previousSamples.set(id, sample)
    }
    sampleRateByConnector.value = next
  })

  return {
    connectors,
    connectorsError: connectorsQuery.error,
    connectorsPending: connectorsQuery.isLoading,
    connectorsTotal: computed(() =>
      listTotal(
        connectorsQuery.data.value as
          | BusinessConsoleConnectorCollectionHealthListEnvelope
          | undefined,
      ),
    ),
    refreshConnectors: () => refetchWithBusinessContext(businessContext, connectorsQuery),
    sampleRateByConnector,
  }
}
