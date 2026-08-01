import type {
  BusinessConsoleMaintenanceWorkOrderItem,
  BusinessConsoleMasterDataResourceDetail,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'

type MaintenanceDevice = BusinessConsoleResourceItem | BusinessConsoleMasterDataResourceDetail

const STATUS_LABELS: Record<string, string> = {
  open: '待处理',
  accepted: '已接单',
  inprogress: '处理中',
  paused: '已暂停',
  waitingforparts: '等待备件',
  completed: '已完成',
  verified: '已验证',
  closed: '已关闭',
  cancelled: '已取消',
}

const PRIORITY_LABELS: Record<string, string> = {
  critical: '紧急',
  urgent: '紧急',
  high: '高',
  medium: '中',
  normal: '中',
  low: '低',
}

const ACTION_LABELS: Record<string, string> = {
  assign: '指派',
  accept: '接单',
  start: '开工',
  pause: '暂停',
  waitforparts: '等待备件',
  resume: '恢复',
  complete: '完成',
  verify: '验证',
  close: '关闭',
  cancel: '取消',
}

const BLOCK_REASON_LABELS: Record<string, string> = {
  'terminal-status': '工单已进入终态，仅可查看。',
  'completion-data-incomplete': '完工数据不完整，服务端未开放后续动作。',
  'unknown-status': '工单状态无法识别，服务端已禁止动作。',
  'assigned-technician-required': '仅当前指派的维修人员可执行该动作。',
  'manage-permission-required': '当前账号没有维护动作权限。',
  'work-scope-required': '当前账号不在允许执行动作的工作范围内。',
}

function normalize(value?: string | null) {
  return value?.trim().toLowerCase() ?? ''
}

export function maintenanceStatusLabel(value?: string | null) {
  return STATUS_LABELS[normalize(value)] ?? '未知状态'
}

export function maintenancePriorityText(value?: string | null) {
  return PRIORITY_LABELS[normalize(value)] ?? '未知优先级'
}

export function maintenanceActionLabel(value?: string | null) {
  return ACTION_LABELS[normalize(value)] ?? '未识别动作'
}

export function maintenanceBlockReasonLabel(value?: string | null) {
  return BLOCK_REASON_LABELS[value?.trim() ?? ''] ?? '服务端已禁止动作，原因暂不可识别。'
}

export function maintenanceDeviceTitle(
  workOrder: BusinessConsoleMaintenanceWorkOrderItem,
  device?: MaintenanceDevice,
) {
  return (
    device?.displayName?.trim() || device?.code?.trim() || workOrder.deviceAssetId || '设备未标识'
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
    .map((part) => part?.trim())
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
