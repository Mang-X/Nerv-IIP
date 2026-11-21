import {
  queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions,
  queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions,
  queryBusinessConsoleMaintenanceInspectionMeasurementTrendQueryOptions,
  queryBusinessConsoleMaintenanceReliabilitySummaryQueryOptions,
  type BusinessConsoleMaintenanceAssetReliabilityEnvelope,
  type BusinessConsoleMaintenanceAssetReliabilityResponse,
  type BusinessConsoleMaintenanceInspectionMeasurementTrendItem,
  type BusinessConsoleMaintenanceInspectionMeasurementTrendResponse,
  type BusinessConsoleMaintenanceReliabilitySummaryItem,
  type BusinessConsoleMaintenanceReliabilitySummaryResponse,
  type EquipmentRuntimeAvailabilityEnvelope,
  type EquipmentRuntimeAvailabilityWindow,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { bindBusinessContext, hasBusinessContext } from '../businessContextBinding'
import { listItems, optionalQuery, unwrapData } from './maintenanceShared'

export interface MaintenanceReliabilityFilters {
  organizationId: string
  environmentId: string
  deviceAssetId: string
  windowStartUtc: string
  windowEndUtc: string
}

export interface MaintenanceAvailabilityFilters {
  organizationId: string
  environmentId: string
  deviceAssetIds: string
  windowStartUtc: string
  windowEndUtc: string
  workCenterIds: string
}

function defaultWindowRange() {
  const end = new Date()
  const start = new Date(end)
  start.setDate(start.getDate() - 30)

  return {
    windowStartUtc: start.toISOString(),
    windowEndUtc: end.toISOString(),
  }
}

function defaultReliabilityFilters(
  initial: Partial<MaintenanceReliabilityFilters> = {},
): MaintenanceReliabilityFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      deviceAssetId: '',
      ...defaultWindowRange(),
      ...initial,
    }),
  )
}

function defaultAvailabilityFilters(
  initial: Partial<MaintenanceAvailabilityFilters> = {},
): MaintenanceAvailabilityFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      deviceAssetIds: '',
      workCenterIds: '',
      ...defaultWindowRange(),
      ...initial,
    }),
  )
}

export function useMaintenanceReliability(
  initialFilters: Partial<MaintenanceReliabilityFilters> = {},
) {
  const filters = defaultReliabilityFilters(initialFilters)
  const reliabilityEnabled = computed(
    () => hasBusinessContext(filters) && filters.deviceAssetId.trim().length > 0,
  )
  const reliabilityQuery = useQuery(() => ({
    ...queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions({
      path: { deviceAssetId: filters.deviceAssetId.trim() },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
      },
    }),
    enabled: reliabilityEnabled.value,
  }))

  return {
    filters,
    reliability: computed<BusinessConsoleMaintenanceAssetReliabilityResponse | undefined>(() =>
      unwrapData<BusinessConsoleMaintenanceAssetReliabilityResponse>(
        reliabilityQuery.data.value as
          | BusinessConsoleMaintenanceAssetReliabilityEnvelope
          | undefined,
      ),
    ),
    reliabilityError: reliabilityQuery.error,
    reliabilityPending: reliabilityQuery.isLoading,
    refreshReliability: () =>
      reliabilityEnabled.value ? reliabilityQuery.refetch() : Promise.resolve(),
  }
}

export interface MaintenanceMeasurementTrendFilters {
  organizationId: string
  environmentId: string
  deviceAssetId: string
  characteristicCode: string
  windowStartUtc: string
  windowEndUtc: string
}

function defaultMeasurementTrendFilters(
  initial: Partial<MaintenanceMeasurementTrendFilters> = {},
): MaintenanceMeasurementTrendFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      deviceAssetId: '',
      characteristicCode: '',
      ...defaultWindowRange(),
      ...initial,
    }),
  )
}

/**
 * 同设备同特性的测量值时间序列（趋势小图数据源）。需 org/env + 设备 + 特性齐备才发请求，
 * 缺任一即静默为空态，不打空请求。
 */
