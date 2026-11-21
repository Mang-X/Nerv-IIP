/**
 * 已发布的稳定错误 wire 值到用户文案的精确映射。
 *
 * 只负责展示，不归一化或猜测相似值；未知值返回空串，由各端保留原有兜底链。
 */
export const STABLE_ERROR_MESSAGES: Readonly<Record<string, string>> = {
  'barcode-resolve-page-invalid': '扫码查询的分页参数有误，请返回扫码页重试。',
  'mes-operation-read-forbidden': '当前账号没有查看工序任务的权限，请联系管理员。',
  'idempotency-key-required': '本次提交缺少操作标识，请刷新后重新发起。',
  'work-center-required-use-v2': '请在新版本停机登记中选择工作中心后重新提交。',
  'operation-task-outside-work-center': '该工序任务不属于所选工作中心，请核对任务和工作中心。',
  'worker-not-on-duty-at-work-center': '该员工当前未在此工作中心当班，请重新选择员工。',
  'finished-goods-receipt-not-found': '未找到完工入库申请，请刷新列表后核对。',
  'permission-denied': '当前账号没有执行该操作的权限，请联系管理员。',
  'approval-principal-unresolved': '无法确认当前审批人，请重新登录后重试。',
  'principal-unresolved': '无法确认当前账号，请重新登录后重试。',
  'maintenance-action-owner-required': '请由该维护工单的当前负责人执行此操作。',
  'assigned-inspector-not-found': '未找到所选检验员，请刷新人员列表后重新选择。',
  'assigned-team-not-active': '所选班组已停用，请选择在用班组。',
  'assigned-team-not-found': '未找到所选班组，请刷新班组列表后重新选择。',
  'assignment-target-outside-selected-scope': '所选分派对象不在当前作业范围内，请重新选择。',
  'resource-id-required': '请先选择要分配的作业记录。',
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
