// 排程失效的共享口径：失效原因码→中文标签，以及工序任务「排程状态」三态派生。
// MES 三处(工序执行/派工看板/工单详情)与 APS 排产工作台共用同一份，避免各页各写一份漂移。
// 事实基础：后端把失效表达为 OperationTask 生命周期状态 ScheduleInvalidated + 可选 reasonCode；
// APS 方案侧则由 gateway summary 的 isInvalidated / latestInvalidationReasonCode 携带。

export type ScheduleStatusKey = 'scheduled' | 'invalidated' | 'unscheduled'

export interface ScheduleStatusDisplay {
  key: ScheduleStatusKey
  label: string
  tone: 'info' | 'warning' | 'neutral'
}

const SCHEDULE_STATUS_DISPLAY: Record<ScheduleStatusKey, ScheduleStatusDisplay> = {
  scheduled: { key: 'scheduled', label: '已排程', tone: 'info' },
  invalidated: { key: 'invalidated', label: '已失效', tone: 'warning' },
  unscheduled: { key: 'unscheduled', label: '未排程', tone: 'neutral' },
}

// 与后端 SchedulingPlanInvalidationReasons 一一对应（restore 类事件同样代表排程前提变化、需重排）。
const REASON_LABELS: Record<string, string> = {
  equipmentunavailable: '设备不可用',
  equipmentrestored: '设备已恢复',
  devicestatechanged: '设备状态变化',
  materialreadinesschanged: '物料齐套变化',
  qualityblocked: '质量阻断',
  qualityreleased: '质量放行',
  workorderreleased: '工单已下达',
}

/** 失效原因码→中文标签；未知码回退原值，空值回退占位。 */
export function describeScheduleInvalidationReason(reasonCode?: string | null): string {
  const raw = (reasonCode ?? '').trim()
  if (!raw) return '排程前提已变化'
  return REASON_LABELS[raw.toLowerCase()] ?? raw
}

/** 失效行的完整引导文案（tooltip/说明用）。 */
export function scheduleInvalidationHint(reasonCode?: string | null): string {
  const raw = (reasonCode ?? '').trim()
  return raw
    ? `排程已失效：${describeScheduleInvalidationReason(raw)}，需重新排程。`
    : '排程已失效，需重新排程。'
}

/**
 * 由工序任务的生命周期状态 + 排程专属事实 `scheduledAtUtc` 派生「排程状态」三态：
 * - 已失效：status 为 scheduleInvalidated（最高优先，覆盖其它）
 * - 已排程：`scheduledAtUtc` 非空——只有已下达 APS 方案（ApplyScheduleAssignment）才写它
 * - 未排程：其余（含仅人工派工/未经排程的任务）
 * 只用 `scheduledAtUtc`：它是后端排程专属字段，人工派工（Assign 写 assignedAtUtc）不会置它，
 * 因此不会把「人工指派但未排程」误报成已排程；也不用 plannedStartUtc（=路由 earliestStartUtc，恒有值）。
 */
export function resolveScheduleStatus(row: {
  status?: string | null
  scheduledAtUtc?: string | null
}): ScheduleStatusDisplay {
  const status = (row.status ?? '').trim().toLowerCase()
  if (status === 'scheduleinvalidated') return SCHEDULE_STATUS_DISPLAY.invalidated
  if (row.scheduledAtUtc) return SCHEDULE_STATUS_DISPLAY.scheduled
  return SCHEDULE_STATUS_DISPLAY.unscheduled
}

export function isScheduleInvalidated(status?: string | null): boolean {
  return (status ?? '').trim().toLowerCase() === 'scheduleinvalidated'
}
