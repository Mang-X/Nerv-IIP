import type { WorkingScheduleOrder } from './useWorkingScheduleDraft'

export const FIRST_PLAN_ORDER_LIMIT = 500

export function firstPlanCapacityReason(count: number) {
  return count > FIRST_PLAN_ORDER_LIMIT
    ? `首版排程最多选择 ${FIRST_PLAN_ORDER_LIMIT} 单，请先移出超出的工单`
    : undefined
}

export function firstPlanSelectionReason(orders: WorkingScheduleOrder[], addedIds: string[]) {
  const selected = new Set(
    orders.filter((order) => order.included).map((order) => order.workOrderId),
  )
  addedIds.forEach((id) => selected.add(id))
  return firstPlanCapacityReason(selected.size)
}
