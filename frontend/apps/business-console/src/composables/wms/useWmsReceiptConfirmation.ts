import {
  confirmBusinessConsoleWmsInboundReceipt,
  type BusinessConsoleConfirmWmsInboundReceiptRequest,
  type BusinessConsoleWmsInboundOrderItem,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { createWmsIdempotencyKey } from './wmsShared'

export interface WmsReceiptConfirmationScope {
  principalId: string
  organizationId: string
  environmentId: string
  scopeKind: string
  scopeId: string
}

type ReceiptDraft = Pick<
  BusinessConsoleConfirmWmsInboundReceiptRequest,
  'expectedVersion' | 'exchangeRate' | 'lines'
>

function intentScope(scope: WmsReceiptConfirmationScope, orderId: string) {
  return {
    principalId: scope.principalId,
    organizationId: scope.organizationId,
    environmentId: scope.environmentId,
    operationType: 'wms.inbound-order.confirm-receipt',
    payloadFingerprint: JSON.stringify([orderId, scope.scopeKind, scope.scopeId]),
  }
}

export function readPurchaseReceiptIntent(scope: WmsReceiptConfirmationScope, orderId: string) {
  return peekPendingBusinessIntent(intentScope(scope, orderId))?.payloadSnapshot as
    | ReceiptDraft
    | undefined
}

export function canConfirmPurchaseReceipt(
  order: BusinessConsoleWmsInboundOrderItem,
  original: ReceiptDraft | undefined,
) {
  const receipt = order.purchaseReceipt
  return (
    order.status === 'Open' &&
    !!order.inboundOrderId &&
    !!order.version &&
    !!receipt?.lines?.length &&
    (receipt.status === 'awaiting-confirmation' || (receipt.status === 'erp-pending' && !!original))
  )
}

export function purchaseReceiptConfirmationErrors(
  lines: readonly { expectedQuantity?: number }[],
  quantities: readonly string[],
  exchangeRate: string,
) {
  const rate = exchangeRate.trim()
  return {
    lines: lines.map(({ expectedQuantity }, index) => {
      const actual = quantities[index]?.trim()
      if (!actual) return '请输入实收数量。'
      if (!/^\d+(\.\d+)?$/.test(actual) || Number(actual) <= 0) return '请输入大于零的实收数量。'
      if (Number(actual) !== expectedQuantity) return '当前实收数量须与待收数量一致。'
    }),
    exchangeRate:
      /^\d{1,10}(\.\d{1,8})?$/.test(rate) && Number(rate) > 0
        ? undefined
        : '请输入大于零且最多八位小数的汇率。',
  }
}

export async function submitPurchaseReceipt(
  scope: WmsReceiptConfirmationScope,
  orderId: string,
  draft: ReceiptDraft,
) {
  const key = intentScope(scope, orderId)
  const intent = acquirePendingBusinessIntent(key, createWmsIdempotencyKey, draft)
  const original = intent.payloadSnapshot as ReceiptDraft
  const { organizationId, environmentId, scopeKind, scopeId } = scope
  const { data } = await confirmBusinessConsoleWmsInboundReceipt({
    path: { inboundOrderId: orderId },
    query: { organizationId, environmentId },
    body: { ...original, scopeKind, scopeId, idempotencyKey: intent.idempotencyKey },
    throwOnError: true,
  })
  if (!data.success || data.data?.inboundOrderId !== orderId) {
    throw new Error('实收确认结果待核实，请重试原提交。')
  }
  clearPendingBusinessIntent(key)
}
