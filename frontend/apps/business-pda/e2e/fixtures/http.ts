import { type Route } from '@playwright/test'

export function envelope<T>(data: T) {
  return { success: true, message: null, data }
}

export async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

/** A `{ items, total }` list payload wrapped in the standard success envelope. */
export function listEnvelope<T>(items: T[]) {
  return envelope({ items, total: items.length })
}

export const nowUtc = '2026-06-11T00:00:00.000Z'

/**
 * 权威回执（#1219）：写操作必须回 `operationReceipt`，前端才敢把这次写认作已确认。
 * `confirmed` 形态自证终局（stateConfirmed=true / readbackRequired=false），无需回读；
 * operationType / resourceId / idempotencyKey 必须与调用方的业务意图逐字一致，否则前端
 * 判为「结果不确定」。mock 不复刻这套语义，就等于把真实写路径整条绕开。
 */
export function confirmedOperationReceipt(input: {
  operationType: string
  resourceType: string
  resourceId: string
  idempotencyKey: string
  resourceStatus: string
}) {
  return {
    ...input,
    authority: 'business-gateway',
    outcome: 'confirmed',
    stateConfirmed: true,
    readbackRequired: false,
    readbackMethod: null,
    readbackPath: null,
    changedAtUtc: nowUtc,
  }
}

export function confirmedOperation(
  operationType: string,
  resourceId: string,
  idempotencyKey: string,
  resourceStatus: string,
) {
  return {
    operationType,
    authority: 'business-gateway',
    resourceType: 'business-resource',
    resourceId,
    idempotencyKey,
    outcome: 'confirmed',
    stateConfirmed: true,
    readbackRequired: false,
    changedAtUtc: nowUtc,
    resourceStatus,
  }
}
