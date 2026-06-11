/**
 * 设备运维（CMMS）+ 报警相关 code→中文 标签映射（纯 TS，框架无关，PC/PDA 共用）。
 *
 * 映射镜像 business-console `useBusinessEquipment` / equipment 页面的现有 code→中文 口径，
 * 确保 PDA 与 PC 文案一致。每个 label 函数大小写不敏感，未知/空值给中文兜底。
 */

function lookup(map: Record<string, string>, value: string | null | undefined, fallback: string): string {
  if (value == null) return fallback
  const normalized = value.trim().toLowerCase()
  if (normalized.length === 0) return fallback
  return map[normalized] ?? fallback
}

/** 报警级别（镜像 PC equipment/alarms.vue：critical/blocked/warning/info）。 */
export const alarmSeverityLabels: Record<string, string> = {
  critical: '严重',
  blocked: '阻塞',
  warning: '预警',
  info: '信息',
}

export function alarmSeverityLabel(value: string | null | undefined): string {
  return lookup(alarmSeverityLabels, value, '未知级别')
}

/** 设备运行状态（镜像 PC equipment/index.vue + [deviceAssetId].vue）。 */
export const equipmentStateLabels: Record<string, string> = {
  running: '运行中',
  idle: '空闲',
  down: '停机',
  faulted: '故障',
  offline: '离线',
  ready: '就绪',
  stopped: '停止',
}

export function equipmentStateLabel(value: string | null | undefined): string {
  return lookup(equipmentStateLabels, value, '未知状态')
}

/** 维修工单优先级（high/medium/low）。 */
export const maintenancePriorityLabels: Record<string, string> = {
  high: '高',
  medium: '中',
  low: '低',
}

export function maintenancePriorityLabel(value: string | null | undefined): string {
  return lookup(maintenancePriorityLabels, value, '未知优先级')
}

/** 维修工单状态（CMMS 生命周期：open→inProgress→completed/closed/cancelled）。 */
export const maintenanceWorkOrderStatusLabels: Record<string, string> = {
  open: '待处理',
  inprogress: '处理中',
  completed: '已完成',
  closed: '已关闭',
  cancelled: '已取消',
}

export function maintenanceWorkOrderStatusLabel(value: string | null | undefined): string {
  return lookup(maintenanceWorkOrderStatusLabels, value, '未知状态')
}

/** 点检结果（pass/fail）。 */
export const inspectionResultLabels: Record<string, string> = {
  pass: '通过',
  fail: '不通过',
}

export function inspectionResultLabel(value: string | null | undefined): string {
  return lookup(inspectionResultLabels, value, '未知结果')
}
