/**
 * 审批域 · 前端受控值常量。
 *
 * 后端 `documentType` 存自由字符串、无对应 CodeSet，所以由前端集中固定，避免手输拼写漂移
 * 导致模板挂不到单据上。**只列平台真会发起审批的单据类型**（当前由
 * `BusinessDocumentApprovalPanel` 接入的工程变更、质量不合格，以及审批服务已种子化的采购订单），
 * 不列平台并不会发起审批的单据，免得在 UI 上伪造尚不存在的闭环。
 */
import type { RefOption } from './masterDataReference'

export const APPROVAL_DOCUMENT_TYPE_OPTIONS: RefOption[] = [
  { value: 'engineering-change-order', label: '工程变更单' },
  { value: 'quality-ncr', label: '质量不合格单' },
  { value: 'purchase-order', label: '采购订单' },
]
