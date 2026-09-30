import { describe, expect, it } from 'vitest'

import { isAvailableMaterialLot } from './materialLots'

describe('isAvailableMaterialLot', () => {
  const receivedLot = {
    requestId: 'MIR-001',
    materialId: 'MAT-001',
    materialLotId: 'LOT-001',
    receivedQuantity: 10,
    consumedQuantity: 2,
    status: 'Received',
  }

  it('只接受已收料且仍有可用量的批次', () => {
    expect(isAvailableMaterialLot(receivedLot)).toBe(true)
    expect(isAvailableMaterialLot({ ...receivedLot, consumedQuantity: 10 })).toBe(false)
  })

  // 反例取 MaterialIssueRequest 自己域内的状态。原先的第二项是小写的
  // `inventoryPostingFailed`——那既拼写不对（#3912），又属于
  // FinishedGoodsReceiptRequest，根本不在 MaterialIssueRequest 的值域里，
  // 是一条恒红的死输入：它证明不了任何东西，只是让这条断言看起来覆盖更广。
  it.each(['PartiallyReceived', 'Requested', 'ReturnRequested'])(
    '拒绝 %s 但仍有正剩余量的非已收料批次',
    (status) => {
      expect(isAvailableMaterialLot({ ...receivedLot, status })).toBe(false)
    },
  )
})
