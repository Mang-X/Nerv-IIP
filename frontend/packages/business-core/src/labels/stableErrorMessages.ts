/**
 * 已发布的稳定错误 wire 值到用户文案的精确映射。
 *
 * 只负责展示，不归一化或猜测相似值；未知值返回空串，由各端保留原有兜底链。
 */
export const STABLE_ERROR_MESSAGES: Readonly<Record<string, string>> = {
  'stored-maintenance-work-order-receipt-is-invalid':
    '工单创建回执异常，请刷新后重试；仍失败请联系管理员。',
  'source-alarm-already-bound-to-a-different-create-intent':
    '该报警已关联其他维护工单，请刷新后核对。',
  'stored-maintenance-completion-receipt-is-invalid':
    '工单完工回执异常，请刷新后重试；仍失败请联系管理员。',
  'idempotency-conflict': '该操作标识已用于其他内容，请刷新后重新发起。',
  'lifecycle-conflict': '状态已被其他操作更新',
}

export function stableErrorMessage(value: unknown): string {
  return typeof value === 'string' ? (STABLE_ERROR_MESSAGES[value] ?? '') : ''
}
