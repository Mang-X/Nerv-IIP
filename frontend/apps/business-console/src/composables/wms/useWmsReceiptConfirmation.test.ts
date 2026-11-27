import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import { describe, expect, it, vi } from 'vitest'
import {
  canConfirmPurchaseReceipt,
  purchaseReceiptConfirmationErrors,
  readPurchaseReceiptIntent,
  submitPurchaseReceipt,
} from './useWmsReceiptConfirmation'

const confirm = vi.hoisted(() => vi.fn())
vi.mock('@nerv-iip/api-client', () => ({ confirmBusinessConsoleWmsInboundReceipt: confirm }))

const scope = {
  principalId: 'user-receiving',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  scopeKind: 'site',
  scopeId: 'SITE-A',
}

const row = {
  inboundOrderId: 'inbound-001',
  inboundOrderNo: 'IN-001',
  status: 'Open',
  version: 3,
  purchaseReceipt: {
    status: 'awaiting-confirmation',
    lines: [{ lineNo: '10', skuCode: 'SKU-1', uomCode: 'kg', expectedQuantity: 5 }],
  },
} satisfies BusinessConsoleWmsInboundOrderItem

describe('purchase receipt confirmation', () => {
  it('requires explicit matching actual quantities and an explicit exchange rate', () => {
    expect(purchaseReceiptConfirmationErrors(row.purchaseReceipt.lines, [''], '')).toEqual({
      lines: ['请输入实收数量。'],
      exchangeRate: '请输入大于零且最多八位小数的汇率。',
    })
    expect(
      purchaseReceiptConfirmationErrors(row.purchaseReceipt.lines, ['4'], '1').lines[0],
    ).toBeTruthy()
    expect(
      purchaseReceiptConfirmationErrors(row.purchaseReceipt.lines, ['5'], '1.25').lines[0],
    ).toBeUndefined()
    expect(canConfirmPurchaseReceipt({ ...row, purchaseReceipt: null }, undefined)).toBe(false)
    expect(canConfirmPurchaseReceipt({ ...row, status: 'Cancelled' }, undefined)).toBe(false)
  })

  it('reuses the first key, version, scope, quantities and rate after an uncertain result', async () => {
    confirm.mockRejectedValueOnce(new TypeError('connection lost')).mockResolvedValueOnce({
      data: { success: true, data: { inboundOrderId: row.inboundOrderId, version: 5 } },
    })
    const draft = {
      expectedVersion: 3,
      exchangeRate: 1.25,
      lines: [{ lineNo: '10', receivedQuantity: 5 }],
    }
    await expect(submitPurchaseReceipt(scope, row.inboundOrderId, draft)).rejects.toThrow(
      'connection lost',
    )
    const staged = {
      ...row,
      version: 4,
      purchaseReceipt: { ...row.purchaseReceipt, status: 'erp-pending' },
    }
    expect(canConfirmPurchaseReceipt(staged, undefined)).toBe(false)
    expect(
      canConfirmPurchaseReceipt(staged, readPurchaseReceiptIntent(scope, row.inboundOrderId)),
    ).toBe(true)
    expect(
      readPurchaseReceiptIntent({ ...scope, scopeId: 'SITE-B' }, row.inboundOrderId),
    ).toBeUndefined()
    await submitPurchaseReceipt(scope, row.inboundOrderId, {
      ...draft,
      expectedVersion: 4,
      exchangeRate: 2,
    })
    const [first, second] = confirm.mock.calls
    expect(first![0].body).toMatchObject({ ...draft, scopeKind: 'site', scopeId: 'SITE-A' })
    expect(first![0].body.idempotencyKey).toBeTruthy()
    expect(second).toEqual(first)
    expect(readPurchaseReceiptIntent(scope, row.inboundOrderId)).toBeUndefined()
  })
})
