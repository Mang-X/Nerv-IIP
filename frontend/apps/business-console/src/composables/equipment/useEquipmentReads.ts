import {
  getBusinessConsoleEquipmentAvailabilityQueryOptions,
  getBusinessConsoleEquipmentDeviceQueryOptions,
  getBusinessConsoleEquipmentOverviewQueryOptions,
  type BusinessConsoleEquipmentDeviceDetailEnvelope,
  type BusinessConsoleEquipmentDeviceDetailResponse,
  type BusinessConsoleEquipmentOverviewEnvelope,
  type BusinessConsoleEquipmentOverviewResponse,
  type EquipmentRuntimeAlarmSummary,
  type EquipmentRuntimeAvailabilityEnvelope,
  type EquipmentRuntimeAvailabilityWindow,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { hasBusinessContext } from '../businessContextBinding'
import {
  defaultAvailabilityFilters,
  defaultDeviceFilters,
  defaultOverviewFilters,
  listItems,
  normalizeDeviceAssetIds,
  toAvailabilityQuery,
  toContextQuery,
  unwrapData,
  useEffectiveDeviceAssetIds,
} from './equipmentReadScope'
import { type BusinessEquipmentQueryState } from './equipmentStatusDisplay'

export function useBusinessEquipmentOverview() {
  const businessContext = useBusinessContextStore()
  const filters = defaultOverviewFilters()
  const {
    deviceResourcesPending,
    deviceRosterError,
    deviceRosterTotal,
    effectiveDeviceAssetIds,
    effectiveDeviceAssetIdCount,
    refreshDeviceRoster,
  } = useEffectiveDeviceAssetIds(filters)
  const contextReady = computed(() => hasBusinessContext(businessContext))
  const overviewEnabled = computed(
    () => contextReady.value && effectiveDeviceAssetIds.value.length > 0,
  )
  const overviewQuery = useQuery(() => ({
    ...getBusinessConsoleEquipmentOverviewQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetIds: effectiveDeviceAssetIds.value,
      },
    }),
    enabled: overviewEnabled.value,
  }))

  const overview = computed(() =>
    unwrapData<BusinessConsoleEquipmentOverviewResponse, BusinessConsoleEquipmentOverviewEnvelope>(
      overviewQuery.data.value,
    ),
  )

  const overviewPending = computed(
    () => deviceResourcesPending.value || overviewQuery.isLoading.value,
  )
  // 四态判定顺序：上下文未就绪 → 在查（含重试）→ 任一读面失败 → 已取到。
  // 台账失败与 overview 失败都算 error：两者任一取不到，「在册几台/几台在跑」就都是未知。
  const overviewState = computed<BusinessEquipmentQueryState>(() => {
    if (!contextReady.value) return 'idle'
    if (overviewPending.value) return 'loading'
    if (deviceRosterError.value || overviewQuery.error.value) return 'error'
    return 'ready'
  })

  return {
    activeBlocks: computed(() => overview.value?.activeBlocks ?? []),
    contextReady,
    deviceRosterError,
    deviceRosterTotal,
    devices: computed(() => overview.value?.devices ?? []),
    effectiveDeviceAssetIdCount,
    filters,
    overview,
    overviewError: overviewQuery.error,
    overviewPending,
    overviewState,
    refreshOverview: () => (overviewEnabled.value ? overviewQuery.refetch() : Promise.resolve()),
    refreshDeviceRoster,
  }
}

export function useBusinessEquipmentAvailability() {
  const businessContext = useBusinessContextStore()
  const filters = defaultAvailabilityFilters()
  const availabilityEnabled = computed(
    () =>
      hasBusinessContext(businessContext) &&
      normalizeDeviceAssetIds(filters.deviceAssetIds).length > 0,
  )
  const availabilityQuery = useQuery(() => ({
    ...getBusinessConsoleEquipmentAvailabilityQueryOptions({
      query: toAvailabilityQuery(businessContext, filters),
    }),
    enabled: availabilityEnabled.value,
  }))

  return {
    availability: computed(() =>
      unwrapData<
        NonNullable<EquipmentRuntimeAvailabilityEnvelope['data']>,
        EquipmentRuntimeAvailabilityEnvelope
      >(availabilityQuery.data.value),
    ),
    availabilityError: availabilityQuery.error,
    availabilityPending: availabilityQuery.isLoading,
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(() =>
      listItems<EquipmentRuntimeAvailabilityWindow, EquipmentRuntimeAvailabilityEnvelope>(
        availabilityQuery.data.value,
      ),
    ),
    filters,
    refreshAvailability: () =>
      availabilityEnabled.value ? availabilityQuery.refetch() : Promise.resolve(),
  }
}

export function useBusinessEquipmentDevice(deviceAssetId?: string) {
  const businessContext = useBusinessContextStore()
  const filters = defaultDeviceFilters(deviceAssetId)
  const deviceEnabled = computed(
    () => hasBusinessContext(businessContext) && filters.deviceAssetId.trim().length > 0,
  )
  const deviceQuery = useQuery(() => ({
    ...getBusinessConsoleEquipmentDeviceQueryOptions({
      path: { deviceAssetId: filters.deviceAssetId },
      query: toContextQuery(businessContext),
    }),
    enabled: deviceEnabled.value,
  }))

  const device = computed<BusinessConsoleEquipmentDeviceDetailResponse | undefined>(() =>
    unwrapData<
      BusinessConsoleEquipmentDeviceDetailResponse,
      BusinessConsoleEquipmentDeviceDetailEnvelope
    >(deviceQuery.data.value),
  )

  return {
    activeAlarms: computed<EquipmentRuntimeAlarmSummary[]>(
      () => device.value?.currentState?.activeAlarms ?? [],
    ),
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(
      () => device.value?.availability?.items ?? [],
    ),
    device,
    deviceError: deviceQuery.error,
    devicePending: deviceQuery.isLoading,
    filters,
    refreshDevice: () => (deviceEnabled.value ? deviceQuery.refetch() : Promise.resolve()),
  }
}
