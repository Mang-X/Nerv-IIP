import { equipmentStateLabel } from '@nerv-iip/business-core'
import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'
import type { ScheduleModel, ScheduleTask } from './types'

/** 商业关联来自已加载的 MES 权威工单；不借分页空缺编造关联。 */
export function withWorkOrderFacts(
  model: ScheduleModel,
  orders: BusinessConsoleMesWorkOrderItem[],
  commercialSourceUnavailable = false,
): ScheduleModel {
  const byId = new Map(orders.map((order) => [order.workOrderId, order]))
  return {
    ...model,
    tasks: model.tasks.map((task) => ({
      ...task,
      commercialSourceFacts: commercialSourceUnavailable
        ? undefined
        : byId.get(task.orderId)?.commercialSourceFacts,
      commercialSourceUnavailable,
    })),
  }
}

/** 四个读面统一的事实口径。当前执行值与保存时风险分别展示。 */
export function taskFactRows(task: ScheduleTask): Array<[string, string]> {
  if (task.blockKind || task.type !== 'operation') return []
  const current = task.currentExecution
  const rows: Array<[string, string]> = []
  const commercial = task.commercialSourceFacts
  if (task.commercialSourceUnavailable) rows.push(['商业关联', '暂不可读取'])
  else if (commercial?.status === 'forbidden') rows.push(['商业关联', '无权读取'])
  for (const order of commercial?.salesOrders ?? []) {
    if (order.salesOrderNo) rows.push(['销售订单', order.salesOrderNo])
    if (order.customerCode) rows.push(['客户', order.customerCode])
  }
  const progress = current?.workOrderProgress
  if (progress?.completedQuantity != null && progress.plannedQuantity != null)
    rows.push(['工单进度', `${progress.completedQuantity} / ${progress.plannedQuantity}`])
  if (task.dueUtc) {
    const minutes = (Date.parse(task.endUtc) - Date.parse(task.dueUtc)) / 60_000
    rows.push([
      '交期差',
      minutes === 0 ? '按期' : `${minutes > 0 ? '延期' : '提前'} ${Math.abs(minutes)} 分钟`,
    ])
  }
  const ready =
    current?.isMaterialReady === true ? '已齐套' : current?.isMaterialReady === false ? '缺料' : ''
  const eta = current?.materialReadyUtc
    ? `预计到料 ${new Date(current.materialReadyUtc).toLocaleString('zh-CN', { hour12: false })}`
    : current?.isMaterialReady === true
      ? ''
      : '未知到料日'
  rows.push(['当前到料', [ready, eta].filter(Boolean).join(' · ')])
  const knownEquipment =
    current?.isEquipmentSourceFresh === true && task.resourceId === task.executionResourceId
  rows.push([
    '当前设备',
    knownEquipment && current?.equipmentState
      ? equipmentStateLabel(current.equipmentState)
      : '未知 · 开工前请人工确认设备可用',
  ])
  if (task.predecessors) rows.push(['前序', task.predecessors.join('、') || '无'])
  if (task.successors) rows.push(['后序', task.successors.join('、') || '无'])
  return rows
}
