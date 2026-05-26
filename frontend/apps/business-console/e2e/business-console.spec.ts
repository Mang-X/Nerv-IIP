import { expect, test, type Page, type Route } from '@playwright/test'

const STORAGE_KEY = 'nerv-iip.business-console.auth'

const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

test.beforeEach(async ({ page }) => {
  await seedStoredSession(page)
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

test('business console smoke pages render', async ({ page }) => {
  await expectHeading(page, '/master-data/skus', 'SKU maintenance')
  await expectHeading(page, '/inventory/availability', 'Availability')
  await expectHeading(page, '/quality/ncrs', 'NCRs')
  await expectHeading(page, '/mes', 'MES 总览')
  await expectHeading(page, '/mes/foundation', '基础就绪')
  await expectHeading(page, '/mes/work-orders', '工单执行')
  await expectHeading(page, '/mes/work-order-detail/WO-001', '工单详情')
  await expectHeading(page, '/mes/operation-tasks', '工序任务')
  await expectHeading(page, '/mes/wip', '在制状态')
  await expectHeading(page, '/mes/production-reports', '生产报工')
  await expectHeading(page, '/mes/receipts', '完工入库')
  await expectHeading(page, '/mes/capacity', '产能影响')
  await expectHeading(page, '/mes/schedules', '规则排程')
})

async function expectHeading(page: Page, path: string, heading: string) {
  await page.goto(path)
  await expect(page.getByRole('heading', { name: heading, level: 1 })).toBeVisible()
}

async function seedStoredSession(page: Page) {
  await page.addInitScript(
    ({ key, storedSession }) => {
      localStorage.setItem(key, JSON.stringify(storedSession))
    },
    {
      key: STORAGE_KEY,
      storedSession: {
        principal,
        refreshToken: session.refreshToken,
        sessionId: session.sessionId,
      },
    },
  )
}

async function routeConsoleApi(route: Route) {
  const url = new URL(route.request().url())

  if (url.pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope(session))
  }

  if (url.pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(principal))
  }

  return route.fallback()
}

async function routeBusinessConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  const { pathname } = url

  if (pathname === '/api/business-console/v1/master-data/skus') {
    return fulfillJson(
      route,
      envelope({
        resources: [
          {
            resourceType: 'sku',
            code: 'SKU-001',
            displayName: 'Demo SKU',
            active: true,
            snapshotVersion: 'v1',
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/inventory/availability') {
    return fulfillJson(
      route,
      envelope({
        skuCode: 'SKU-001',
        uomCode: 'EA',
        onHandQuantity: 12,
        reservedQuantity: 2,
        availableQuantity: 10,
        lines: [
          {
            locationCode: 'A-01',
            qualityStatus: 'available',
            ownerType: 'owned',
            onHandQuantity: 12,
            availableQuantity: 10,
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/quality/ncrs') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            id: 'ncr-1',
            code: 'NCR-001',
            status: 'open',
            summary: 'Dimension out of tolerance',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-001',
            skuId: 'SKU-001',
            quantity: 10,
            priority: 1,
            dueUtc: '2026-05-25T12:00:00.000Z',
            status: 'released',
            operationTasks: [
              {
                operationTaskId: 'op-1',
                status: 'ready',
                operationSequence: 10,
                workCenterId: 'WC-001',
              },
            ],
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/overview') {
    return fulfillJson(
      route,
      envelope({
        counts: [
          { key: 'WorkOrders', count: 1, status: 'Released' },
          { key: 'OperationTasks', count: 1, status: 'Ready' },
        ],
        blockers: [],
        pendingWork: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/foundation-readiness') {
    return fulfillJson(
      route,
      envelope({
        status: 'Ready',
        areas: [{ areaCode: 'master-data', status: 'Ready', issues: [] }],
        blockingIssues: [],
        warningIssues: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders/WO-001') {
    return fulfillJson(
      route,
      envelope({
        workOrderId: 'WO-001',
        skuId: 'SKU-001',
        quantity: 10,
        status: 'released',
        readinessStatus: 'Ready',
        blockingReasons: [],
        operationTasks: [
          {
            operationTaskId: 'op-1',
            workOrderId: 'WO-001',
            status: 'Ready',
            operationSequence: 10,
            workCenterId: 'WC-001',
            qualityStatus: 'Ready',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders/WO-001/material-readiness') {
    return fulfillJson(
      route,
      envelope({
        workOrderId: 'WO-001',
        readinessStatus: 'Ready',
        blockingReasons: [],
        items: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/operation-tasks') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            operationTaskId: 'op-1',
            workOrderId: 'WO-001',
            status: 'Ready',
            operationSequence: 10,
            workCenterId: 'WC-001',
            qualityStatus: 'Ready',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/wip') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-001',
            operationTaskId: 'op-1',
            workCenterId: 'WC-001',
            status: 'Ready',
            plannedQuantity: 10,
            goodQuantity: 5,
            scrapQuantity: 0,
            blockingReasons: [],
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/production-reports') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            productionReportId: 'report-1',
            workOrderId: 'WO-001',
            operationTaskId: 'op-1',
            goodQuantity: 5,
            scrapQuantity: 0,
            reportedAtUtc: '2026-05-25T13:00:00.000Z',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/finished-goods-receipt-requests') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            receiptRequestId: 'receipt-1',
            workOrderId: 'WO-001',
            skuId: 'SKU-001',
            quantity: 5,
            receiptStatus: 'Pending',
            requestedAtUtc: '2026-05-25T14:00:00.000Z',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/capacity-impacts') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            impactId: 'impact-1',
            workCenterId: 'WC-001',
            deviceAssetId: 'DEV-001',
            status: 'Active',
            effectiveFromUtc: '2026-05-25T15:00:00.000Z',
            reasonCode: 'MAINTENANCE',
          },
        ],
      }),
    )
  }

  return fulfillJson(route, envelope({}))
}

function envelope<T>(data: T) {
  return {
    success: true,
    data,
  }
}

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}
