import {
  createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions,
  listBusinessConsoleTelemetryAlarmRulesQueryOptions,
  listBusinessConsoleTelemetryTagsQueryOptions,
  queryBusinessConsoleTelemetryDeviceHistoryQueryOptions,
  queryBusinessConsoleTelemetryOeeQueryOptions,
  queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions,
  type BusinessConsoleCreateOrUpdateTelemetryAlarmRuleRequest,
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
import { computed, reactive } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'

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

function defaultWindowFilters(initial: Partial<TelemetryWindowFilters> = {}): TelemetryWindowFilters {
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

function listItems<TItem>(envelope: { success?: boolean, data?: { items?: TItem[] } | null } | undefined) {
  return envelope?.success ? envelope.data?.items ?? [] : []
}

function listTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  return envelope?.success ? envelope.data?.total ?? 0 : 0
}

function unwrapData<TData>(envelope: { success?: boolean, data?: TData | null } | undefined) {
  return envelope?.success ? envelope.data ?? undefined : undefined
}

export function formatOeeRate(value: number | null | undefined) {
  if (value === null || value === undefined) return '无数据'
  return `${(value * 100).toFixed(1)}%`
}

export function describeTelemetryOeeLimitations() {
  return '当前 OEE 只按设备运行状态计算可用率，性能与质量不作为真实测量值；P0 仅用于判断设备运行事实覆盖和停机影响。'
}

export function useBusinessTelemetryTags(initialFilters: Partial<TelemetryListFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultListFilters(initialFilters)
  const tagsQuery = useQuery(() =>
    listBusinessConsoleTelemetryTagsQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: trimOptional(filters.deviceAssetId),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  return {
    filters,
    refreshTags: tagsQuery.refetch,
    tags: computed<BusinessConsoleTelemetryTagItem[]>(() =>
      listItems<BusinessConsoleTelemetryTagItem>(tagsQuery.data.value as BusinessConsoleTelemetryTagListEnvelope | undefined),
    ),
    tagsError: tagsQuery.error,
    tagsPending: tagsQuery.isLoading,
    tagsTotal: computed(() => listTotal(tagsQuery.data.value as BusinessConsoleTelemetryTagListEnvelope | undefined)),
  }
}

export function useBusinessTelemetryAlarmRules(initialFilters: Partial<TelemetryListFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultListFilters(initialFilters)
  const alarmRulesQuery = useQuery(() =>
    listBusinessConsoleTelemetryAlarmRulesQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: trimOptional(filters.deviceAssetId),
        isEnabled: enabledQuery(filters.isEnabled),
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )
  const saveMutation = useMutation({
    ...createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions(),
    onSuccess() {
      void alarmRulesQuery.refetch()
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
      listTotal(alarmRulesQuery.data.value as BusinessConsoleTelemetryAlarmRuleListEnvelope | undefined),
    ),
    filters,
    refreshAlarmRules: alarmRulesQuery.refetch,
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

export function useBusinessTelemetryHistory(initialFilters: Partial<TelemetryWindowFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultWindowFilters(initialFilters)
  const deviceAssetId = computed(() => filters.deviceAssetId.trim())
  const historyEnabled = computed(() => deviceAssetId.value.length > 0)
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
    return historyItems.value.filter((item) => item.tagKey === tagKey)
  })

  return {
    filters,
    historyError: historyQuery.error,
    historyItems,
    historyPending: historyQuery.isLoading,
    refreshHistory: () => historyEnabled.value ? historyQuery.refetch() : Promise.resolve(),
    visibleHistoryItems,
  }
}

export function useBusinessTelemetryOee(initialFilters: Partial<TelemetryWindowFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultWindowFilters(initialFilters)
  const deviceAssetId = computed(() => filters.deviceAssetId.trim())
  const oeeEnabled = computed(() => deviceAssetId.value.length > 0)
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
      unwrapData<BusinessConsoleTelemetryOeeResponse>(oeeQuery.data.value as BusinessConsoleTelemetryOeeEnvelope | undefined),
    ),
    oeeError: oeeQuery.error,
    oeePending: computed(() => oeeQuery.isLoading.value || runtimeAvailabilityQuery.isLoading.value),
    refreshOee: () => oeeEnabled.value
      ? Promise.all([oeeQuery.refetch(), runtimeAvailabilityQuery.refetch()])
      : Promise.resolve(),
    runtimeAvailabilityError: runtimeAvailabilityQuery.error,
  }
}
