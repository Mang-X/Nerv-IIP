export type MesWorkOrderAuthority = {
  workOrderType?: string | null
  sourceWorkOrderId?: string | null
  sourceNcrId?: string | null
  sourceNcrCode?: string | null
}

function hasText(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

export function isReworkWorkOrder(item: MesWorkOrderAuthority) {
  return item.workOrderType === 'rework'
}

export function hasCompleteReworkAuthority(item: MesWorkOrderAuthority) {
  return (
    !isReworkWorkOrder(item) ||
    (hasText(item.sourceWorkOrderId) && hasText(item.sourceNcrId) && hasText(item.sourceNcrCode))
  )
}
