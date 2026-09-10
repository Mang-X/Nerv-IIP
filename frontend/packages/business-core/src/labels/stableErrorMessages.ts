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
  // #3287：网关对幂等键入参形状的两条拒绝（400）。两端都必须登记在这张表里，否则
  // PDA 会把裸英文码甩到操作工屏上（400 在 actionableHttpMessage 里没有本地文案，
  // 回落链是 actionableMessage ?? serverMessage ?? fallback），而 PC 的
  // notify.ts 通用正则按**子串** `idempotency` 命中，会回「操作意图发生冲突」——
  // 正是这两条错误码要消灭的那句误导。
  // 文案刻意不写死长度上界：那个数由网关的 MaximumLength 拥有，抄到前端就是又一处
  // 会漂移的手抄上界（#3176 / #3228 / #3229 / #3281 同族）。
  'idempotency-key-too-long': '操作标识过长，本次未提交；请重新发起，仍失败请联系管理员。',
  'idempotency-key-invalid-characters':
    '操作标识含不支持的字符，本次未提交；请重新发起，仍失败请联系管理员。',
  'lifecycle-conflict': '状态已被其他操作更新',
}

export function stableErrorMessage(value: unknown): string {
  return typeof value === 'string' ? (STABLE_ERROR_MESSAGES[value] ?? '') : ''
}
