import { expect, test, type Page, type Route } from '@playwright/test'

const principal = {
  principalId: 'user-admin',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@nerv-iip.local',
  organizationId: 'org-1',
  environmentId: 'env-1',
  permissionVersion: 1,
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-current',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

const isVitest = Boolean(process.env.VITEST)

if (isVitest) {
  const { describe, it } = await import('vitest')

  describe.skip('iam admin e2e', () => {
    it('runs with Playwright', () => {})
  })
} else {
  test.beforeEach(async ({ page }) => {
    await page.route('**/api/console/v1/**', routeConsoleApi)
  })

  test('admin opens IAM users roles and sessions after login', async ({ page }) => {
    await page.goto('/login')
    await login(page)

    await page.getByRole('link', { name: 'Users' }).click()
    await expect(page).toHaveURL('/iam/users')
    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
    await expect(page.getByRole('cell', { name: 'admin@nerv-iip.local' })).toBeVisible()

    await page.getByRole('link', { name: 'Roles' }).click()
    await expect(page).toHaveURL('/iam/roles')
    await expect(page.getByRole('heading', { name: 'Roles' })).toBeVisible()
    await expect(
      page.getByRole('cell', { name: 'Platform Administrator', exact: true }),
    ).toBeVisible()
    await expect(page.getByText('iam.users.read')).toBeVisible()

    await page.getByRole('link', { name: 'Sessions' }).click()
    await expect(page).toHaveURL('/iam/sessions')
    await expect(page.getByRole('heading', { name: 'Sessions' })).toBeVisible()
    await expect(page.getByText('session-current')).toBeVisible()
  })
}

async function login(page: Page) {
  await page.getByLabel('Login name').fill('admin')
  await page.getByLabel('Password').fill('Admin123!')
  await page.getByRole('button', { name: 'Sign in' }).click()
}

async function routeConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  const { pathname } = url

  if (pathname === '/api/console/v1/auth/login' || pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope(session))
  }

  if (pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(principal))
  }

  if (pathname === '/api/console/v1/instances') {
    return fulfillJson(route, envelope({ pageIndex: 1, pageSize: 20, totalCount: 0, items: [] }))
  }

  if (pathname === '/api/console/v1/iam/users') {
    return fulfillJson(
      route,
      envelope({
        pageIndex: 1,
        pageSize: 20,
        totalCount: 1,
        items: [
          {
            userId: 'user-admin',
            loginName: 'admin',
            email: 'admin@nerv-iip.local',
            enabled: true,
          },
        ],
      }),
    )
  }

  if (pathname === '/api/console/v1/iam/roles') {
    return fulfillJson(
      route,
      envelope({
        pageIndex: 1,
        pageSize: 20,
        totalCount: 1,
        items: [
          {
            roleId: 'role-platform-admin',
            roleName: 'Platform Administrator',
            permissionCodes: ['iam.users.read', 'iam.roles.read'],
          },
        ],
      }),
    )
  }

  if (pathname === '/api/console/v1/iam/permissions') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            code: 'iam.users.read',
            domain: 'iam',
            description: 'Read IAM users.',
            seeded: true,
          },
          {
            code: 'iam.roles.read',
            domain: 'iam',
            description: 'Read IAM roles.',
            seeded: true,
          },
        ],
      }),
    )
  }

  if (pathname === '/api/console/v1/iam/sessions') {
    return fulfillJson(
      route,
      envelope({
        pageIndex: 1,
        pageSize: 20,
        totalCount: 1,
        items: [
          {
            sessionId: 'session-current',
            userId: 'user-admin',
            issuedAtUtc: '2026-05-20T08:00:00Z',
            expiresAtUtc: '2099-01-01T00:00:00Z',
            revokedAtUtc: null,
            permissionVersion: 1,
          },
        ],
      }),
    )
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
