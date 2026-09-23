import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'
import type { EntityPickerOption } from '@nerv-iip/ui'
import { resolveStatus } from '@nerv-iip/ui'

/**
 * 工单选择器的候选项。工单目录是持续新增的大目录（现场已有数千张），选择器走**服务端搜索**，
 * 每次只拿回一页结果——所以已选工单可能根本不在当前结果里（来自地址栏、或上一次搜索）。
 *
 * 这里补一条占位项把已选工单保住，否则选择器会显示成「未选择」，让人以为选中丢了。
 * 占位项的显示文案优先用**人读工单号**（`workOrderNo`）；只有在目录里完全找不到该工单时，
 * 才回落到标识本身——工单标识在本系统就是 `WO-…` 人读编码，不会印出 GUID。
 */
export function buildWorkOrderPickerOptions(
  workOrders: readonly BusinessConsoleMesWorkOrderItem[],
  selectedWorkOrderId: string | null | undefined,
): EntityPickerOption[] {
  const options: EntityPickerOption[] = []
  for (const order of workOrders) {
    const value = order.workOrderId?.trim()
    if (!value) continue
    const hint = [order.skuCode?.trim(), order.status ? resolveStatus(order.status).label : '']
      .filter(Boolean)
      .join(' · ')
    options.push({ value, label: order.workOrderNo?.trim() || value, ...(hint ? { hint } : {}) })
  }

  const selected = selectedWorkOrderId?.trim()
  if (!selected || options.some((option) => option.value === selected)) return options

  const known = workOrders.find((order) => order.workOrderId?.trim() === selected)
  return [
    { value: selected, label: known?.workOrderNo?.trim() || selected, hint: '当前所选' },
    ...options,
  ]
}

/**
 * 工序任务选择器的候选项：取**已选工单**自带的工序任务（工单列表每行就带着它的工序），
 * 按工序顺序排。工序任务标识可能是系统 GUID，所以显示文案用人读工序任务号，
 * 选择器要关掉编码行（`:show-code="false"`），不把标识印到界面上。
 */
export function buildOperationTaskPickerOptions(
  workOrder: BusinessConsoleMesWorkOrderItem | undefined,
): EntityPickerOption[] {
  return [...(workOrder?.operationTasks ?? [])]
    .sort((a, b) => (a.operationSequence ?? 0) - (b.operationSequence ?? 0))
    .flatMap((task) => {
      const value = task.operationTaskId?.trim()
      if (!value) return []
      const sequence = task.operationSequence == null ? '' : `工序 ${task.operationSequence}`
      const label = task.operationTaskNo?.trim() || sequence || '未编号工序'
      const hint = [
        label === sequence ? '' : sequence,
        task.workCenterName?.trim() || task.workCenterCode?.trim(),
        task.status ? resolveStatus(task.status).label : '',
      ]
        .filter(Boolean)
        .join(' · ')
      return [{ value, label, ...(hint ? { hint } : {}) }]
    })
}
