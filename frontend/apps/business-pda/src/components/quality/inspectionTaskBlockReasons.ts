const INSPECTION_TASK_BLOCK_MESSAGES = {
  'task-completed': '任务已完成，仅可查看。',
  'task-unassigned': '任务尚未派工，无法领取。',
  'task-already-claimed': '任务已由其他检验员领取。',
  'task-assigned-to-another-inspector': '任务已派给其他检验员，无法领取。',
  'task-assigned-to-another-team': '任务已派给其他班组，无法领取。',
  'task-outside-selected-work-scope': '任务不在当前工作范围内，无法领取。',
} as const

export type InspectionTaskBlockReason = keyof typeof INSPECTION_TASK_BLOCK_MESSAGES

export function isInspectionTaskBlockReason(reason: unknown): reason is InspectionTaskBlockReason {
  return typeof reason === 'string' && reason in INSPECTION_TASK_BLOCK_MESSAGES
}

export function inspectionTaskBlockReasonMessage(reason: string | undefined) {
  return isInspectionTaskBlockReason(reason)
    ? INSPECTION_TASK_BLOCK_MESSAGES[reason]
    : '当前任务不可领取，请刷新后重试。'
}

export class InspectionTaskClaimBlockedError extends Error {
  readonly reason: InspectionTaskBlockReason

  constructor(reason: InspectionTaskBlockReason) {
    super(inspectionTaskBlockReasonMessage(reason))
    this.name = 'InspectionTaskClaimBlockedError'
    this.reason = reason
  }
}
