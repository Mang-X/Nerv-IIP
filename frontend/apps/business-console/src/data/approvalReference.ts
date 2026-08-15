/**
 * 审批域 · 前端受控值常量。
 *
 * 后端 `documentType` 存自由字符串、无对应 CodeSet，所以由前端集中固定，避免手输拼写漂移
 * 导致模板挂不到单据上。**只列平台真会发起审批的单据类型**（当前由
 * `BusinessDocumentApprovalPanel` 接入的工程变更、质量不合格，以及审批服务已种子化的采购订单），
 * 不列平台并不会发起审批的单据，免得在 UI 上伪造尚不存在的闭环。
 */
import type { RefOption } from './masterDataReference'

/**
 * NCR 处置审批的 `documentType` —— **权威码值**，与后端
 * `Nerv.IIP.Contracts.Approval/ApprovalDocumentTypes.NcrDisposition` 逐字一致。
 *
 * 收敛理由（#1327）：此前处置面发的是 `quality-ncr`，而种子模板挂的是 `ncr-disposition`、
 * Quality 服务的审批白名单又只认 `quality-ncr`——三方各写各的字面量，种子态下
 * 处置审批既选不到模板也判不通过。取 `ncr-disposition` 是因为它才是已落库事实
 * （模板 / 历史链 / 委托）用的码值，且语义就是被审对象「不合格品处置」。
 */
export const NCR_DISPOSITION_DOCUMENT_TYPE = 'ncr-disposition'
export const ENGINEERING_CHANGE_ORDER_DOCUMENT_TYPE = 'engineering-change-order'
export const ENGINEERING_CHANGE_ORDER_TEMPLATE_CODE = 'APT-WB-ECO-001'

export const APPROVAL_DOCUMENT_TYPE_OPTIONS: RefOption[] = [
  { value: ENGINEERING_CHANGE_ORDER_DOCUMENT_TYPE, label: '工程变更单' },
  { value: NCR_DISPOSITION_DOCUMENT_TYPE, label: '不合格品处置' },
  { value: 'purchase-order', label: '采购订单' },
]

/**
 * 裁决动作的**唯一权威取值**（小写），与后端
 * `Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs` 的
 * `ApprovalDecisions`（approve / reject / return）逐字对齐。
 *
 * 为什么要集中成常量而不是各处写字面量：Gateway 契约里 `decision` 是自由 `string`
 * （`types.gen.ts` → `...ResolveApprovalStepRequest.decision?: string`），**类型层拦不住**
 * 拼写与大小写漂移。审批中心此前发的是 `'Approve' / 'Reject' / 'Resolve'`，一切裁决必 400
 * （#1311）——其中 `Resolve` 更是后端从未支持过的值。取值收敛到这里 + 下面的联合类型，
 * 才能让「发错值」变成 typecheck 期错误而不是运行期 400。
 */
export const APPROVAL_DECISION_VALUES = ['approve', 'reject', 'return'] as const

export type ApprovalDecisionValue = (typeof APPROVAL_DECISION_VALUES)[number]

/** 裁决动作的按钮/标题措辞（动作视角，与决策记录里的状态词表 `APPROVAL_DECISION_LABELS` 分工不同）。 */
export const APPROVAL_DECISION_ACTION_LABELS: Readonly<Record<ApprovalDecisionValue, string>> = {
  approve: '通过',
  reject: '驳回',
  return: '退回',
}
