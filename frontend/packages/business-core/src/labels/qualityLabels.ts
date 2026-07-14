/**
 * 质量检验相关 code→中文 标签映射（纯 TS，框架无关，PC/PDA 共用）。
 *
 * 镜像 Business Quality 服务的 code 口径：检验任务状态（pending/in-progress/completed）
 * 与来源类型（receiving/operation/final），确保 PDA 待检执行页与 PC 待检工作台文案一致
 * （MAN-457 / #811 与 console C3-1 / #801 同源）。每个 label 函数大小写不敏感，未知/空值给中文兜底。
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

/**
 * 检验任务状态（镜像 Quality `InspectionTaskStatuses`：pending/in-progress/completed）。
 * PDA 待检列表默认只呈现 pending（未检）；已检任务 status=completed。
 */
export const inspectionTaskStatusLabels: Record<string, string> = {
  'pending': '待检',
  'in-progress': '检验中',
  'completed': '已完成',
  'cancelled': '已取消',
}

export function inspectionTaskStatusLabel(value: string | null | undefined): string {
  return lookup(inspectionTaskStatusLabels, value, '未知状态')
}

/**
 * 检验任务来源类型（镜像 Quality `InspectionTask.SourceTypes`：receiving/operation/final）。
 * 用于待检列表的来源筛选 chips 与任务上下文展示。
 */
export const inspectionTaskSourceTypeLabels: Record<string, string> = {
  receiving: '来料检',
  operation: '过程检',
  final: '终检',
}

export function inspectionTaskSourceTypeLabel(value: string | null | undefined): string {
  return lookup(inspectionTaskSourceTypeLabels, value, '其他来源')
}

/** 来源类型有序表（筛选 chips 的候选，顺序即展示顺序）。 */
export const INSPECTION_TASK_SOURCE_TYPES: readonly string[] = ['receiving', 'operation', 'final']

/**
 * 检验记录权威结论（镜像 Quality `InspectionRecordResults`：passed/rejected/conditional-release）。
 * 用于检验记录详情/结果页的结论展示。
 */
export const inspectionRecordResultLabels: Record<string, string> = {
  'passed': '合格',
  'rejected': '不合格',
  'conditional-release': '条件放行',
}

export function inspectionRecordResultLabel(value: string | null | undefined): string {
  return lookup(inspectionRecordResultLabels, value, '未知结论')
}
