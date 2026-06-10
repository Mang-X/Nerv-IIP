import type { ChangeType, ConflictReason, ConflictSeverity } from './types'

export type StatusTone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

// 业务语言映射:UI 只出现这些中文,绝不暴露 reasonCode / changeType 等工程枚举。
export const conflictReasonLabel: Record<ConflictReason, string> = {
  dueDate: '交期风险',
  capacity: '产能不足',
  calendar: '日历冲突',
  material: '物料未齐套',
  quality: '质量限制',
  equipment: '设备不可用',
  noEligibleResource: '无可用资源',
  outsideHorizon: '超出排程范围',
  invalidLockedAssignment: '锁定无效',
  predecessorUnscheduled: '前序未排产',
}

export const changeTypeLabel: Record<ChangeType, string> = {
  added: '新增',
  moved: '已移动',
  delayed: '已延后',
  preserved: '保持不变',
  blocked: '受阻',
}

export const severityTone: Record<ConflictSeverity, StatusTone> = {
  info: 'info',
  warning: 'warning',
  error: 'danger',
}

export const changeTone: Record<ChangeType, StatusTone> = {
  added: 'success',
  moved: 'info',
  delayed: 'warning',
  preserved: 'neutral',
  blocked: 'danger',
}
