import type {
  BusinessConsoleMaintenanceWorkOrderItem,
  BusinessConsoleMasterDataResourceDetail,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'

type MaintenanceDevice = BusinessConsoleResourceItem | BusinessConsoleMasterDataResourceDetail

function normalize(value?: string | null) {
  return typeof value === 'string' ? value.trim().toLowerCase() : ''
}

const GUID_REFERENCE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function maintenanceWorkOrderTitle(sourceReferenceId: unknown) {
  if (typeof sourceReferenceId !== 'string') return '维修工单'
  const reference = sourceReferenceId.trim()
  if (!reference || GUID_REFERENCE.test(reference) || reference.includes(':')) return '维修工单'
  return reference
}

export function maintenanceDeviceTitle(
  workOrder: BusinessConsoleMaintenanceWorkOrderItem,
  device?: MaintenanceDevice,
) {
  return (
    (typeof device?.displayName === 'string' ? device.displayName.trim() : '') ||
    (typeof device?.code === 'string' ? device.code.trim() : '') ||
    (workOrder.deviceAssetId ? '设备资料不可用' : '设备未标识')
  )
}

export function maintenanceDeviceLocation(device?: MaintenanceDevice) {
  if (!device) return '位置资料不可用'
  const parts = [
    device.siteCode,
    device.plantCode,
    device.workshopCode,
    device.lineCode,
    device.workCenterCode,
    device.stationCode,
  ]
    .map((part) => (typeof part === 'string' ? part.trim() : ''))
    .filter((part): part is string => Boolean(part))
    .filter((part, index, all) => all.indexOf(part) === index)
  return parts.length ? parts.join(' · ') : '位置未登记'
}

export function isMaintenanceTerminal(workOrder: BusinessConsoleMaintenanceWorkOrderItem) {
  return (
    workOrder.blockReasons?.includes('terminal-status') === true ||
    ['closed', 'cancelled'].includes(normalize(workOrder.status))
  )
}

export function formatMaintenanceDateTime(value?: string | null) {
  if (!value) return '时间未记录'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '时间未记录' : date.toLocaleString('zh-CN')
}
