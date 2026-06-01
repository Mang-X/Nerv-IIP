import {
  getBusinessConsoleEquipmentAvailabilityQueryOptions,
  getBusinessConsoleEquipmentDeviceQueryOptions,
  getBusinessConsoleEquipmentOverviewQueryOptions,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  type BusinessConsoleEquipmentAlarmListEnvelope,
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
import { computed, reactive } from 'vue'

const DEFAULT_DEVICE_ASSET_IDS = 'DEV-OIL-01,DEV-PACK-01'

export type EquipmentTone = 'success' | 'danger' | 'muted'

export interface EquipmentReasonDisplay {
  code: string
  label: string
  nextStep: string
}

export interface BusinessEquipmentOverviewFilters {
  deviceAssetIds: string
}

export interface BusinessEquipmentAvailabilityFilters extends BusinessEquipmentOverviewFilters {
  windowStartUtc: string
  windowEndUtc: string
  workCenterIds?: string
}

export interface BusinessEquipmentDeviceFilters {
  deviceAssetId: string
}

const equipmentReasonDisplays: Record<string, EquipmentReasonDisplay> = {
  'equipment.activeAlarm': {
    code: 'equipment.activeAlarm',
    label: '设备报警未解除',
    nextStep: '处理并解除设备报警后重新检查',
  },
  'equipment.stateUnavailable': {
    code: 'equipment.stateUnavailable',
    label: '设备状态不可运行',
    nextStep: '确认设备恢复运行后重新检查',
  },
  'equipment.downtime': {
    code: 'equipment.downtime',
    label: '设备停机中',
    nextStep: '关闭停机事件或改派可用设备',
  },
  'equipment.maintenanceWindow': {
    code: 'equipment.maintenanceWindow',
    label: '维修保养占用',
    nextStep: '调整维修窗口、等待释放或选择替代设备',
  },
  'equipment.inspectionRequired': {
    code: 'equipment.inspectionRequired',
    label: '点检未通过',
    nextStep: '完成点检并确认结果后重新检查',
  },
  'equipment.sourceStale': {
    code: 'equipment.sourceStale',
    label: '采集数据过期',
    nextStep: '检查采集连接并刷新设备状态',
  },
  'equipment.tagMappingMissing': {
    code: 'equipment.tagMappingMissing',
    label: '采集点未配置',
    nextStep: '补齐设备采集点映射',
  },
  'equipment.noEligibleSubstitute': {
    code: 'equipment.noEligibleSubstitute',
    label: '无可替代设备',
    nextStep: '调整排程或维护设备能力配置',
  },
}

export function describeEquipmentReason(code: string): EquipmentReasonDisplay {
  const normalizedCode = code.trim()

  return equipmentReasonDisplays[normalizedCode] ?? {
    code: normalizedCode,
    label: normalizedCode,
    nextStep: '查看设备详情并处理来源业务单据',
  }
}

export function equipmentStatusTone(status: string | null | undefined): EquipmentTone {
  const value = status?.trim().toLowerCase()
  if (value === 'running' || value === 'ready' || value === 'idle') {
    return 'success'
  }
  if (value === 'faulted' || value === 'stopped' || value === 'offline' || value === 'down') {
    return 'danger'
  }
  return 'muted'
}

function defaultOverviewFilters(): BusinessEquipmentOverviewFilters {
  return reactive({
    deviceAssetIds: DEFAULT_DEVICE_ASSET_IDS,
  })
}

function defaultAvailabilityFilters(): BusinessEquipmentAvailabilityFilters {
  const now = new Date()
  const start = new Date(now)
  start.setHours(8, 0, 0, 0)
  const end = new Date(start)
  end.setHours(16, 0, 0, 0)

  return reactive({
    deviceAssetIds: DEFAULT_DEVICE_ASSET_IDS,
    windowStartUtc: start.toISOString(),
    windowEndUtc: end.toISOString(),
  })
}

function defaultDeviceFilters(deviceAssetId = 'DEV-OIL-01'): BusinessEquipmentDeviceFilters {
  return reactive({
    deviceAssetId,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function toContextQuery(businessContext: ReturnType<typeof useBusinessContextStore>) {
  return {
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }
}

function toOverviewQuery(
  businessContext: ReturnType<typeof useBusinessContextStore>,
  filters: BusinessEquipmentOverviewFilters,
) {
  return {
    ...toContextQuery(businessContext),
    deviceAssetIds: filters.deviceAssetIds,
  }
}

function toAvailabilityQuery(
  businessContext: ReturnType<typeof useBusinessContextStore>,
  filters: BusinessEquipmentAvailabilityFilters,
) {
  return {
    ...toOverviewQuery(businessContext, filters),
    windowStartUtc: filters.windowStartUtc,
    windowEndUtc: filters.windowEndUtc,
    ...optionalQuery('workCenterIds', filters.workCenterIds),
  }
}

function unwrapData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function listItems<TItem, TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

export function useBusinessEquipmentOverview() {
  const businessContext = useBusinessContextStore()
  const filters = defaultOverviewFilters()
  const overviewQuery = useQuery(() =>
    getBusinessConsoleEquipmentOverviewQueryOptions({
      query: toOverviewQuery(businessContext, filters),
    }),
  )

  const overview = computed(() =>
    unwrapData<BusinessConsoleEquipmentOverviewResponse, BusinessConsoleEquipmentOverviewEnvelope>(
      overviewQuery.data.value,
    ),
  )

  return {
    activeBlocks: computed(() => overview.value?.activeBlocks ?? []),
    devices: computed(() => overview.value?.devices ?? []),
    filters,
    overview,
    overviewError: overviewQuery.error,
    overviewPending: overviewQuery.isLoading,
    refreshOverview: overviewQuery.refetch,
  }
}

export function useBusinessEquipmentAvailability() {
  const businessContext = useBusinessContextStore()
  const filters = defaultAvailabilityFilters()
  const availabilityQuery = useQuery(() =>
    getBusinessConsoleEquipmentAvailabilityQueryOptions({
      query: toAvailabilityQuery(businessContext, filters),
    }),
  )

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
    refreshAvailability: availabilityQuery.refetch,
  }
}

export function useBusinessEquipmentDevice(deviceAssetId?: string) {
  const businessContext = useBusinessContextStore()
  const filters = defaultDeviceFilters(deviceAssetId)
  const deviceQuery = useQuery(() =>
    getBusinessConsoleEquipmentDeviceQueryOptions({
      path: { deviceAssetId: filters.deviceAssetId },
      query: toContextQuery(businessContext),
    }),
  )

  const device = computed<BusinessConsoleEquipmentDeviceDetailResponse | undefined>(() =>
    unwrapData<BusinessConsoleEquipmentDeviceDetailResponse, BusinessConsoleEquipmentDeviceDetailEnvelope>(
      deviceQuery.data.value,
    ),
  )

  return {
    activeAlarms: computed<EquipmentRuntimeAlarmSummary[]>(() => device.value?.currentState?.activeAlarms ?? []),
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(() => device.value?.availability?.items ?? []),
    device,
    deviceError: deviceQuery.error,
    devicePending: deviceQuery.isLoading,
    filters,
    refreshDevice: deviceQuery.refetch,
  }
}

export function useBusinessEquipmentAlarms() {
  const businessContext = useBusinessContextStore()
  const alarmsQuery = useQuery(() =>
    listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: toContextQuery(businessContext),
    }),
  )

  return {
    alarms: computed<EquipmentRuntimeAlarmSummary[]>(() =>
      listItems<EquipmentRuntimeAlarmSummary, BusinessConsoleEquipmentAlarmListEnvelope>(
        alarmsQuery.data.value,
      ),
    ),
    alarmsError: alarmsQuery.error,
    alarmsPending: alarmsQuery.isLoading,
    refreshAlarms: alarmsQuery.refetch,
  }
}
