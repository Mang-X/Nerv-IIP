import { expect, test, type Page, type Route } from '@playwright/test'

import {
  envelope,
  expectNoHorizontalOverflow,
  expectTouchTargets,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
} from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

async function fulfillResolve(route: Route, status: string, candidates: unknown[] = []) {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(envelope({ status, candidates, total: candidates.length })),
  })
}

async function scan(page: Page, value: string) {
  const input = page.locator('input[placeholder^="扫描"]').first()
  await input.fill(value)
  await input.press('Enter')
}

test('唯一结果：首页仅凭 MES 双强 ID 直达并由目标页精确回读', async ({ page }) => {
  let requestBody: unknown
  const businessWriteRequests: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (
      url.pathname.startsWith('/api/business-console/v1/') &&
      ['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method()) &&
      url.pathname !== '/api/business-console/v1/barcode/resolve'
    ) {
      businessWriteRequests.push(`${request.method()} ${url.pathname}`)
    }
  })
  await page.route('**/api/business-console/v1/barcode/resolve', async (route) => {
    requestBody = route.request().postDataJSON()
    await fulfillResolve(route, 'resolved', [
      {
        objectType: 'mes-operation',
        strongIds: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
      },
    ])
  })

  await page.goto('/')
  await scan(page, 'OP-CODE-1')

  await expect(page).toHaveURL('/mes/operation?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByTestId('action-complete')).toBeVisible()
  expect(requestBody).toEqual({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    scannedValue: 'OP-CODE-1',
    pageIndex: 1,
    pageSize: 20,
  })
  expect(businessWriteRequests).toEqual([])
  await expectNoHorizontalOverflow(page)
})

test('歧义结果：停留原页并等待人工选择，不自动猜测', async ({ page }) => {
  await page.route('**/api/business-console/v1/barcode/resolve', (route) =>
    fulfillResolve(route, 'ambiguous', [
      { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } },
      {
        objectType: 'mes-operation',
        strongIds: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
      },
    ]),
  )

  await page.goto('/scan')
  await scan(page, 'AMB-1')
  await expect(page).toHaveURL('/scan')
  await expect(page.getByTestId('barcode-status')).toContainText('多个候选')
  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)

  await page.getByTestId('barcode-candidate-1').click()
  await expect(page).toHaveURL('/mes/operation?workOrderId=WO-1&operationTaskId=OP-1')
})

test('未知结果：只展示当前权限范围内的服务端候选，不伪装成已验证对象', async ({ page }) => {
  await page.route('**/api/business-console/v1/barcode/resolve', (route) =>
    fulfillResolve(route, 'unknown'),
  )
  await page.route('**/api/business-console/v1/search**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(
        envelope({
          query: 'UNKNOWN-9',
          results: [
            {
              objectType: 'mes-work-order',
              title: '候选工单 WO-9',
              objectNumber: 'WO-9',
              route: '/business/mes/work-orders/WO-9',
            },
          ],
        }),
      ),
    })
  })

  await page.goto('/scan')
  await scan(page, 'UNKNOWN-9')
  await page.getByTestId('barcode-search').click()

  const results = page.getByTestId('barcode-search-results')
  await expect(results).toContainText('仅供核对的候选（未验证主数据）')
  await expect(results).toContainText('候选工单 WO-9')
  await expect(results.getByRole('link')).toHaveCount(0)
  await expect(page).toHaveURL('/scan')
  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)
})

test('无权限结果：明确显示 forbidden，且不离开扫码页', async ({ page }) => {
  await page.route('**/api/business-console/v1/barcode/resolve', (route) =>
    route.fulfill({ status: 403, contentType: 'application/json', body: '{}' }),
  )

  await page.goto('/scan')
  await scan(page, 'DENIED-1')

  await expect(page.getByRole('alert')).toContainText('无权解析')
  await expect(page).toHaveURL('/scan')
  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)
})

test('过期强 ID：解析后虽可导航，目标页仍按当前权限精确回读并阻断', async ({ page }) => {
  await page.route('**/api/business-console/v1/barcode/resolve', (route) =>
    fulfillResolve(route, 'resolved', [
      {
        objectType: 'mes-operation',
        strongIds: { workOrderId: 'WO-STALE', operationTaskId: 'OP-STALE' },
      },
    ]),
  )

  await page.goto('/scan')
  await scan(page, 'STALE-1')

  await expect(page).toHaveURL('/mes/operation?workOrderId=WO-STALE&operationTaskId=OP-STALE')
  await expect(page.getByTestId('operation-deep-link-message')).toContainText(
    '未在当前主体授权作业范围内找到指定工序任务',
  )
  await expectNoHorizontalOverflow(page)
})
