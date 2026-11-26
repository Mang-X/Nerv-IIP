import { expect, test } from '@playwright/test'
import {
  envelope,
  fulfillJson,
  principal,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
} from './console-scenarios/fixture'

test('PC 部分收料响应丢失后恢复原收料，再过账下一次部分收料', async ({ page }) => {
  const operator = {
    ...principal,
    permissionCodes: [...principal.permissionCodes, 'business.mes.materials.manage'],
  }
  const receiptPosts: Array<{ receivedQuantity: number; idempotencyKey: string }> = []
  const receiptKeys = new Set<string>()
  const requestId = 'MIR-4551'
  let posting = false
  let receivedQuantity = 0
  await seedStoredSession(page, operator)
  await page.route('**/api/console/v1/**', (route) => routeConsoleApi(route, operator))
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await page.route('**/api/business-console/v1/mes/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith(`/${requestId}/line-side-receipts`)) {
      const body = route.request().postDataJSON() as (typeof receiptPosts)[number]
      receiptPosts.push(body)
      if (!receiptKeys.has(body.idempotencyKey)) {
        receiptKeys.add(body.idempotencyKey)
        receivedQuantity += body.receivedQuantity
      }
      if (receiptPosts.length === 1) {
        posting = true
        return route.abort('connectionreset')
      }
      return fulfillJson(route, envelope({ accepted: true, downstreamDocumentId: requestId }))
    }
    if (path.endsWith('/material-issue-requests')) {
      return fulfillJson(
        route,
        envelope({
          items: [
            {
              requestId,
              workOrderId: 'WO-001',
              materialId: 'mat-1',
              requestedQuantity: 6,
              receivedQuantity: posting ? 0 : receivedQuantity,
              status: posting
                ? 'ReceiptPosting'
                : receivedQuantity
                  ? 'PartiallyReceived'
                  : 'Requested',
            },
          ],
          total: 1,
        }),
      )
    }
    if (path.endsWith('/material-readiness')) {
      return fulfillJson(
        route,
        envelope({
          workOrderId: 'WO-001',
          readinessStatus: !posting && receivedQuantity >= 4 ? 'Ready' : 'Blocked',
          blockingReasons: [],
          items: [],
        }),
      )
    }
    return route.fallback()
  })

  await page.goto('/mes/work-orders/WO-001')
  const receive = page.getByTestId(`receive-material-${requestId}`)
  const dialog = page.getByRole('alertdialog')
  const submit = dialog.getByTestId('submit-line-side-receipt')
  const quantity = dialog.getByLabel('收料数量')
  await receive.click()
  await quantity.fill('2')
  await submit.click()
  await expect(dialog.getByRole('alert')).toContainText('暂未确认收料结果')
  await page.reload()
  await receive.click()
  await expect(quantity).toHaveValue('2')
  await expect(quantity).toBeDisabled()
  await expect(submit).toHaveText('重试本次收料')
  await submit.click()
  await expect(dialog).toBeHidden()
  await expect(page.getByText('正在等待收料过账与最新齐套结果。')).toBeVisible()
  expect(receiptPosts).toHaveLength(2)
  expect(receiptPosts[0]?.idempotencyKey).toEqual(expect.any(String))
  expect(receiptPosts[1]).toEqual(receiptPosts[0])
  expect(receivedQuantity).toBe(2)

  posting = false
  await expect(page.getByText('阻塞', { exact: true }).first()).toBeVisible()
  await receive.click()
  await quantity.fill('2')
  await submit.click()
  await expect(dialog).toBeHidden()
  await expect(page.getByText('已齐套', { exact: true })).toBeVisible()
  expect(receiptPosts).toHaveLength(3)
  expect(receiptPosts[2]?.idempotencyKey).toEqual(expect.any(String))
  expect(receiptPosts[2]?.idempotencyKey).not.toBe(receiptPosts[0]?.idempotencyKey)
  expect(receivedQuantity).toBe(4)
  const row = page.getByRole('row').filter({ hasText: requestId })
  await expect(row.getByRole('cell', { name: '4', exact: true })).toBeVisible()
})
