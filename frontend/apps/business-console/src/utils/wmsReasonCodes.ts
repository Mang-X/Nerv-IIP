/**
 * WMS 拒绝原因代码 → 中文人话。
 *
 * 背景（#1397 / 第三轮走查台账 #81）：出库复核必 422，前端只能显示「请检查填写项」，
 * 用户既不知道卡在哪，也不知道下一步做什么。真因是 WMS 服务把拒绝理由压成了常量
 * `unprocessable`，理由只进服务端日志。
 *
 * 现在的契约（后端侧见 `WmsUnprocessableReasonCodes.cs`）：
 * - 后端承诺**稳定的 kebab 代码**（网关的下游消息护栏只放行 ASCII 代码，不放行自由文本）；
 * - 中文文案在这里映射，且必须满足三件事：**点名对象、说清卡在哪、给出下一步**。
 *
 * `errorData` 为什么始终是空数组：那是 FluentValidation 的字段袋，各服务的 error writer
 * 一律写死 `[]`；422 是领域拒绝而非字段校验，没有字段级条目，`message` 是唯一载体。
 * 所以修法是让 `message` 有内容，而不是去填 `errorData`。
 */

/** 供文案「点名对象」用的上下文。取不到就退化成不点名的通用句，但仍然给出路。 */
export interface WmsReasonContext {
  /** 出库单号，如 `OB-WQ-B-PICK-MIR-20260731-02`。 */
  outboundOrderNo?: string
  /** 拣货任务号，如 `WT-OB-WQ-B-PICK-MIR-20260731-02-01`。 */
  taskNo?: string
}

function subject(context: WmsReasonContext | undefined): string {
  const no = context?.outboundOrderNo?.trim()
  return no ? `出库单 ${no}` : '该出库单'
}

function taskSubject(context: WmsReasonContext | undefined): string {
  const no = context?.taskNo?.trim()
  return no ? `拣货任务 ${no}` : '该拣货任务'
}

/**
 * 拒绝原因代码 → 中文。
 *
 * 每条都写成「**卡在哪** + **去哪解**」，不写「请检查填写项」这类无信息量的句子。
 */
const REASON_MESSAGES: Record<string, (context?: WmsReasonContext) => string> = {
  'outbound-pack-review-not-passed': (context) =>
    `${subject(context)}的复核结论为「不通过」，不能据此完成出库。请勾选「复核通过」后提交；若确有问题，应走异常处理而不是完成出库。`,

  'outbound-picking-task-missing': (context) =>
    `${subject(context)}还没有拣货任务，复核缺少拣货事实。请先到「拣货任务」页为该出库单创建拣货任务并执行完成。`,

  'outbound-picking-not-completed': (context) =>
    `${subject(context)}的拣货任务尚未完成，复核要求先有拣货事实。请到「拣货任务」页把该出库单的任务开始并完成拣货，再回来复核。`,

  'outbound-picking-difference-reason-missing': (context) =>
    `${subject(context)}存在差异完成的拣货任务但没有差异原因。请到「拣货任务」页补填该任务的差异原因后再复核。`,

  'outbound-line-picking-task-missing': (context) =>
    `${subject(context)}有明细行没有对应的已完成拣货任务。请到「拣货任务」页为缺失的行补建并完成拣货任务，再回来复核。`,

  'picking-difference-reason-required': (context) =>
    `${taskSubject(context)}的拣货数量少于计划量，必须填写差异原因才能完成。请在「差异原因」里说明少拣的原因（如库存不足、货损）。`,

  'executed-quantity-out-of-range': (context) =>
    `${taskSubject(context)}的实拣数量超出计划量或为负数。请填写 0 到计划量之间的数量。`,

  // —— 403（作业范围 / 派工）——
  // 这些代码原本也被压成一句 "forbidden"，导致「这单派给别人了」和「不在你的作业范围」
  // 在界面上长得一模一样（台账 #82「页面也不指路」）。
  'resource-not-assigned-to-self': (context) =>
    `${subject(context)}已派给其他作业员，你不能代为执行。请让当班负责人改派给你，或由被指派人执行。`,

  'assignment-principal-mismatch': (context) =>
    `${subject(context)}的指派人与当前登录账号不一致。请刷新页面确认最新指派，或联系当班负责人改派。`,

  'resource-outside-selected-work-scope': (context) =>
    `${subject(context)}不在当前选择的作业范围内。请在页面顶部切换到该单所属的库区/站点后再操作。`,

  'missing-work-pool-assignment': (context) =>
    `${subject(context)}还没有分配作业池，无法执行。请先由当班负责人把它分配到对应的作业池。`,

  'resource-tenant-mismatch': () =>
    '该对象不属于当前组织或环境，无法操作。请确认顶部的组织/环境选择是否正确。',

  'missing-work-scope-kind': () => '本次操作没有带上作业范围，请在页面顶部选择库区/站点后重试。',

  'missing-work-scope-id': () => '本次操作没有带上作业范围，请在页面顶部选择库区/站点后重试。',
}

/**
 * 把 WMS 返回的原因代码翻成中文；不是已知代码就返回空串，交给调用方走原有的分层兜底。
 *
 * 之所以要 `trim()` 且大小写敏感：代码是后端的稳定契约常量，模糊匹配只会掩盖漂移
 * ——真出现未登记的新代码时，宁可落到兜底文案并在控制台留下原文，也好过猜错语义。
 */
export function wmsReasonMessage(rawMessage: unknown, context?: WmsReasonContext): string {
  if (typeof rawMessage !== 'string') return ''
  const resolve = REASON_MESSAGES[rawMessage.trim()]
  return resolve ? resolve(context) : ''
}

/** 已登记的代码清单，供契约测试与排查使用。 */
export const WMS_REASON_CODES = Object.keys(REASON_MESSAGES)
