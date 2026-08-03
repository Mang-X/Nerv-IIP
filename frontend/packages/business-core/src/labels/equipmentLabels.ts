/**
 * 设备运维（CMMS）+ 报警相关 code→中文 标签映射（纯 TS，框架无关，PC/PDA 共用）。
 *
 * 映射镜像 business-console `useBusinessEquipment` / equipment 页面的现有 code→中文 口径，
 * 确保 PDA 与 PC 文案一致。每个 label 函数大小写不敏感，未知/空值给中文兜底。
 */

function lookup(
  map: Record<string, string>,
  value: string | null | undefined,
  fallback: string,
): string {
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

/**
 * 报警生命周期状态（镜像 IndustrialTelemetry `AlarmEvent.Status`：raised/acknowledged/shelved/cleared）。
 * PDA 报警确认/搁置（MAN-456）与 PC 共用同一 code→中文 口径。
 */
export const alarmLifecycleStatusLabels: Record<string, string> = {
  raised: '未确认',
  acknowledged: '已确认',
  shelved: '已搁置',
  cleared: '已清除',
}

export function alarmLifecycleStatusLabel(value: string | null | undefined): string {
  return lookup(alarmLifecycleStatusLabels, value, '未知状态')
}

/**
 * 报警列表排序权重：未确认 > 已搁置 > 已确认 > 已清除（MAN-456 交互稿）。
 * 数值越小越靠前；未知状态排在已知状态之后、已清除之前，避免吞掉待处理项。
 */
export function alarmLifecycleSortWeight(value: string | null | undefined): number {
  const normalized = value?.trim().toLowerCase()
  switch (normalized) {
    case 'raised':
      return 0
    case 'shelved':
      return 1
    case 'acknowledged':
      return 2
    case 'cleared':
      return 4
    default:
      return 3
  }
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

/** 维修工单生产优先级（人工建单 + 报警自动开单等来源）。 */
export const maintenancePriorityLabels: Record<string, string> = {
  critical: '紧急',
  urgent: '紧急',
  high: '高',
  medium: '中',
  normal: '中',
  low: '低',
  planned: '计划保养',
}

export function maintenancePriorityLabel(value: string | null | undefined): string {
  return lookup(maintenancePriorityLabels, value, '未知优先级')
}

/** 维修工单状态目录（Maintenance MAN-631 完整生命周期，供筛选与展示共用）。 */
export const maintenanceWorkOrderStatusOptions = [
  { value: 'open', label: '待处理' },
  { value: 'accepted', label: '已接单' },
  { value: 'inProgress', label: '处理中' },
  { value: 'paused', label: '已暂停' },
  { value: 'waitingForParts', label: '等待备件' },
  { value: 'completed', label: '已完成' },
  { value: 'verified', label: '已验证' },
  { value: 'closed', label: '已关闭' },
  { value: 'cancelled', label: '已取消' },
] as const

export type MaintenanceWorkOrderStatusCode =
  (typeof maintenanceWorkOrderStatusOptions)[number]['value']

export function isMaintenanceWorkOrderStatusCode(
  value: unknown,
): value is MaintenanceWorkOrderStatusCode {
  return (
    typeof value === 'string' &&
    maintenanceWorkOrderStatusOptions.some((option) => option.value === value)
  )
}

export function normalizeMaintenanceWorkOrderStatusFilter(
  value: unknown,
): '' | MaintenanceWorkOrderStatusCode {
  const normalized = typeof value === 'string' ? value.trim() : ''
  return isMaintenanceWorkOrderStatusCode(normalized) ? normalized : ''
}

export const maintenanceWorkOrderStatusLabels: Record<string, string> = Object.fromEntries(
  maintenanceWorkOrderStatusOptions.map(({ value, label }) => [value.toLowerCase(), label]),
)

export function maintenanceWorkOrderStatusLabel(value: string | null | undefined): string {
  return lookup(maintenanceWorkOrderStatusLabels, value, '未知状态')
}

/** Maintenance 聚合支持的完整生命周期动作。 */
export const maintenanceWorkOrderActionLabels: Record<string, string> = {
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

export function maintenanceWorkOrderActionLabel(value: string | null | undefined): string {
  return lookup(maintenanceWorkOrderActionLabels, value, '未识别动作')
}

/** Maintenance 服务与 BusinessGateway 返回的动作阻塞原因。 */
export const maintenanceWorkOrderBlockReasonLabels: Record<string, string> = {
  'terminal-status': '工单已进入终态，仅可查看。',
  'completion-data-incomplete': '完工数据不完整，当前不可执行后续动作。',
  'unknown-status': '工单状态无法识别，当前不可执行操作。',
  'assigned-technician-required': '仅当前指派的维修人员可执行该动作。',
  'manage-permission-required': '当前账号没有维护动作权限。',
  'work-scope-required': '当前账号缺少执行动作所需的工作范围。',
  'work-scope-not-authorized': '当前账号未获授权进入执行动作所需的工作范围。',
}

export function maintenanceWorkOrderBlockReasonLabel(value: string | null | undefined): string {
  return lookup(
    maintenanceWorkOrderBlockReasonLabels,
    value,
    '当前不可执行操作，具体原因暂不可识别。',
  )
}

/** 点检结果（pass/fail）。 */
export const inspectionResultLabels: Record<string, string> = {
  pass: '通过',
  fail: '不通过',
}

export function inspectionResultLabel(value: string | null | undefined): string {
  return lookup(inspectionResultLabels, value, '未知结果')
}
