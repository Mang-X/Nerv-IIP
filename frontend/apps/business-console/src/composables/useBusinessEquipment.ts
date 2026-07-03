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
import { useBusinessMasterDataResources } from './useBusinessMasterData'
import { hasBusinessContext, refetchWithBusinessContext } from './businessContextBinding'

const DEFAULT_DEVICE_ASSET_IDS = ''

// 看板默认范围 = 全部设备。后端 overview/availability 要求 deviceAssetIds 非空（最多 50 个），
// 故未手动指定范围时，自动取设备资源列表（device-asset）的全部编号带入。
const MAX_DEVICE_ASSET_IDS = 50

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

function defaultDeviceFilters(deviceAssetId = ''): BusinessEquipmentDeviceFilters {
  return reactive({
    deviceAssetId,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function normalizeDeviceAssetIds(deviceAssetIds: string) {
  return deviceAssetIds
    .split(',')
    .map((deviceAssetId) => deviceAssetId.trim())
    .filter((deviceAssetId) => deviceAssetId.length > 0)
    .join(',')
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
    deviceAssetIds: normalizeDeviceAssetIds(filters.deviceAssetIds),
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

/**
 * 看板默认范围解析：用户手动输入了设备号则按输入过滤；未输入时回退到「全部设备」——
 * 取设备资源列表（device-asset）的全部编号（后端最多 50 个，超出截断）。返回逗号分隔串。
 */
function useEffectiveDeviceAssetIds(filters: BusinessEquipmentOverviewFilters) {
  const { resources: deviceResources, resourcesPending: deviceResourcesPending }
    = useBusinessMasterDataResources('device-asset')

  const allDeviceAssetIds = computed(() =>
    deviceResources.value
      .map((device) => device.code?.trim())
      .filter((code): code is string => Boolean(code))
      .slice(0, MAX_DEVICE_ASSET_IDS)
      .join(','),
  )

  const effectiveDeviceAssetIds = computed(() => {
    const manual = normalizeDeviceAssetIds(filters.deviceAssetIds)
    return manual.length > 0 ? manual : allDeviceAssetIds.value
  })

  return { effectiveDeviceAssetIds, deviceResourcesPending }
}

export function useBusinessEquipmentOverview() {
  const businessContext = useBusinessContextStore()
  const filters = defaultOverviewFilters()
  const { effectiveDeviceAssetIds, deviceResourcesPending } = useEffectiveDeviceAssetIds(filters)
  const overviewEnabled = computed(() => hasBusinessContext(businessContext) && effectiveDeviceAssetIds.value.length > 0)
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

  return {
    activeBlocks: computed(() => overview.value?.activeBlocks ?? []),
    devices: computed(() => overview.value?.devices ?? []),
    filters,
    overview,
    overviewError: overviewQuery.error,
    overviewPending: computed(() => deviceResourcesPending.value || overviewQuery.isLoading.value),
    refreshOverview: () => overviewEnabled.value ? overviewQuery.refetch() : Promise.resolve(),
  }
}

export function useBusinessEquipmentAvailability() {
  const businessContext = useBusinessContextStore()
  const filters = defaultAvailabilityFilters()
  const availabilityEnabled = computed(() => hasBusinessContext(businessContext) && normalizeDeviceAssetIds(filters.deviceAssetIds).length > 0)
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
    refreshAvailability: () => availabilityEnabled.value ? availabilityQuery.refetch() : Promise.resolve(),
  }
}

export function useBusinessEquipmentDevice(deviceAssetId?: string) {
  const businessContext = useBusinessContextStore()
  const filters = defaultDeviceFilters(deviceAssetId)
  const deviceEnabled = computed(() => hasBusinessContext(businessContext) && filters.deviceAssetId.trim().length > 0)
  const deviceQuery = useQuery(() => ({
    ...getBusinessConsoleEquipmentDeviceQueryOptions({
      path: { deviceAssetId: filters.deviceAssetId },
      query: toContextQuery(businessContext),
    }),
    enabled: deviceEnabled.value,
  }))

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
    refreshDevice: () => deviceEnabled.value ? deviceQuery.refetch() : Promise.resolve(),
  }
}

export function useBusinessEquipmentAlarms() {
  const businessContext = useBusinessContextStore()
  const alarmsQuery = useQuery(() => ({
    ...listBusinessConsoleEquipmentAlarmsQueryOptions({
      query: toContextQuery(businessContext),
    }),
    enabled: hasBusinessContext(businessContext),
  }))

  return {
    alarms: computed<EquipmentRuntimeAlarmSummary[]>(() =>
      listItems<EquipmentRuntimeAlarmSummary, BusinessConsoleEquipmentAlarmListEnvelope>(
        alarmsQuery.data.value,
      ),
    ),
    alarmsError: alarmsQuery.error,
    alarmsPending: alarmsQuery.isLoading,
    refreshAlarms: () => refetchWithBusinessContext(businessContext, alarmsQuery),
  }
}
