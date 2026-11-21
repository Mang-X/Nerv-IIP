import { useBusinessContextStore } from '@/stores/businessContext'
import { computed, reactive } from 'vue'
import { useBusinessMasterDataResources } from '../useBusinessMasterData'

const DEFAULT_DEVICE_ASSET_IDS = ''

// 看板默认范围 = 全部设备。后端 overview/availability 要求 deviceAssetIds 非空（最多 50 个），
// 故未手动指定范围时，自动取设备资源列表（device-asset）的全部编号带入。
export const MAX_DEVICE_ASSET_IDS = 50

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

export function defaultOverviewFilters(): BusinessEquipmentOverviewFilters {
  return reactive({
    deviceAssetIds: DEFAULT_DEVICE_ASSET_IDS,
  })
}

export function defaultAvailabilityFilters(): BusinessEquipmentAvailabilityFilters {
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

export function defaultDeviceFilters(deviceAssetId = ''): BusinessEquipmentDeviceFilters {
  return reactive({
    deviceAssetId,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

export function normalizeDeviceAssetIds(deviceAssetIds: string) {
  return deviceAssetIds
    .split(',')
    .map((deviceAssetId) => deviceAssetId.trim())
    .filter((deviceAssetId) => deviceAssetId.length > 0)
    .join(',')
}

export function toContextQuery(businessContext: ReturnType<typeof useBusinessContextStore>) {
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

export function toAvailabilityQuery(
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

export function unwrapData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

export function listItems<
  TItem,
  TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null },
>(envelope: TEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

/**
 * 看板默认范围解析：用户手动输入了设备号则按输入过滤；未输入时回退到「全部设备」——
 * 取设备资源列表（device-asset）的全部编号（后端最多 50 个，超出截断）。返回逗号分隔串。
 *
 * 台账读面的 error 必须透出：台账 4xx/5xx 时「在册几台」本身就是未知，
 * 页面不能把它渲染成「0 台设备」——那是把故障伪装成现场没有设备。
 */
export function useEffectiveDeviceAssetIds(filters: BusinessEquipmentOverviewFilters) {
  const {
    resources: deviceResources,
    resourcesError: deviceRosterError,
    resourcesPending: deviceResourcesPending,
    resourcesTotal: deviceRosterTotal,
    refreshResources: refreshDeviceRoster,
  } = useBusinessMasterDataResources('device-asset')

  const rosterDeviceCodes = computed(() =>
    deviceResources.value
      .map((device) => device.code?.trim())
      .filter((code): code is string => Boolean(code)),
  )

  const allDeviceAssetIds = computed(() =>
    rosterDeviceCodes.value.slice(0, MAX_DEVICE_ASSET_IDS).join(','),
  )

  const effectiveDeviceAssetIds = computed(() => {
    const manual = normalizeDeviceAssetIds(filters.deviceAssetIds)
    return manual.length > 0 ? manual : allDeviceAssetIds.value
  })

  return {
    deviceResourcesPending,
    deviceRosterError,
    // 台账在册总数（后端 total，含未加载到本页的记录）；台账取不到时回 0，调用方须先看 error。
    deviceRosterTotal: computed(() =>
      Math.max(deviceRosterTotal.value, rosterDeviceCodes.value.length),
    ),
    effectiveDeviceAssetIds,
    // 本次实际带入查询的设备台数——与在册总数不等时说明发生了截断，页面必须如实说明。
    effectiveDeviceAssetIdCount: computed(
      () =>
        normalizeDeviceAssetIds(effectiveDeviceAssetIds.value).split(',').filter(Boolean).length,
    ),
    refreshDeviceRoster,
  }
}
