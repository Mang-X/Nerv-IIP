import { expect, test, type Page, type Route } from '@playwright/test'

const STORAGE_KEY = 'nerv-iip.console.auth'

const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-1',
  environmentId: 'env-1',
  permissionVersion: 1,
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

const instance = {
  applicationKey: 'payments',
  applicationName: 'Payments API',
  version: '1.2.3',
  nodeKey: 'node-1',
  nodeName: 'Node 1',
  instanceKey: 'inst-1',
  instanceName: 'payments-1',
  reportedStatus: 'Running',
  healthStatus: 'Healthy',
}

const operationTask = {
  operationTaskId: 'op-123',
  organizationId: 'org-1',
  environmentId: 'env-1',
  instanceKey: 'inst-1',
  operationCode: 'RestartInstance',
  status: 'Running',
  requestedBy: 'admin',
  requestedAtUtc: '2026-05-19T08:00:00.000Z',
  currentAttemptId: 'attempt-1',
  attempts: [
    {
      attemptId: 'attempt-1',
      status: 'Running',
      startedAtUtc: '2026-05-19T08:00:01.000Z',
      attemptNo: 1,
      maxAttempts: 3,
    },
  ],
  auditRecords: [
    {
      auditRecordId: 'audit-1',
      operationTaskId: 'op-123',
      action: 'RestartRequested',
      actor: 'admin',
      occurredAtUtc: '2026-05-19T08:00:00.000Z',
      correlationId: 'corr-1',
    },
  ],
}

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
})

test('login succeeds, redirects home, and shows the instance list', async ({ page }) => {
  await page.goto('/login')

  await login(page)

  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: 'Instances' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'Payments API' })).toBeVisible()
  await expect(page.getByRole('cell', { name: 'payments-1' })).toBeVisible()
})

test('visiting a protected page while signed out redirects to login with redirect query', async ({
  page,
}) => {
  await page.goto('/operations/op-123')

  await expect(page).toHaveURL('/login?redirect=/operations/op-123')
  await expect(page.getByRole('heading', { name: 'Nerv-IIP Console' })).toBeVisible()
})

test('login returns to the original protected path from redirect query', async ({ page }) => {
  await page.goto('/operations/op-123')

  await login(page)

  await expect(page).toHaveURL('/operations/op-123')
  await expect(page.getByRole('heading', { name: 'RestartInstance' })).toBeVisible()
  await expect(page.getByText('attempt-1')).toBeVisible()
})

test('restarting an instance opens the operation detail timeline', async ({ page }) => {
  await page.goto('/login')
  await login(page)

  await page.getByRole('button', { name: 'Restart' }).click()

  await expect(page).toHaveURL('/operations/op-123')
  await expect(page.getByRole('heading', { name: 'RestartInstance' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Attempts' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Audit Records' })).toBeVisible()
  await expect(page.getByText('RestartRequested')).toBeVisible()
})

test('a 401 API response clears the stored session and redirects to login', async ({ page }) => {
  await seedStoredSession(page)
  await page.route('**/api/console/v1/instances*', async (route) => {
    await route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ success: false, message: 'Unauthorized' }),
    })
  })

  await page.goto('/')

  await expect(page).toHaveURL('/login?redirect=/')
  await expect(page.getByRole('heading', { name: 'Nerv-IIP Console' })).toBeVisible()
  await expect(page.evaluate((key) => localStorage.getItem(key), STORAGE_KEY)).resolves.toBeNull()
})

test('unknown paths are protected and redirect to login', async ({ page }) => {
  await page.goto('/xxx')

  await expect(page).toHaveURL('/login?redirect=/xxx')
  await expect(page.getByRole('heading', { name: 'Nerv-IIP Console' })).toBeVisible()
})

async function login(page: Page) {
  await page.getByLabel('Login name').fill('admin')
  await page.getByLabel('Password').fill('admin')
  await page.getByRole('button', { name: 'Sign in' }).click()
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
  const { pathname } = url

  if (pathname === '/api/console/v1/auth/login') {
    return fulfillJson(route, envelope(session))
  }

  if (pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope(session))
  }

  if (pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(principal))
  }

  if (pathname === '/api/console/v1/instances') {
    return fulfillJson(
      route,
      envelope({
        pageIndex: 1,
        pageSize: 20,
        totalCount: 1,
        items: [instance],
      }),
    )
  }

  if (pathname === '/api/console/v1/instances/inst-1') {
    return fulfillJson(route, envelope({ ...instance, capabilities: [], metadata: {} }))
  }

  if (pathname === '/api/console/v1/instances/inst-1/operations/restart') {
    return fulfillJson(route, envelope(operationTask))
  }

  if (pathname === '/api/console/v1/operation-tasks/op-123') {
    return fulfillJson(route, envelope(operationTask))
  }

  return route.fallback()
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
