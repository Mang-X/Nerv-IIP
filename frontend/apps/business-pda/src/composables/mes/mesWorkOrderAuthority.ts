export type MesWorkOrderAuthority = {
  workOrderType?: string | null
  sourceWorkOrderId?: string | null
  sourceNcrId?: string | null
  sourceNcrCode?: string | null
}

export type ParsedMesWorkOrderAuthority =
  | { kind: 'standard' }
  | {
      kind: 'rework'
      sourceWorkOrderId: string
      sourceNcrId: string
      sourceNcrCode: string
    }

function hasText(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

export function isReworkWorkOrder(item: MesWorkOrderAuthority) {
  return item.workOrderType === 'rework'
}

export function parseMesWorkOrderAuthority(
  item: MesWorkOrderAuthority,
): ParsedMesWorkOrderAuthority | null {
  if (!isReworkWorkOrder(item)) return { kind: 'standard' }
  if (
    !hasText(item.sourceWorkOrderId) ||
    !hasText(item.sourceNcrId) ||
    !hasText(item.sourceNcrCode)
  ) {
    return null
  }
  return {
    kind: 'rework',
    sourceWorkOrderId: item.sourceWorkOrderId.trim(),
    sourceNcrId: item.sourceNcrId.trim(),
    sourceNcrCode: item.sourceNcrCode.trim(),
  }
}

export function hasCompleteReworkAuthority(item: MesWorkOrderAuthority) {
  return parseMesWorkOrderAuthority(item) !== null
}

export function hasSameMesWorkOrderAuthority(
  parent: MesWorkOrderAuthority,
  task: MesWorkOrderAuthority,
) {
  const parentAuthority = parseMesWorkOrderAuthority(parent)
  const taskAuthority = parseMesWorkOrderAuthority(task)
  if (!parentAuthority || !taskAuthority || parentAuthority.kind !== taskAuthority.kind)
    return false
  if (parentAuthority.kind === 'standard' && taskAuthority.kind === 'standard') return true
  if (parentAuthority.kind !== 'rework' || taskAuthority.kind !== 'rework') return false
  return (
    parentAuthority.sourceWorkOrderId === taskAuthority.sourceWorkOrderId &&
    parentAuthority.sourceNcrId === taskAuthority.sourceNcrId &&
    parentAuthority.sourceNcrCode === taskAuthority.sourceNcrCode
  )
}
