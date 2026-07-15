import {
  createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions,
  getBusinessConsoleTelemetryTagCurrentValueQueryOptions,
  listBusinessConsoleTelemetryAlarmRulesQueryOptions,
  listBusinessConsoleTelemetryConnectorCollectionHealthQueryOptions,
  listBusinessConsoleTelemetryTagsQueryOptions,
  queryBusinessConsoleTelemetryDeviceHistoryQueryOptions,
  queryBusinessConsoleTelemetryOeeQueryOptions,
  queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions,
  type BusinessConsoleConnectorCollectionHealthListEnvelope,
  type BusinessConsoleConnectorCollectionHealthListItem,
  type BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest,
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
import { computed, reactive, type Ref } from 'vue'
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
 * 采集连接器状态墙轮询周期。拔线后后端在心跳超时窗口内把状态派生为 stale，本页每个周期重取以在
 * 「1 个轮询周期内」反映断线。用官方 auto-refetch 表达，不手写 setInterval。
 */
export const CONNECTOR_HEALTH_POLL_INTERVAL_MS = 10_000

export function connectorHealthStatusLabel(status?: string | null) {
  const labels: Record<string, string> = {
    current: '在线',
    stale: '断线 / 异常',
    unknown: '未上报',
  }
  return status ? (labels[status] ?? status) : '未上报'
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

/**
 * 采集连接器健康墙：消费 #885 连接器采集健康列表 facade，按周期轮询。断线/异常连接器由后端排序置顶。
 * 业务上下文为空时不发请求（空态由页面处理）。
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

  return {
    connectors: computed<BusinessConsoleConnectorCollectionHealthListItem[]>(() =>
      listItems<BusinessConsoleConnectorCollectionHealthListItem>(
        connectorsQuery.data.value as
          | BusinessConsoleConnectorCollectionHealthListEnvelope
          | undefined,
      ),
    ),
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
  }
}
