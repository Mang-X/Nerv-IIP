/**
 * 业务码值 → 中文显示名的集中词表。
 *
 * 为什么集中：这些码值散落在多个域的读面里（审批、库存、质量、MES、WMS、编码规则…），
 * 以前每页各自 `labels[x] ?? x` 兜底，词表漏一个就把英文码印到界面上。集中一份后
 * 补词只需改这里，不用满仓库找。
 *
 * 约定：
 * - key 一律小写；查表前用 `normalizeCode` 归一（兼容后端 kebab-case 与 PascalCase 两种写法）。
 * - 查不到时由调用方决定说法，`labelFor` 默认原样返回，避免把「没值」和「词表漏了」混为一谈。
 * - 状态徽标（NvStatusBadge）的通用状态词表在 `@nerv-iip/ui` 内，这里只放它覆盖不到的业务码值。
 */

/** 把 `PartiallyReceived` / `partially-received` / `PARTIALLY_RECEIVED` 归一成 `partially-received`。 */
export function normalizeCode(value?: string | null): string {
  if (!value) return ''
  return value
    .trim()
    .replace(/([a-z\d])([A-Z])/g, '$1-$2')
    .replace(/[\s_]+/g, '-')
    .toLowerCase()
}

/** 按词表取中文名；查不到默认原样返回（调用方可传 fallback 改为占位）。 */
export function labelFor(
  dictionary: Readonly<Record<string, string>>,
  value?: string | null,
  fallback?: string,
): string {
  if (!value) return fallback ?? ''
  return dictionary[normalizeCode(value)] ?? fallback ?? value
}

/** 审批单据类型（审批中心 documentType）。 */
export const DOCUMENT_TYPE_LABELS: Readonly<Record<string, string>> = {
  'purchase-order': '采购订单',
  'purchase-requisition': '采购申请',
  'purchase-receipt': '采购收货',
  'sales-order': '销售订单',
  'sales-quotation': '销售报价',
  'delivery-order': '发货单',
  'work-order': '生产工单',
  'ncr-disposition': '不合格品处置',
  'engineering-change-order': '工程变更',
  'inventory-count': '库存盘点',
  'maintenance-work-order': '维修工单',
}

/** 审批决定。 */
export const APPROVAL_DECISION_LABELS: Readonly<Record<string, string>> = {
  approve: '同意',
  approved: '已同意',
  reject: '驳回',
  rejected: '已驳回',
  abstain: '弃权',
  delegate: '转办',
  withdraw: '撤回',
}

/** 库存质量状态（StockQualityStatus）。 */
export const QUALITY_STATUS_LABELS: Readonly<Record<string, string>> = {
  unrestricted: '无限制',
  quality: '待检',
  quarantine: '隔离',
  hold: '冻结',
  blocked: '冻结',
  scrapped: '已报废',
  'conditional-release': '条件放行',
  returned: '已退货',
}

/** 库存货主类型（ownerType）。 */
export const STOCK_OWNER_TYPE_LABELS: Readonly<Record<string, string>> = {
  company: '本公司',
  customer: '客户寄售',
  supplier: '供应商寄售',
  subcontractor: '外协方',
}

/** 不合格品处置方式（defaultDisposition / disposition）。 */
export const DISPOSITION_LABELS: Readonly<Record<string, string>> = {
  rework: '返工',
  repair: '返修',
  scrap: '报废',
  'return-to-supplier': '退供应商',
  'conditional-release': '条件放行',
  'use-as-is': '让步接收',
  regrade: '降级使用',
}

/** 追溯节点类型（MES 追溯图 nodeType）。 */
export const TRACE_NODE_TYPE_LABELS: Readonly<Record<string, string>> = {
  'work-order': '生产工单',
  'operation-task': '工序任务',
  'material-lot': '投入批次',
  'produced-lot': '产出批次',
  'received-lot': '收货批次',
  'produced-serial': '产出序列号',
  'consumed-serial': '消耗序列号',
  shipment: '发货',
  inspection: '检验记录',
}

/** 报工候选挂起原因（TelemetryProductionReportCandidate.suspensionReason）。 */
export const REPORT_SUSPENSION_REASON_LABELS: Readonly<Record<string, string>> = {
  'active-alarm': '设备存在未处理报警',
  'no-work-center-mapping': '设备未绑定工作中心',
  'no-current-work-order': '工作中心当前无在制工单',
  'no-operation-task': '工作中心当前无进行中的工序任务',
  'signal-stale': '采集信号已过期',
}

/** 质量数据来源类型（sourceType）。 */
export const QUALITY_SOURCE_TYPE_LABELS: Readonly<Record<string, string>> = {
  'in-process': '过程检验',
  operation: '工序检验',
  'mes-operation': '工序报工',
  incoming: '来料检验',
  final: '成品检验',
  audit: '审核抽检',
}

/** 编码规则版本状态。 */
export const CODE_RULE_VERSION_LABELS: Readonly<Record<string, string>> = {
  draft: '草稿',
  published: '已发布',
  superseded: '已被替代',
  archived: '已归档',
}
