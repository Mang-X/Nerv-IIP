export interface MaterialLotAvailabilityLike {
  requestId?: unknown
  materialId?: unknown
  materialLotId?: unknown
  receivedQuantity?: unknown
  consumedQuantity?: unknown
  status?: unknown
}

export interface AvailableMaterialLotFields {
  requestId: string
  materialId: string
  materialLotId: string
  receivedQuantity: number
  consumedQuantity: number
  status: 'Received'
}

export function isAvailableMaterialLot<T extends MaterialLotAvailabilityLike>(
  row: T,
): row is T & AvailableMaterialLotFields {
  return (
    row.status === 'Received' &&
    typeof row.requestId === 'string' &&
    typeof row.materialId === 'string' &&
    typeof row.materialLotId === 'string' &&
    typeof row.receivedQuantity === 'number' &&
    typeof row.consumedQuantity === 'number' &&
    row.receivedQuantity > row.consumedQuantity
  )
}
