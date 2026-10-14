import { expect, test, type Page } from '@playwright/test'

// 公开 API fixture 证明前端对象接力；不代替真实最小权限主体验收。
const receipt = {
  receiptRequestId: 'receipt-1',
  requestNo: 'FGR-20261014-001',
  workOrderId: 'WO-20261014-001',
  workOrderNo: 'WO-20261014-001',
  skuId: 'FG-TUB-001',
  skuCode: 'FG-TUB-001',
  producedLotNo: 'LOT-20261014-001',
  quantity: 8,
  receiptStatus: 'Posted',
}
const originalQuery =
  '?mode=batch&workOrderId=WO-20261014-001&batchOrSerial=LOT-20261014-001&materialLotId=MAT-20261014-001'
async function setup(page: Page, permissions: string[], receiptCount = 2) {
  const principal = {
    principalId: 'trace-reader',
    principalType: 'User',
    loginName: 'trace-reader',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: ['business.mes.traceability.read', ...permissions],
  }
  const session = {
    principal,
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    sessionId: 'trace-session',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
  }
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route('**/api/console/v1/**', async (route) => {
    await route.fulfill({
      json: {
        success: true,
        data: new URL(route.request().url()).pathname.endsWith('/auth/me') ? principal : session,
      },
    })
  })
  await page.route('**/api/business-console/v1/**', async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    let data: unknown = {}
    if (path.endsWith('/me/work-context')) data = { organizationId: 'org-001' }
    if (path.includes('/traceability/'))
      data = {
        nodes: [
          {
            nodeId: receipt.workOrderId,
            nodeType: 'WorkOrder',
            displayName: receipt.workOrderNo,
            status: 'Completed',
          },
          {
            nodeId: receipt.producedLotNo,
            nodeType: 'ProducedLot',
            displayName: receipt.producedLotNo,
            status: 'Posted',
          },
        ],
        edges: [
          {
            fromNodeId: receipt.workOrderId,
            toNodeId: receipt.producedLotNo,
            relationType: 'produced',
          },
        ],
      }
    if (path.endsWith('/mes/work-orders'))
      data = {
        total: 1,
        items: [
          {
            workOrderId: receipt.workOrderId,
            workOrderNo: receipt.workOrderNo,
            skuCode: receipt.skuCode,
          },
        ],
      }
    if (path.endsWith('/finished-goods-receipt-requests')) {
      expect(url.searchParams.get('workOrderId')).toBe(receipt.workOrderId)
      data = {
        total: receiptCount,
        items: receiptCount
          ? [
              receipt,
              {
                ...receipt,
                receiptRequestId: 'receipt-2',
                requestNo: 'FGR-20261014-002',
                producedLotNo: 'LOT-20261014-002',
              },
            ]
          : [],
      }
    }
    if (path.endsWith('/inventory-link')) {
      expect(url.searchParams.get('organizationId')).toBe('org-001')
      expect(url.searchParams.get('environmentId')).toBe('env-dev')
      expect(url.searchParams.get('workOrderId')).toBe(receipt.workOrderId)
      expect(path).toContain('/FGR-20261014-002/inventory-link')
      data = {
        ...receipt,
        requestNo: 'FGR-20261014-002',
        producedLotNo: 'LOT-20261014-002',
        requestedQuantity: 8,
        postedQuantity: 8,
        remainingQuantity: 0,
        linkStatus: 'qualityRestricted',
        isInventoryLinkEstablished: true,
        movements: [
          {
            movementId: 'movement-2',
            sourceDocumentId: 'FGR-20261014-002',
            skuCode: receipt.skuCode,
            lotNo: 'LOT-20261014-002',
            quantity: 8,
            uomCode: 'pcs',
            qualityStatus: 'quality',
            locationCode: 'FG-A',
          },
        ],
        balances: [
          {
            skuCode: receipt.skuCode,
            siteCode: 'SITE-001',
            locationCode: 'FG-A',
            lotNo: 'LOT-20261014-002',
            onHandQuantity: 8,
            reservedQuantity: 3,
            availableQuantity: 5,
            qualityStatus: 'quality',
            uomCode: 'pcs',
          },
        ],
      }
    }
    await route.fulfill({ json: { success: true, data } })
  })
}

test('从真实工单选择多条 FGR 中的第二条，核对库存来源并恢复原追溯查询', async ({
  page,
}, testInfo) => {
  await setup(page, ['business.mes.receipts.read', 'business.inventory.ledger.read'])
  await page.goto(`/mes/traceability${originalQuery}`)
  await page.getByRole('link', { name: '查看完工入库', exact: true }).click()
  await expect(page.getByText('FGR-20261014-001', { exact: true })).toBeVisible()
  const secondRow = page.getByRole('row').filter({ hasText: 'FGR-20261014-002' })
  await expect(secondRow).toContainText('LOT-20261014-002')
  await secondRow.getByRole('button', { name: '库存来源', exact: true }).click()
  const source = page.getByRole('region', { name: '入库库存来源' })
  await expect(source).toContainText('库存状态：质量受限')
  await expect(source).toContainText('产出批次 LOT-20261014-002')
  await expect(source).toContainText('FG-A')
  await expect(source.getByText('待检', { exact: true })).toHaveCount(2)
  await page.screenshot({
    path: testInfo.outputPath('receipt-inventory-source.png'),
    fullPage: true,
    animations: 'disabled',
  })
  await source.getByRole('button', { name: '关闭库存来源' }).click()
  await page.getByRole('button', { name: '返回追溯', exact: true }).click()
  await expect(page).toHaveURL(new RegExp('/mes/traceability\\?'))
  const returned = new URL(page.url())
  for (const [key, value] of new URLSearchParams(originalQuery))
    expect(returned.searchParams.get(key)).toBe(value)
})

test('仅追溯权限说明缺入库读取权限，不提供虚假目标', async ({ page }) => {
  await setup(page, [])
  await page.goto(`/mes/traceability${originalQuery}`)
  await expect(page.getByText('无完工入库读取权限', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: '查看完工入库', exact: true })).toHaveCount(0)
})

test('入库只读无台账权限时保留真实登记并不请求库存', async ({ page }) => {
  await setup(page, ['business.mes.receipts.read'])
  const requests: string[] = []
  page.on('request', (request) => requests.push(request.url()))
  await page.goto(`/mes/traceability${originalQuery}`)
  await page.getByRole('link', { name: '查看完工入库', exact: true }).click()
  await expect(page.getByText('FGR-20261014-001', { exact: true })).toBeVisible()
  await expect(page.getByText('无库存台账读取权限', { exact: true })).toHaveCount(2)
  await expect(page.getByRole('button', { name: '库存来源', exact: true })).toHaveCount(0)
  expect(requests.filter((url) => url.includes('/inventory-link'))).toEqual([])
})

test('关联工单没有真实 FGR 时显示空态，不从批次推导单据', async ({ page }) => {
  await setup(page, ['business.mes.receipts.read', 'business.inventory.ledger.read'], 0)
  await page.goto(`/mes/traceability${originalQuery}`)
  await page.getByRole('link', { name: '查看完工入库', exact: true }).click()
  await expect(
    page.getByText('还没有完工入库登记。末道工序报完工后，在此把成品登记入库即会出现对应记录。'),
  ).toBeVisible()
  await expect(page.getByRole('button', { name: '库存来源', exact: true })).toHaveCount(0)
})
