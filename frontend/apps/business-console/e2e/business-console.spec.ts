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
  await expectHeading(page, '/mes/work-orders', 'Work orders')
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
