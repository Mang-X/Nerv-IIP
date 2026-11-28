import {
  createOrUpdateBusinessConsoleTelemetryAlarmRuleMutationOptions,
  getBusinessConsoleTelemetryTagCurrentValueQueryOptions,
  listBusinessConsoleTelemetryAlarmRulesQueryOptions,
  listBusinessConsoleTelemetryTagsQueryOptions,
  queryBusinessConsoleTelemetryDeviceHistoryQueryOptions,
  type BusinessConsoleTelemetryTagCurrentValueEnvelope,
  type BusinessConsoleTelemetryTagCurrentValueResponse,
  type BusinessConsoleTelemetryAlarmRuleItem,
  type BusinessConsoleTelemetryAlarmRuleListEnvelope,
  type BusinessConsoleTelemetryHistoryEnvelope,
  type BusinessConsoleTelemetryHistoryItem,
  type BusinessConsoleTelemetryTagItem,
  type BusinessConsoleTelemetryTagListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, toValue, type MaybeRefOrGetter, type Ref } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext, refetchWithBusinessContext } from '../shared/businessContextBinding'
import {
  defaultListFilters,
  defaultWindowFilters,
  enabledQuery,
  listItems,
  listTotal,
  toContextQuery,
  trimOptional,
  type SaveTelemetryAlarmRuleInput,
  type TelemetryListFilters,
  type TelemetryWindowFilters,
  unwrapData,
} from './telemetryShared'

export function useBusinessTelemetryTags(
  initialFilters: Partial<TelemetryListFilters> = {},
  poll: MaybeRefOrGetter<number | false> = false,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
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
    enabled: hasBusinessContext(businessContext) && toValue(enabled),
    autoRefetch: () => toValue(poll),
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

export function useBusinessTelemetryAlarmRules(
  initialFilters: Partial<TelemetryListFilters> = {},
  enabled: MaybeRefOrGetter<boolean> = true,
) {
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
    enabled: hasBusinessContext(businessContext) && toValue(enabled),
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

export function useBusinessTelemetryHistory(
  initialFilters: Partial<TelemetryWindowFilters> = {},
  poll: MaybeRefOrGetter<number | false> = false,
  rollingMinutes: MaybeRefOrGetter<number | undefined> = undefined,
) {
  const businessContext = useBusinessContextStore()
  const filters = defaultWindowFilters(initialFilters)
  const deviceAssetId = computed(() => filters.deviceAssetId.trim())
  const historyEnabled = computed(
    () => hasBusinessContext(businessContext) && deviceAssetId.value.length > 0,
  )
  const historyQuery = useQuery(() => {
    const options = queryBusinessConsoleTelemetryDeviceHistoryQueryOptions({
      path: { deviceAssetId: deviceAssetId.value },
      query: {
        ...toContextQuery(businessContext),
        fromUtc: trimOptional(filters.windowStartUtc),
        toUtc: trimOptional(filters.windowEndUtc),
      },
    })
    const minutes = toValue(rollingMinutes)
    return {
      ...options,
      enabled: historyEnabled.value,
      autoRefetch: () => toValue(poll),
      ...(minutes
        ? {
            query: (context: Parameters<typeof options.query>[0]) =>
              queryBusinessConsoleTelemetryDeviceHistoryQueryOptions({
                path: { deviceAssetId: deviceAssetId.value },
                query: {
                  ...toContextQuery(businessContext),
                  fromUtc: new Date(Date.now() - minutes * 60_000).toISOString(),
                  toUtc: new Date().toISOString(),
                },
              }).query(context),
          }
        : {}),
    }
  })
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
