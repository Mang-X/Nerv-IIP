/**
 * 已发布的稳定错误 wire 值到用户文案的精确映射。
 *
 * 只负责展示，不归一化或猜测相似值；未知值返回空串，由各端保留原有兜底链。
 */
export const STABLE_ERROR_MESSAGES: Readonly<Record<string, string>> = {
  'work-scope-not-authorized': '当前账号未获授权操作此工作范围，请切换到已授权范围或联系管理员。',
  'replay-window-expired': '模板资产退役的重放窗口已过期，不能重新执行或复用该资产。',
  'stored-maintenance-work-order-receipt-is-invalid':
    '工单创建回执异常，请刷新后重试；仍失败请联系管理员。',
  'source-alarm-already-bound-to-a-different-create-intent':
    '该报警已关联其他维护工单，请刷新后核对。',
  'stored-maintenance-completion-receipt-is-invalid':
    '工单完工回执异常，请刷新后重试；仍失败请联系管理员。',
  'idempotency-conflict': '该操作标识已用于其他内容，请刷新后重新发起。',
  // 键长度上界由网关维护。
  'idempotency-key-too-long': '操作标识过长，本次未提交；请重新发起，仍失败请联系管理员。',
  'idempotency-key-invalid-characters':
    '操作标识含不支持的字符，本次未提交；请重新发起，仍失败请联系管理员。',
  'request-payload-invalid': '提交的内容有误，请检查后重新提交；仍失败请联系管理员。',
  'lifecycle-conflict': '状态已被其他操作更新',
  'downtime-reason-code-already-exists':
    '停机原因编码已存在，请换一个编码，或到停机原因页编辑原有原因。',
  'idempotency-key-mismatch': '本次请求的操作标识前后不一致，请刷新页面后重新发起。',
  forbidden: '没有执行该操作的权限，请联系管理员确认你的作业范围。',
  unprocessable: '当前数据不满足该操作的前置条件，请刷新后核对。',
  ROUTING_SNAPSHOT_MISSING: '工单缺少已发布生产版本的工艺路线快照，请先维护并发布生产版本。',
  // 熔断拒绝发生于转发之前，可以确认本次请求未发出。
  'downstream-circuit-open': '服务暂时不可用，本次请求未发出；请稍后重试。',
  'production-serial-policy-invalid':
    '当前物料的序列号策略不可用于生产报工，请联系管理员检查物料配置。',
  'production-label-template-required': '该物料按生产环节生成序列号，请选择标签模板后重试。',
  'production-label-rule-unavailable':
    '未找到可用于该物料的有效条码规则，请联系管理员检查物料配置。',
  'production-label-quantity-invalid': '按生产环节生成序列号时，良品数量必须为正整数。',
}

export function stableErrorMessage(value: unknown): string {
  return typeof value === 'string' ? (STABLE_ERROR_MESSAGES[value] ?? '') : ''
}
