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
  // #3333：网关**校验层**（FastEndpoints 的 DTO 校验器 + 模型绑定失败）的统一稳定码。
  // 此前这条通道走 FastEndpoints 默认形状，顶层 message 是英文常量
  // `One or more errors occurred!`——两端都只读顶层 message，于是那句英文直接上屏。
  // 现在网关把它成形为与 KnownException 同一个信封并放这条码（后端权威：
  // `BusinessGatewayValidationErrorResponse.StableErrorCode`）。
  //
  // ⚠️ 文案边界（别读成「已经能显示具体哪个字段错了」）：这条码是**整条请求被校验拒绝**的
  // 唯一码，不携带字段身份。逐字段原因在响应的 `errorData` 里，但**没有任何前端位点消费它**，
  // 也不该由这张表承载——那张袋子里的句子是 FluentValidation 按 UI 文化出的默认句
  // （属性名仍是英文驼峰，如「'idempotency Key' 必须小于或等于128个字符」），
  // 不是可交给操作工的可行动文案。字段级中文文案是另一票。
  'request-payload-invalid': '提交的内容有误，请检查后重新提交；仍失败请联系管理员。',
  'lifecycle-conflict': '状态已被其他操作更新',
  // #3155：以下四条此前**没有任何一侧登记**，后端一直在发、前端一直不认，所以直接裸码上屏。
  // 它们现在与上面各条一样受跨语言契约约束：后端注册表里的每个码必须在本表登记，由
  // `scripts/verify-stable-code-frontend-vocabulary.ps1` 在 CI 强制（方向是后端 ⊆ 本表）。
  //
  // 网关幂等键钳的第三条（另两条在上面）。语义是「同一次请求里出现了两个互不相同的幂等键」，
  // 不是「这个键被别人用过」——后者是 `idempotency-conflict`。两句文案刻意分开：
  // PC 的 notify.ts 通用正则按子串 `idempotency` 命中会把两者都说成「操作意图发生冲突」，
  // 那对本条是误导（真正要做的是刷新页面重发，而不是去查键的历史用途）。
  'idempotency-key-mismatch': '本次请求的操作标识前后不一致，请刷新页面后重新发起。',
  // WMS 拒绝路径的两条**兜底**码（`WmsLifecycleConflictMiddleware`）。中段那些更具体的
  // kebab 原因码（如 `resource-not-assigned-to-self`）由 `SafeOutboundCode` 原样外发，
  // 它们不是常量声明、不在契约扫描面内——见检查器头部的边界声明，别读成「WMS 全部拒绝原因都已登记」。
  forbidden: '没有执行该操作的权限，请联系管理员确认你的作业范围。',
  unprocessable: '当前数据不满足该操作的前置条件，请刷新后核对。',
  // MES `MesRoutingSnapshotMissingException` 经 `MesLifecycleConflictMiddleware` 外发时，
  // 信封 message 位是**裸码**（KnownException 那条路径才带中文），故必须在本表登记。
  ROUTING_SNAPSHOT_MISSING: '工单缺少已发布生产版本的工艺路线快照，请先维护并发布生产版本。',
}

export function stableErrorMessage(value: unknown): string {
  return typeof value === 'string' ? (STABLE_ERROR_MESSAGES[value] ?? '') : ''
}
