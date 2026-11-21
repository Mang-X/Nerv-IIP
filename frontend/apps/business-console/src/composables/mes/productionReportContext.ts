/**
 * 报工上下文：**只能**由工单列表行 / 工序任务行带出，弹窗自身不提供任何挑选入口。
 * 带不出来的字段（如工序任务行没有物料与计划数量）就不展示，绝不退化成手填。
 */
export interface ProductionReportContext {
  workOrderId: string
  workOrderNo?: string | null
  operationTaskId: string
  operationTaskNo?: string | null
  operationSequence?: number | null
  operationStatus?: string | null
  /** 工作中心显示名（已解析为人读名称，不是 ID）。 */
  workCenterLabel?: string | null
  /** 物料显示名（已解析为人读名称，不是 ID）。 */
  skuLabel?: string | null
  /** 工单计划数量。 */
  plannedQuantity?: number | null
}
