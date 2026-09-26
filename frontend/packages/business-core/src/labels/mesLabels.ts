/**
 * MES 一线作业的可读中文状态标签（框架无关，纯 TS）。
 *
 * 这些映射原先在 4 个 PDA MES 页面（工序执行 / 报工 / 领料 / 完工入库）各自重复维护。
 * 集中到 business-core 后，页面只消费函数，不再复制标签表；渲染文案对每个已处理状态保持不变。
 *
 * 约定：未知/缺失状态一律回落到 `未知状态`，与各页面原有兜底一致——不向一线暴露原始状态码。
 */

const UNKNOWN_STATUS_LABEL = '未知状态'

/** 工单状态可读标签（report / issue / receipt 页面共用，原本三份相同副本）。 */
export const WORK_ORDER_STATUS_LABELS: Record<string, string> = {
  Created: '待下达',
  Released: '已下达',
  Planned: '已计划',
  InProgress: '生产中',
  Started: '生产中',
  Hold: '已挂起',
  OnHold: '已挂起',
  Completed: '已完成',
  Closed: '已关闭',
  Cancelled: '已取消',
  Scrapped: '已报废',
  Split: '已拆分',
  Merged: '已合并',
}

// MES 工单读面回显的是域常量的小写码（`created` / `released` …），按不区分大小写查表。
const WORK_ORDER_STATUS_LABELS_BY_CODE = new Map(
  Object.entries(WORK_ORDER_STATUS_LABELS).map(([code, label]) => [code.toLowerCase(), label]),
)

export function workOrderStatusLabel(status?: string | null): string {
  return WORK_ORDER_STATUS_LABELS_BY_CODE.get((status ?? '').toLowerCase()) ?? UNKNOWN_STATUS_LABEL
}

/** 工序任务状态可读标签（operation / report 页面共用，原本两份相同副本）。 */
export const OPERATION_TASK_STATUS_LABELS: Record<string, string> = {
  Queued: '待开工',
  Ready: '可开工',
  Running: '执行中',
  Started: '执行中',
  InProgress: '执行中',
  Paused: '已暂停',
  Held: '已暂停',
  ScheduleInvalidated: '排程已失效',
  Completed: '已完成',
  Cancelled: '已取消',
  Blocked: '受阻',
}

export function operationTaskStatusLabel(status?: string | null): string {
  return OPERATION_TASK_STATUS_LABELS[status ?? ''] ?? UNKNOWN_STATUS_LABEL
}

/** 领料申请状态可读标签（issue 页面专用状态集，集中存放一处便于维护）。 */
export const MATERIAL_ISSUE_STATUS_LABELS: Record<string, string> = {
  Requested: '待领料',
  Pending: '待领料',
  Issued: '已发料',
  PartiallyReceived: '部分接收',
  Received: '已接收',
  Confirmed: '已接收',
  Completed: '已完成',
  Cancelled: '已取消',
  Rejected: '已驳回',
}

export function materialIssueStatusLabel(status?: string | null): string {
  return MATERIAL_ISSUE_STATUS_LABELS[status ?? ''] ?? UNKNOWN_STATUS_LABEL
}

/** 完工入库申请状态可读标签（receipt 页面专用状态集，集中存放一处便于维护）。 */
export const RECEIPT_STATUS_LABELS: Record<string, string> = {
  Requested: '待入库',
  Pending: '待入库',
  Created: '待入库',
  Submitted: '待入库',
  PartiallyReceived: '部分入库',
  Received: '已入库',
  Completed: '已入库',
  Cancelled: '已取消',
  Rejected: '已驳回',
}

export function receiptStatusLabel(status?: string | null): string {
  return RECEIPT_STATUS_LABELS[status ?? ''] ?? UNKNOWN_STATUS_LABEL
}

/**
 * 工单行的可读标题/副标题（report / issue / receipt 共用，原本三份相同副本）。
 *
 * 仅依赖普通行字段（`workOrderId` / `status` / `skuId` / `quantity`），框架无关。
 */
export interface WorkOrderLabelRow {
  workOrderId?: string | null
  status?: string | null
  skuId?: string | null
  quantity?: number | null
}

/**
 * 工序的上屏称呼：一律按工艺序号「工序 10」，不显示工序任务号——返工工单的任务号形如
 * `OPT-0000-<GUID>`，是内部号。取不到序号时说明缺失，不回落到任务号。
 */
export function operationSequenceLabel(sequence?: number | null): string {
  return sequence === undefined || sequence === null ? '工序信息未提供' : `工序 ${sequence}`
}

export function workOrderTitle(wo: WorkOrderLabelRow): string {
  return wo.workOrderId ?? '无工单'
}

