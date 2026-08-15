import { describe, expect, it } from 'vitest'
import { confirmedMaintenanceCreateWorkOrderId } from './maintenanceCreateReceipt'

const workOrderId = '019F1000-0000-7000-8000-000000000001'

function response(payloadId: unknown, receiptId: unknown) {
  return {
    success: true,
    data: {
      workOrderId: payloadId,
      operationReceipt: { resourceId: receiptId },
    },
  }
}

describe('confirmedMaintenanceCreateWorkOrderId', () => {
  it('accepts matching canonical GUIDs and returns the normalized strong ID', () => {
    expect(
      confirmedMaintenanceCreateWorkOrderId(
        response(` ${workOrderId} `, workOrderId.toLowerCase()),
      ),
    ).toBe(workOrderId.toLowerCase())
  })

  it.each([
    ['matching semantic strings', 'WO-INVALID', 'WO-INVALID'],
    ['empty GUIDs', '00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-000000000000'],
    ['mismatched GUIDs', workOrderId, '019f1000-0000-7000-8000-000000000002'],
    ['missing receipt ID', workOrderId, undefined],
  ])('fails closed for %s', (_case, payloadId, receiptId) => {
    expect(() => confirmedMaintenanceCreateWorkOrderId(response(payloadId, receiptId))).toThrow(
      '强标识不一致或不合法',
    )
  })

  it('fails closed when the operation receipt is absent', () => {
    expect(() =>
      confirmedMaintenanceCreateWorkOrderId({ success: true, data: { workOrderId } }),
    ).toThrow('缺少权威操作回执')
  })
})