export function useMaintenanceMeasurementTrend(
  initialFilters: Partial<MaintenanceMeasurementTrendFilters> = {},
) {
  const filters = defaultMeasurementTrendFilters(initialFilters)
  const trendEnabled = computed(
    () =>
      hasBusinessContext(filters) &&
      filters.deviceAssetId.trim().length > 0 &&
      filters.characteristicCode.trim().length > 0,
  )
  const trendQuery = useQuery(() => ({
    ...queryBusinessConsoleMaintenanceInspectionMeasurementTrendQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        deviceAssetId: filters.deviceAssetId.trim(),
        characteristicCode: filters.characteristicCode.trim(),
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
      },
    }),
    enabled: trendEnabled.value,
  }))

  const trend = computed<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse | undefined>(
    () =>
      unwrapData<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse>(
        trendQuery.data.value as
          | {
              success?: boolean
              data?: BusinessConsoleMaintenanceInspectionMeasurementTrendResponse | null
            }
          | undefined,
      ),
  )

  return {
    filters,
    trend,
    trendItems: computed<BusinessConsoleMaintenanceInspectionMeasurementTrendItem[]>(
      () => trend.value?.items ?? [],
    ),
    trendError: trendQuery.error,
    trendPending: trendQuery.isLoading,
    trendEnabled,
    refreshTrend: () => (trendEnabled.value ? trendQuery.refetch() : Promise.resolve()),
  }
}

export interface MaintenanceReliabilitySummaryFilters {
  organizationId: string
  environmentId: string
  deviceAssetId: string
  technicianUserId: string
  windowStartUtc: string
  windowEndUtc: string
}

function defaultReliabilitySummaryFilters(
  initial: Partial<MaintenanceReliabilitySummaryFilters> = {},
): MaintenanceReliabilitySummaryFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      deviceAssetId: '',
      technicianUserId: '',
      ...defaultWindowRange(),
      ...initial,
    }),
  )
}

/**
 * 窗口内按（设备 · 技师）聚合的工时与费用汇总。设备/技师为可选过滤（空即不带该参数）；
 * 只需 org/env 即可查（可跨设备汇总），可靠性页按当前设备下钻。
 */
export function useMaintenanceReliabilitySummary(
  initialFilters: Partial<MaintenanceReliabilitySummaryFilters> = {},
) {
  const filters = defaultReliabilitySummaryFilters(initialFilters)
  const summaryEnabled = computed(() => hasBusinessContext(filters))
  const summaryQuery = useQuery(() => ({
    ...queryBusinessConsoleMaintenanceReliabilitySummaryQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
        ...optionalQuery('deviceAssetId', filters.deviceAssetId),
        ...optionalQuery('technicianUserId', filters.technicianUserId),
      },
    }),
    enabled: summaryEnabled.value,
  }))

  const summary = computed<BusinessConsoleMaintenanceReliabilitySummaryResponse | undefined>(() =>
    unwrapData<BusinessConsoleMaintenanceReliabilitySummaryResponse>(
      summaryQuery.data.value as
        | { success?: boolean; data?: BusinessConsoleMaintenanceReliabilitySummaryResponse | null }
        | undefined,
    ),
  )

  return {
    filters,
    summaryItems: computed<BusinessConsoleMaintenanceReliabilitySummaryItem[]>(
      () => summary.value?.items ?? [],
    ),
    summaryError: summaryQuery.error,
    summaryPending: summaryQuery.isLoading,
    summaryEnabled,
    refreshSummary: () => (summaryEnabled.value ? summaryQuery.refetch() : Promise.resolve()),
  }
}

export function useMaintenanceAvailabilityWindows(
  initialFilters: Partial<MaintenanceAvailabilityFilters> = {},
) {
  const filters = defaultAvailabilityFilters(initialFilters)
  const availabilityEnabled = computed(
    () => hasBusinessContext(filters) && filters.deviceAssetIds.trim().length > 0,
  )
  const availabilityQuery = useQuery(() => ({
    ...queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
        ...optionalQuery('deviceAssetIds', filters.deviceAssetIds),
        ...optionalQuery('workCenterIds', filters.workCenterIds),
      },
    }),
    enabled: availabilityEnabled.value,
  }))

  return {
    filters,
    availabilityError: availabilityQuery.error,
    availabilityPending: availabilityQuery.isLoading,
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(() =>
      listItems<EquipmentRuntimeAvailabilityWindow>(
        availabilityQuery.data.value as EquipmentRuntimeAvailabilityEnvelope | undefined,
      ),
    ),
    refreshAvailability: () =>
      availabilityEnabled.value ? availabilityQuery.refetch() : Promise.resolve(),
  }
}