export function workOrderSubtitle(wo: WorkOrderLabelRow): string {
  const parts = [workOrderStatusLabel(wo.status)]
  if (wo.skuId) parts.push(`物料 ${wo.skuId}`)
  if (wo.quantity !== undefined && wo.quantity !== null) parts.push(`计划 ${wo.quantity}`)
  return parts.join(' · ')
}

/**
 * 班次交接单状态可读标签。
 *
 * 取值权威是 MES 域 `ShiftHandover.OpenStatus` / `AcceptedStatus`（`Open` / `Accepted`），
 * 读面按枚举名回显字符串。`open` 在交接语境是「待接班」而不是通用的「待处理」。
 */
export const SHIFT_HANDOVER_STATUS_LABELS: Record<string, string> = {
  open: '待接班',
  accepted: '已接班',
}

export function shiftHandoverStatusLabel(status?: string | null): string {
  return SHIFT_HANDOVER_STATUS_LABELS[normalizeHandoverCode(status)] ?? UNKNOWN_STATUS_LABEL
}

/**
 * 遗留问题来源域。取值权威是 MES 域枚举 `ShiftHandoverIssueCategory`（Equipment / Quality）；
 * 写面按 `ShiftHandoverVocabulary.ParseCategory` 大小写不敏感解析，因此这里按小写归一后查表。
 */
export const SHIFT_HANDOVER_ISSUE_CATEGORY_LABELS: Record<string, string> = {
  equipment: '设备',
  quality: '质量',
}

/** 写面可提交的类别码（与域枚举同名，写面收字符串）。 */
export const SHIFT_HANDOVER_ISSUE_CATEGORY_CODES = ['Equipment', 'Quality'] as const

export function shiftHandoverIssueCategoryLabel(category?: string | null): string {
  return SHIFT_HANDOVER_ISSUE_CATEGORY_LABELS[normalizeHandoverCode(category)] ?? '未分类'
}

/** 遗留问题严重度。取值权威是 MES 域枚举 `ShiftHandoverIssueSeverity`（Low / Medium / High）。 */
export const SHIFT_HANDOVER_ISSUE_SEVERITY_LABELS: Record<string, string> = {
  low: '低',
  medium: '中',
  high: '高',
}

/** 写面可提交的严重度码（与域枚举同名，写面收字符串）。 */
export const SHIFT_HANDOVER_ISSUE_SEVERITY_CODES = ['Low', 'Medium', 'High'] as const

/**
 * 解不出时用 `未知级别`，与本模块 `alarmSeverityLabel` 的既有口径一致。
 *
 * console 侧那份副本用的是 `未定级`、本文件早先写的是 `未分级`——**两个都是离群值**，
 * 同一 business-core 里已经有一个严重度回落文案在用 `未知级别`。副本收拢（#3470）以这份为准。
 */
export function shiftHandoverIssueSeverityLabel(severity?: string | null): string {
  return SHIFT_HANDOVER_ISSUE_SEVERITY_LABELS[normalizeHandoverCode(severity)] ?? '未知级别'
}

/**
 * 未完工单状态的**写面值域** —— 与读面展示表 `WORK_ORDER_STATUS_LABELS` 是两件事。
 *
 * 读面表回答「拿到这个码怎么显示」，所以它是历史拼写的并集：里面既有 `InProgress` 又有
 * `Started`（两者都显示「生产中」），也含 `Completed` / `Closed`。把它当选择器数据源有两个
 * 后果：屏上并排两条无法区分的「生产中」；以及允许把一张**未完工单**标成「已完成/已关闭」
 * ——那与该实体的定义（`completedQuantity < plannedQuantity`）自相矛盾。
 *
 * ## 码的拼写取自契约，不是常量名
 *
 * 权威有两处且一致，**都是小写**：
 * - 契约：`ListBusinessConsoleMesWorkOrdersData['query']['status']`（api-client 生成物）；
 * - 域常量：`WorkOrder.cs` 的 `CreatedStatus="created"` / `ReleasedStatus="released"` /
 *   `StartedStatus="started"` / `HoldStatus="hold"`。
 *
 * 先前这里写的是 `Planned` / `Released` / `Started` / `OnHold`——把**常量名**当成了值。
 * `Planned` 与 `OnHold` 在系统里**根本不存在**，写进交接单后 PC 读面
 * （`business-console` 的 `useMesReferenceLabels.statusLabel`，解不出即原样回吐）会把
 * 英文码直接显示给用户。本 PR 是这一列唯一的生产者，所以那是本 PR 引入的缺陷。
 *
 * ## 只留流转中的状态
 *
 * 终态（completed / closed / cancelled / scrapped / split / merged）一律排除。
 *
 * ## 文案与 PC 读面对齐
 *
 * 同一张交接单在 PDA 与 PC 上必须读作同一个词，所以这里直接采用 PC 读面对这四个码的说法，
 * 而不是套用 `WORK_ORDER_STATUS_LABELS`（它对 `Released` 说「已下达」，与 PC 的「已释放」不一致）。
 *
 * ## 这条跨界期望被钉到什么程度（覆盖边界，别当成四个码都钉住了）
 *
 * 钉在 `apps/business-pda/src/composables/shiftHandoverWorkOrderStatus.contract.test.ts`
 * （**不是**本文件的 `mesLabels.test.ts`——那边四格都是写面自证）。它是**单向**的：
 *
 * - **改这里的 label → 红**（四个码都红）：契约测试比对本表与它钉的 PC 文案副本。
 * - **改 console 的词表 → 只有 `started` 会红**，红在 console 自己的
 *   `pages/mes/handoversPage.test.ts`「点开交接单按 id 取详情…」那格；
 *   `created` / `released` / `hold` 改了**两侧都绿**（实测：console 2472 全绿 + PDA 1343 全绿）。
 *
 * 也就是说四个码里只有 `started` 是双面红，其余三个只挡得住我方漂移。console 在另一个 app 里，
 * 从这里 import 它的词表只会变成第四份副本，所以这条缝**不在本票内补**——写明它，
 * 好过让后来人以为四个码都有防线。
 */
