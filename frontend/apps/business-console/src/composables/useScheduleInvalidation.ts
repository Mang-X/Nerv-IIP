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
 * 由工序任务的生命周期状态 + 排程分配时间派生「排程状态」三态：
 * - 已失效：status 为 scheduleInvalidated（最高优先，覆盖其它）
 * - 未排程：仍在 queued 且从未被排程/派工分配（assignedAtUtc 为空）
 * - 已排程：其余（已排程分配、执行中、暂停、完成等）
 * 不用 plannedStartUtc 判定——它取自路由 earliestStartUtc，恒有值。
 */
export function resolveScheduleStatus(row: {
  status?: string | null
  assignedAtUtc?: string | null
}): ScheduleStatusDisplay {
  const status = (row.status ?? '').trim().toLowerCase()
  if (status === 'scheduleinvalidated') return SCHEDULE_STATUS_DISPLAY.invalidated
  if (status === 'queued' && !row.assignedAtUtc) return SCHEDULE_STATUS_DISPLAY.unscheduled
  return SCHEDULE_STATUS_DISPLAY.scheduled
}

export function isScheduleInvalidated(status?: string | null): boolean {
  return (status ?? '').trim().toLowerCase() === 'scheduleinvalidated'
}
