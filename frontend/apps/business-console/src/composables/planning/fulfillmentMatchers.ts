import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleErpDeliveryOrderItem,
  BusinessConsoleMesProductionPlanRow,
  BusinessConsoleMrpPeggingItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'

/** 规范化销售订单号：去空白，空串→undefined（空 scope 不发请求）。 */
export function normalizeScope(value: string | null | undefined): string | undefined {
  const trimmed = value?.trim()
  return trimmed ? trimmed : undefined
}

/** DemandSource 关联匹配：sourceReference === salesOrderNo（#958 桥接键）。 */
export function matchDemandSource(
  items: readonly BusinessConsoleDemandSourceItem[] | undefined,
  salesOrderNo: string | undefined,
): BusinessConsoleDemandSourceItem | undefined {
  if (!salesOrderNo) return undefined
  return items?.find(
    (item) => item.sourceReference === salesOrderNo && item.demandType === 'sales-order',
  )
}

/**
 * MRP pegging 关联匹配：只认 `peggingType = 'demand'` 且
 * `demandSourceReference === salesOrderNo` 的行（scheduled-receipt 行的引用是
 * `系统:单据类型:单号` 复合串，不是销售单号，必须排除）。
 *
 * 合批建议对每一张被合并的订单都保留一行 demand pegging，所以两张订单各自都能命中
 * 同一个 suggestionId——这正是合批场景下两条时间线都能点亮的依据。
 */
export function matchDemandPeggings(
  items: readonly BusinessConsoleMrpPeggingItem[] | undefined,
  salesOrderNo: string | undefined,
): BusinessConsoleMrpPeggingItem[] {
  if (!salesOrderNo) return []
  return (items ?? []).filter(
    (item) =>
      item.peggingType?.trim().toLowerCase() === 'demand' &&
      item.demandSourceReference?.trim() === salesOrderNo &&
      Boolean(item.suggestionId?.trim()),
  )
}

/** 从命中的 pegging 行取去重后的建议标识（保持命中顺序）。 */
export function peggingSuggestionIds(items: readonly BusinessConsoleMrpPeggingItem[]): string[] {
  const ids: string[] = []
  for (const item of items) {
    const id = item.suggestionId?.trim()
    if (id && !ids.includes(id)) ids.push(id)
  }
  return ids
}

/**
 * 下游引用码值大小写/分隔符两侧口径不一（`BusinessMes` / `business-mes`），统一归一化后比较。
 *
 * 导出供别处复用：计划工作台原来各写一份**严格字面量**比较，于是「对该单排产」按钮对种子
 * 数据（后端写的是 `business-mes`）一个都不渲染——功能做了却谁都看不见（#1262 / MAN-694，
 * 第五轮走查实测）。这类码值口径不一致在本仓库反复出现，比较逻辑只该有一处。
 */
export function normalizeReferenceToken(value: string | null | undefined): string {
  return (value ?? '').toLowerCase().replace(/[^a-z0-9]/g, '')
}

/** 该计划建议是否已承接成 MES 工单——两侧码值归一后判定，不做字面量比较。 */
export function isMesWorkOrderDownstream(
  service: string | null | undefined,
  documentType: string | null | undefined,
): boolean {
  return (
    normalizeReferenceToken(service) === 'businessmes' &&
    normalizeReferenceToken(documentType) === 'workorder'
  )
}

/**
 * 建议本体匹配：按 `suggestionIds` 的顺序（扫描窗内**新运行在前**）优先取新的那条，
 * 而不是按服务端返回顺序——重跑 MRP 后，本单最新一次的建议才是计划员看到的那条。
 */
export function matchPlanningSuggestion(
  items: readonly BusinessConsolePlanningSuggestionItem[] | undefined,
  suggestionIds: readonly string[],
): BusinessConsolePlanningSuggestionItem | undefined {
  for (const suggestionId of suggestionIds) {
    const matched = (items ?? []).find((item) => item.suggestionId?.trim() === suggestionId)
    if (matched) return matched
  }
  return undefined
}

/**
 * 建议 → MES 工单：接受建议时后端把工单号回写为建议的下游引用
 * （downstreamService=BusinessMes / downstreamDocumentType=WorkOrder / downstreamDocumentId=工单号）。
 * 只认这三项齐备的行，绝不把别的下游单据（采购申请）当工单。
 *
 * **重跑 MRP 后不能只看最新一条建议**：接受建议后再跑一次 MRP，本单会多出一条尚未被接受的
 * 新建议，而工单挂在**旧**建议上。所以这里遍历扫描窗内命中的全部建议，取第一条真正带工单
 * 引用的——否则工单明明在，节点却显示空态。
 */