export const SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS = [
  { code: 'created', label: '已创建' },
  { code: 'released', label: '已释放' },
  { code: 'started', label: '已开工' },
  { code: 'hold', label: '挂起' },
] as const

/** 交接单未完工单状态的显示名；只认写面值域，解不出不回吐英文码。 */
export function shiftHandoverUnfinishedWorkOrderStatusLabel(status?: string | null): string {
  const code = (status ?? '').trim().toLowerCase()
  return (
    SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.find((o) => o.code === code)?.label ??
    UNKNOWN_STATUS_LABEL
  )
}

function normalizeHandoverCode(value?: string | null): string {
  return (value ?? '').trim().toLowerCase()
}

/**
 * 完工入库单 ERP 成本归集进度（Requested 状态卡在哪一环，#3728 / #3767）。
 *
 * 字段对齐 api-client 生成类型
 * `NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleMesReceiptCostCapitalizationProgress`。
 */
export interface ReceiptCostCapitalizationProgress {
  workOrderCompleted?: boolean | null
  receivedReportCount?: number | null
  expectedReportCount?: number | null
  receivedMaterialMovementCount?: number | null
  expectedMaterialMovementCount?: number | null
  capitalizationPublished?: boolean | null
}

/** `receiptPendingReason` 所需的入库单行字段（business-console / PDA 共用同一份判断逻辑）。 */
export interface ReceiptPendingReasonRow {
  receiptStatus?: string | null
  unitCost?: number | null
  costCapitalization?: ReceiptCostCapitalizationProgress | null
}

/**
 * 「待入库」卡在哪一环（#3728，PDA 补齐于 #3767）。Requested 是两段等待：先等 ERP 成本归集
 * 回传单位成本，拿到单位成本后才提交库存过账。成本归集卡住时 MES 没有失败码，所以每个待入库
 * 行都要在这里给出原因；costCapitalization 由网关按调用者 ERP 财务读权限附带，没有时只能说明
 * 在等哪一环。
 *
 * business-console 和 PDA 调用的是同一个列表接口
 * （`listBusinessConsoleMesFinishedGoodsReceiptRequests`）。网关按 Ordinal 比较运行时状态值
 * `"Requested"`（`BusinessConsoleMesEndpoints.cs`），生成类型 types.gen 里的小写枚举值只是
 * 展示层处理器（`MesListDisplayOpenApiDocumentProcessor`）改写的契约文档，不是运行时实际大小写；
 * 这里对 `Requested` 做大小写不敏感比较，不依赖某一侧的具体大小写拼写，与本文件其余大小写
 * 混杂的状态表（历史遗留、各自域的独立值域）无关。
 */
export function receiptPendingReason(row: ReceiptPendingReasonRow): string | null {
  if ((row.receiptStatus ?? '').toLowerCase() !== 'requested') return null
  if (row.unitCost !== undefined && row.unitCost !== null) return '已提交库存过账，等待库存确认'
  const progress = row.costCapitalization
  if (!progress) return '等待 ERP 成本归集回传单位成本'
  if (progress.capitalizationPublished) return 'ERP 已完成成本归集，等待单位成本回传'
  // 归集停住（完工或报工事件被 ERP 拒绝）只有管理员能在死信里处理，业务用户能做的是找管理员。
  const escalate = '长时间不变请联系系统管理员'
  if (!progress.workOrderCompleted) return `等待 ERP 收到工单完工后归集成本；${escalate}`
  const counts = `报工成本 ${progress.receivedReportCount ?? 0}/${progress.expectedReportCount ?? 0}，物料过账 ${progress.receivedMaterialMovementCount ?? 0}/${progress.expectedMaterialMovementCount ?? 0}`
  return `等待 ERP 成本归集（${counts}）；${escalate}`
}
