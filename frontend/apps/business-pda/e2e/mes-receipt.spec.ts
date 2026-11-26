import { expect, test } from '@playwright/test'
import { routeBusinessConsoleApi, routeConsoleApi, seedStoredSession } from './fixtures'
import { principal, session } from './fixtures/auth'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

test('完工入库：选取当前工单的实际批次并回读原登记', async ({ page }) => {
  const receiptOperator = {
    ...principal,
    permissionCodes: [
      'business.mes.work-orders.read',
      'business.mes.receipts.read',
      'business.mes.receipts.manage',
    ],
  }
  await page.route('**/api/console/v1/auth/*', async (route) => {
    const endpoint = new URL(route.request().url()).pathname
    if (endpoint.endsWith('/me')) {
      return route.fulfill({ json: { success: true, data: receiptOperator } })
    }
    if (endpoint.endsWith('/refresh')) {
      return route.fulfill({
        json: { success: true, data: { ...session, principal: receiptOperator } },
      })
    }
    return route.fallback()
  })
  const writes: unknown[] = []
  const readbacks: string[] = []
  const api = '**/api/business-console/v1/mes'
  await page.route(`${api}/work-orders/*/produced-lots*`, async (route) => {
    const path = new URL(route.request().url()).pathname
    const items = path.endsWith('/WO-501/produced-lots')
      ? []
      : path.endsWith('/WO-2/produced-lots')
        ? [
            {
              producedLotNo: 'LOT-2',
              reportNo: 'REPORT-2',
              remainingQuantity: 4,
              serialNo: 'SN-2',
            },
            { producedLotNo: 'EXHAUSTED', remainingQuantity: 0 },
          ]
        : [{ producedLotNo: 'LOT-1', remainingQuantity: 8 }]
    await route.fulfill({ json: { success: true, data: { items } } })
  })
  await page.route(`${api}/finished-goods-receipt-requests*`, async (route) => {
    if (route.request().method() === 'POST') {
      writes.push(route.request().postDataJSON())
      await route.fulfill({
        json: {
          success: true,
          data: { finishedGoodsReceiptRequestId: 'REQ-2', requestNo: 'FGR-2' },
        },
      })
      return
    }
    const query = new URL(route.request().url()).searchParams
    if (query.get('keyword') !== 'FGR-2') return route.fallback()
    readbacks.push(query.get('workOrderId') ?? '')
    await route.fulfill({
      json: {
        success: true,
        data: {
          items: [
            {
              receiptRequestId: 'REQ-OTHER',
              requestNo: 'FGR-20',
              workOrderId: 'WO-2',
              quantity: 1,
            },
            {
              receiptRequestId: 'REQ-2',
              requestNo: 'FGR-2',
              workOrderId: 'WO-2',
              quantity: 3,
              producedLotNo: 'LOT-2',
              receiptStatus: 'Requested',
            },
          ],
          total: 2,
        },
      },
    })
  })

  await page.goto('/mes/receipt')
  await page.getByTestId('new-receipt').click()
  await page.getByTestId('receipt-work-order').filter({ hasText: 'WO-501' }).click()
  await expect(
    page.getByText('当前工单暂无可入库批次。请确认已报工产出，或改选工单。'),
  ).toBeVisible()
  await expect(page.getByTestId('submit-receipt')).toBeDisabled()
  await page.getByTestId('change-work-order').click()
  await page.getByTestId('receipt-work-order').filter({ hasText: 'WO-1' }).click()
  await expect(page.getByText('LOT-1', { exact: true })).toBeVisible()
  await page.getByTestId('change-work-order').click()
  await page.getByTestId('receipt-work-order').filter({ hasText: 'WO-2' }).click()
  await expect(page.getByText('LOT-1', { exact: true })).toHaveCount(0)
  await expect(page.getByText('EXHAUSTED', { exact: true })).toHaveCount(0)
  const submit = page.getByTestId('submit-receipt')
  await expect(submit).toBeDisabled()
  await page.getByText('LOT-2', { exact: true }).click()
  await page.getByTestId('receipt-uom').fill('PCS')
  await page.getByTestId('receipt-quantity').click()
  await page.getByRole('button', { name: '5', exact: true }).click()
  await page.getByRole('button', { name: '完成', exact: true }).click()
  await expect(submit).toBeDisabled()
  await expect(page.getByText('入库数量不能超过该批次剩余可入库数量。')).toBeVisible()
  await page.getByTestId('receipt-quantity').click()
  await page.getByRole('button', { name: '删除', exact: true }).click()
  await page.getByRole('button', { name: '3', exact: true }).click()
  await page.getByRole('button', { name: '完成', exact: true }).click()
  await submit.click()

  await expect(page.getByText('完工入库已提交')).toBeVisible()
  await expect(
    page.getByText('单号 FGR-2 · 批次 LOT-2 · 数量 3 · 待入库', { exact: true }),
  ).toBeVisible()
  expect(readbacks).toEqual(['WO-2'])
  expect(writes).toEqual([
    expect.objectContaining({
      workOrderId: 'WO-2',
      skuId: 'SKU-2',
      producedLotNo: 'LOT-2',
      serialNo: 'SN-2',
      quantity: 3,
      uomCode: 'PCS',
    }),
  ])
})