export function matchSuggestionWorkOrder(
  items: readonly BusinessConsolePlanningSuggestionItem[] | undefined,
  suggestionIds: readonly string[],
): { suggestionId: string; workOrderNo: string } | undefined {
  for (const suggestionId of suggestionIds) {
    for (const item of items ?? []) {
      if (item.suggestionId?.trim() !== suggestionId) continue
      if (normalizeReferenceToken(item.downstreamService) !== 'businessmes') continue
      if (normalizeReferenceToken(item.downstreamDocumentType) !== 'workorder') continue
      const workOrderNo = item.downstreamDocumentId?.trim()
      if (workOrderNo) return { suggestionId, workOrderNo }
    }
  }
  return undefined
}

/**
 * 生产计划行（即带来源引用的 MES 工单）匹配：**只认** `sourceDocumentId === 解析出工单号的那条建议`。
 *
 * 不做「按销售单号兜底」：行上根本没有工单号（`productionPlanId` 就是 `sourceDocumentId`），
 * 无从校验兜底命中的行是不是解析出来的那张工单——合批 / 一单多工单时很容易把**别的工单**的
 * 状态贴到本节点上。宁可不贴状态，也不贴错状态。
 *
 * 反过来，合批工单的 `sourceDemandReference` 只是需求引用集合里的**第一条**，可能是别的订单号，
 * 因此它绝不能用来把本单排除掉——只在关联键说明里如实标注「合批」。
 */
export function matchProductionPlanRow(
  rows: readonly BusinessConsoleMesProductionPlanRow[] | undefined,
  suggestionId: string | undefined,
): BusinessConsoleMesProductionPlanRow | undefined {
  if (!suggestionId) return undefined
  return (rows ?? []).find((row) => row.sourceDocumentId?.trim() === suggestionId)
}

/** MRP 建议节点的记录：命中的 pegging 行 + （若仍在建议列表里）建议本体。 */
export interface MrpSuggestionRecord {
  pegging: BusinessConsoleMrpPeggingItem
  suggestion?: BusinessConsolePlanningSuggestionItem
}

/** MES 工单节点的记录：工单号（人读）+ 该工单的来源引用行（补状态）。 */
export interface MesWorkOrderRecord {
  workOrderNo: string
  planRow?: BusinessConsoleMesProductionPlanRow
}

/**
 * MRP 建议没有人读单号（suggestionId 是 GUID，与需求与计划工作台一致不上屏），
 * 因此用「物料 × 数量」这一组业务事实自识别。
 */
export function describeMrpSuggestion(record: MrpSuggestionRecord): string {
  const skuCode =
    record.suggestion?.skuCode?.trim() ||
    record.pegging.parentSkuCode?.trim() ||
    record.pegging.componentSkuCode?.trim() ||
    ''
  const quantity = record.suggestion?.quantity ?? record.pegging.quantity
  const uomCode = record.suggestion?.uomCode?.trim() ?? ''
  const amount = quantity == null ? '' : `${quantity}${uomCode ? ` ${uomCode}` : ''}`
  return [skuCode, amount].filter(Boolean).join(' × ') || '计划建议'
}

/**
 * 合批说明。合批工单的首条需求引用可能是别的订单号——这时明说「同时承接…等订单」，
 * 不让人误以为这张工单只为本单而开。
 */
export function describeMergedWorkOrder(
  record: MesWorkOrderRecord,
  salesOrderNo: string | undefined,
): string | undefined {
  const primaryDemand = record.planRow?.sourceDemandReference?.trim()
  return primaryDemand && salesOrderNo && primaryDemand !== salesOrderNo
    ? `该工单为合批工单，同时承接 ${primaryDemand} 等订单`
    : undefined
}

/** 发货单关联匹配：DeliveryOrder.salesOrderNo === salesOrderNo。 */
export function matchDeliveryOrders(
  items: readonly BusinessConsoleErpDeliveryOrderItem[] | undefined,
  salesOrderNo: string | undefined,
): BusinessConsoleErpDeliveryOrderItem[] {
  if (!salesOrderNo) return []
  return (items ?? []).filter((item) => item.salesOrderNo === salesOrderNo)
}
