import { describe, expect, it } from 'vitest'

import { isAvailableMaterialLot } from './materialLots'

describe('isAvailableMaterialLot', () => {
  const receivedLot = {
    requestId: 'MIR-001',
    materialId: 'MAT-001',
    materialLotId: 'LOT-001',
    receivedQuantity: 10,
    consumedQuantity: 2,
    status: 'received',
  }

  it('只接受已收料且仍有可用量的批次', () => {
    expect(isAvailableMaterialLot(receivedLot)).toBe(true)
    expect(isAvailableMaterialLot({ ...receivedLot, consumedQuantity: 10 })).toBe(false)
  })

  it.each(['partiallyReceived', 'inventoryPostingFailed'])(
    '拒绝 %s 但仍有正剩余量的非已收料批次',
    (status) => {
      expect(isAvailableMaterialLot({ ...receivedLot, status })).toBe(false)
    },
  )
})
