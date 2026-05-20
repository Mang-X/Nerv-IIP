import { expect, test, type Locator, type Page, type Route } from '@playwright/test'

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

interface ConsoleIamUser {
  email: string
  enabled: boolean
  loginName: string
  userId: string
}

interface ConsoleIamRole {
  permissionCodes: string[]
  roleId: string
  roleName: string
}

interface ConsoleIamSession {
  expiresAtUtc: string
  issuedAtUtc: string
  permissionVersion: number
  revokedAtUtc: string | null
  sessionId: string
  userId: string
}

interface ConsoleIamFixture {
  requests: {
    createdRoles: unknown[]
    createdUsers: unknown[]
    disabledUsers: string[]
    resetPasswords: unknown[]
    revokedSessions: string[]
    updatedRolePermissions: unknown[]
    updatedUsers: unknown[]
    deniedIamRequests: number
  }
  route: (route: Route) => Promise<void>
}

const isVitest = Boolean(process.env.VITEST)

if (isVitest) {
  const { describe, it } = await import('vitest')

  describe.skip('iam admin e2e', () => {
    it('runs with Playwright', () => {})
  })
} else {
  test('admin manages users roles and sessions after login', async ({ page }) => {
    const fixture = createConsoleIamFixture()
    await page.route('**/api/console/v1/**', fixture.route)
    await page.setViewportSize({ width: 1366, height: 1200 })

    await page.goto('/login')
    await login(page)

    await page.getByRole('link', { name: 'Users' }).click()
    await expect(page).toHaveURL('/iam/users')
    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
    await expect(page.getByRole('cell', { name: 'admin@nerv-iip.local' })).toBeVisible()

    await page.getByRole('button', { name: 'Create user' }).click()
    const createUserDialog = page.getByRole('dialog', { name: 'Create user' })
    await expect(createUserDialog).toBeVisible()
    await createUserDialog.getByLabel('Login name').fill('operator')
    await createUserDialog.getByLabel('Email').fill('operator@nerv-iip.local')
    await createUserDialog.getByLabel('Password').fill('Operator123!')
    await createUserDialog.getByRole('button', { name: 'Create user' }).click()
    await expect(page.getByRole('cell', { name: 'operator@nerv-iip.local' })).toBeVisible()
    expect(fixture.requests.createdUsers).toContainEqual({
      email: 'operator@nerv-iip.local',
      loginName: 'operator',
      password: 'Operator123!',
    })

    await openUserAction(page, 'operator', 'Edit')
    const editUserDialog = page.getByRole('dialog', { name: 'Edit user' })
    await expect(editUserDialog).toBeVisible()
    await editUserDialog.getByLabel('Login name').fill('operator-edited')
    await editUserDialog.getByLabel('Email').fill('operator-edited@nerv-iip.local')
    await editUserDialog.getByRole('button', { name: 'Save changes' }).click()
    await expect(page.getByRole('cell', { name: 'operator-edited@nerv-iip.local' })).toBeVisible()
    expect(fixture.requests.updatedUsers).toContainEqual({
      body: {
        email: 'operator-edited@nerv-iip.local',
        enabled: true,
        loginName: 'operator-edited',
      },
      userId: 'user-operator',
    })

    await openUserAction(page, 'operator-edited', 'Reset password')
    const resetPasswordDialog = page.getByRole('dialog', { name: 'Reset password' })
    await expect(resetPasswordDialog).toBeVisible()
    await resetPasswordDialog.getByLabel('New password').fill('Operator456!')
    await resetPasswordDialog.getByRole('button', { name: 'Reset password' }).click()
    expect(fixture.requests.resetPasswords).toContainEqual({
      body: { newPassword: 'Operator456!' },
      userId: 'user-operator',
    })

    await openUserAction(page, 'operator-edited', 'Disable')
    await expect(
      page.getByRole('row', { name: /operator-edited/ }).getByText('Disabled'),
    ).toBeVisible()
    expect(fixture.requests.disabledUsers).toContain('user-operator')

    await page.getByRole('link', { name: 'Roles' }).click()
    await expect(page).toHaveURL('/iam/roles')
    await expect(page.getByRole('heading', { name: 'Roles' })).toBeVisible()
    await expect(
      page.getByRole('cell', { name: 'Platform Administrator', exact: true }),
    ).toBeVisible()
    await expect(page.getByText('iam.users.read')).toBeVisible()

    await page.getByRole('button', { name: 'Create role' }).click()
    const createRoleDialog = page.getByRole('dialog', { name: 'Create role' })
    await expect(createRoleDialog).toBeVisible()
    await createRoleDialog.getByLabel('Role name').fill('Operations Auditor')
    await createRoleDialog.getByRole('checkbox', { name: /iam\.users\.read/ }).setChecked(true)
    await createRoleDialog.getByRole('button', { name: 'Create role' }).click()
    await expect(page.getByRole('cell', { name: 'Operations Auditor', exact: true })).toBeVisible()
    expect(fixture.requests.createdRoles).toContainEqual({
      permissionCodes: ['iam.users.read'],
      roleName: 'Operations Auditor',
    })

    await openRoleAction(page, 'Operations Auditor', 'Edit permissions')
    const editRoleDialog = page.getByRole('dialog', { name: 'Edit role permissions' })
    await expect(editRoleDialog).toBeVisible()
    await editRoleDialog.getByRole('checkbox', { name: /iam\.roles\.read/ }).setChecked(true)
    await editRoleDialog.getByRole('button', { name: 'Save permissions' }).click()
    await expect(
      page.getByRole('row', { name: /Operations Auditor/ }).getByText('iam.roles.read'),
    ).toBeVisible()
    expect(fixture.requests.updatedRolePermissions).toContainEqual({
      body: { permissionCodes: ['iam.roles.read', 'iam.users.read'] },
      roleId: 'role-operations-auditor',
    })

    await page.getByRole('link', { name: 'Sessions' }).click()
    await expect(page).toHaveURL('/iam/sessions')
    await expect(page.getByRole('heading', { name: 'Sessions' })).toBeVisible()
    await expect(page.getByText('session-current')).toBeVisible()

    await page.getByRole('button', { name: 'Revoke session session-operator' }).click()
    const revokeDialog = page.getByRole('alertdialog', { name: 'Revoke session' })
    await expect(revokeDialog).toBeVisible()
    await revokeDialog.getByRole('button', { name: 'Revoke session' }).click()
    await expect(
      page.getByRole('row', { name: /session-operator/ }).getByText('Revoked'),
    ).toBeVisible()
    expect(fixture.requests.revokedSessions).toContain('session-operator')
  })

  test('permission denied keeps IAM admin pages in a safe state', async ({ page }) => {
    const fixture = createConsoleIamFixture({ denyIam: true })
    await page.route('**/api/console/v1/**', fixture.route)

    await page.goto('/login')
    await login(page)

    await page.getByRole('link', { name: 'Users' }).click()

    await expect(page).toHaveURL('/iam/users')
    await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
    await expect(page.getByText('Unable to complete user request')).toBeVisible()
    await expect(page.getByText('Unable to complete IAM request.')).toBeVisible()
    await expect(page.getByRole('cell', { name: 'admin@nerv-iip.local' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Create user' })).toBeVisible()
    expect(fixture.requests.deniedIamRequests).toBeGreaterThan(0)
  })

  test('IAM admin pages pass responsive layout and semantic color smoke checks', async ({
    page,
  }) => {
    const fixture = createConsoleIamFixture()
    await page.route('**/api/console/v1/**', fixture.route)

    await page.setViewportSize({ width: 1366, height: 900 })
    await page.goto('/login')
    await login(page)

    for (const path of ['/iam/users', '/iam/roles', '/iam/sessions']) {
      await page.goto(path)
      await expect(page.locator('main')).toBeVisible()
      await expectNoHorizontalOverflow(page)
      await expectNoVisibleTextOverlap(page.locator('main'))
      await expectSelectedNavigationIsBlue(page)
    }

    await page.goto('/iam/users')
    await expectNonPrimaryBadge(page.getByText('Enabled'))
    await page.goto('/iam/sessions')
    await expectNonPrimaryBadge(page.getByText('Active').first())

    await page.goto('/iam/users')
    await page.getByRole('button', { name: 'Create user' }).click()
    await expect(page.getByRole('dialog', { name: 'Create user' })).toBeVisible()

    await page.goto('/iam/roles')
    await page.getByRole('button', { name: 'Create role' }).click()
    await expect(page.getByRole('dialog', { name: 'Create role' })).toBeVisible()

    await page.goto('/iam/sessions')
    await page.getByRole('button', { name: 'Revoke session session-operator' }).click()
    await expect(page.getByRole('alertdialog', { name: 'Revoke session' })).toBeVisible()

    await page.setViewportSize({ width: 390, height: 844 })
    for (const path of ['/iam/users', '/iam/roles', '/iam/sessions']) {
      await page.goto(path)
      await expect(page.locator('main')).toBeVisible()
      await expectNoHorizontalOverflow(page)
      await expectNoVisibleTextOverlap(page.locator('main'))
    }
  })
}

async function login(page: Page) {
  await page.getByLabel('Login name').fill('admin')
  await page.getByLabel('Password').fill('Admin123!')
  await page.getByRole('button', { name: 'Sign in' }).click()
}

async function openUserAction(page: Page, loginName: string, action: string) {
  await page.getByRole('button', { name: `Open actions for ${loginName}` }).click()
  await page.getByRole('menuitem', { name: action }).click()
}

async function openRoleAction(page: Page, roleName: string, action: string) {
  await page.getByRole('button', { name: `Open actions for ${roleName}` }).click()
  await page.getByRole('menuitem', { name: action }).click()
}

function createConsoleIamFixture(options: { denyIam?: boolean } = {}): ConsoleIamFixture {
  const users: ConsoleIamUser[] = [
    {
      userId: 'user-admin',
      loginName: 'admin',
      email: 'admin@nerv-iip.local',
      enabled: true,
    },
  ]
  const roles: ConsoleIamRole[] = [
    {
      roleId: 'role-platform-admin',
      roleName: 'Platform Administrator',
      permissionCodes: ['iam.users.read', 'iam.roles.read'],
    },
  ]
  const sessions: ConsoleIamSession[] = [
    {
      sessionId: 'session-current',
      userId: 'user-admin',
      issuedAtUtc: '2026-05-20T08:00:00Z',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      revokedAtUtc: null,
      permissionVersion: 1,
    },
    {
      sessionId: 'session-operator',
      userId: 'user-operator',
      issuedAtUtc: '2026-05-20T09:00:00Z',
      expiresAtUtc: '2099-01-01T00:00:00Z',
      revokedAtUtc: null,
      permissionVersion: 1,
    },
  ]
  const requests: ConsoleIamFixture['requests'] = {
    createdRoles: [],
    createdUsers: [],
    disabledUsers: [],
    resetPasswords: [],
    revokedSessions: [],
    updatedRolePermissions: [],
    updatedUsers: [],
    deniedIamRequests: 0,
  }

  return {
    requests,
    route: async (route) => {
      const url = new URL(route.request().url())
      const { pathname } = url
      const method = route.request().method()

      if (
        pathname === '/api/console/v1/auth/login' ||
        pathname === '/api/console/v1/auth/refresh'
      ) {
        return fulfillJson(route, envelope(session))
      }

      if (pathname === '/api/console/v1/auth/me') {
        return fulfillJson(route, envelope(principal))
      }

      if (pathname === '/api/console/v1/instances') {
        return fulfillJson(
          route,
          envelope({ pageIndex: 1, pageSize: 20, totalCount: 0, items: [] }),
        )
      }

      if (options.denyIam && pathname.startsWith('/api/console/v1/iam/')) {
        requests.deniedIamRequests += 1
        return fulfillJson(
          route,
          { success: false, message: 'Permission denied by IAM policy.' },
          403,
        )
      }

      if (pathname === '/api/console/v1/iam/users' && method === 'GET') {
        return fulfillJson(route, envelope(pageOf(users)))
      }

      if (pathname === '/api/console/v1/iam/users' && method === 'POST') {
        const body = route.request().postDataJSON() as {
          email: string
          loginName: string
          password: string
        }
        requests.createdUsers.push(body)
        const created = {
          userId: `user-${body.loginName}`,
          loginName: body.loginName,
          email: body.email,
          enabled: true,
        }
        users.push(created)
        return fulfillJson(route, envelope(created), 201)
      }

      const userId = matchPath(pathname, /^\/api\/console\/v1\/iam\/users\/([^/]+)$/)
      if (userId && method === 'PATCH') {
        const body = route.request().postDataJSON() as {
          email: string
          enabled: boolean
          loginName: string
        }
        requests.updatedUsers.push({ body, userId })
        const user = users.find((item) => item.userId === userId)
        if (!user) {
          return fulfillJson(route, { success: false, message: 'User not found.' }, 404)
        }
        Object.assign(user, body)
        return fulfillJson(route, envelope(user))
      }

      const disableUserId = matchPath(
        pathname,
        /^\/api\/console\/v1\/iam\/users\/([^/]+)\/disable$/,
      )
      if (disableUserId && method === 'POST') {
        requests.disabledUsers.push(disableUserId)
        const user = users.find((item) => item.userId === disableUserId)
        if (user) {
          user.enabled = false
        }
        return fulfillJson(route, envelope({}))
      }

      const resetUserId = matchPath(
        pathname,
        /^\/api\/console\/v1\/iam\/users\/([^/]+)\/reset-password$/,
      )
      if (resetUserId && method === 'POST') {
        const body = route.request().postDataJSON()
        requests.resetPasswords.push({ body, userId: resetUserId })
        return fulfillJson(route, envelope({}))
      }

      if (pathname === '/api/console/v1/iam/roles' && method === 'GET') {
        return fulfillJson(route, envelope(pageOf(roles)))
      }

      if (pathname === '/api/console/v1/iam/roles' && method === 'POST') {
        const body = route.request().postDataJSON() as {
          permissionCodes: string[]
          roleName: string
        }
        requests.createdRoles.push(body)
        const created = {
          roleId: `role-${body.roleName.toLowerCase().replace(/\s+/g, '-')}`,
          roleName: body.roleName,
          permissionCodes: [...body.permissionCodes].sort(),
        }
        roles.push(created)
        return fulfillJson(route, envelope(created), 201)
      }

      const roleId = matchPath(pathname, /^\/api\/console\/v1\/iam\/roles\/([^/]+)\/permissions$/)
      if (roleId && method === 'PATCH') {
        const body = route.request().postDataJSON() as { permissionCodes: string[] }
        const normalizedBody = { permissionCodes: [...body.permissionCodes].sort() }
        requests.updatedRolePermissions.push({ body: normalizedBody, roleId })
        const role = roles.find((item) => item.roleId === roleId)
        if (!role) {
          return fulfillJson(route, { success: false, message: 'Role not found.' }, 404)
        }
        role.permissionCodes = normalizedBody.permissionCodes
        return fulfillJson(route, envelope(role))
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

      if (pathname === '/api/console/v1/iam/sessions' && method === 'GET') {
        return fulfillJson(route, envelope(pageOf(sessions)))
      }

      const sessionId = matchPath(pathname, /^\/api\/console\/v1\/iam\/sessions\/([^/]+)\/revoke$/)
      if (sessionId && method === 'POST') {
        requests.revokedSessions.push(sessionId)
        const current = sessions.find((item) => item.sessionId === sessionId)
        if (current) {
          current.revokedAtUtc = '2026-05-20T10:00:00Z'
        }
        return fulfillJson(route, envelope({}))
      }

      return route.fallback()
    },
  }
}

function matchPath(pathname: string, pattern: RegExp) {
  return pattern.exec(pathname)?.[1]
}

function pageOf<T>(items: T[]) {
  return {
    pageIndex: 1,
    pageSize: 20,
    totalCount: items.length,
    items,
  }
}

async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => {
    const documentElement = document.documentElement
    return documentElement.scrollWidth - documentElement.clientWidth
  })

  expect(overflow).toBeLessThanOrEqual(1)
}

async function expectNoVisibleTextOverlap(scope: Locator) {
  const overlaps = await scope.evaluate((root) => {
    const elements = [
      ...root.querySelectorAll<HTMLElement>('h1,h2,h3,p,a,button,td,th,label,span'),
    ].filter((element) => {
      const rect = element.getBoundingClientRect()
      const style = window.getComputedStyle(element)
      return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden'
    })

    for (let index = 0; index < elements.length; index += 1) {
      const first = elements[index].getBoundingClientRect()
      for (let nextIndex = index + 1; nextIndex < elements.length; nextIndex += 1) {
        const second = elements[nextIndex].getBoundingClientRect()
        const sameAncestor =
          elements[index].contains(elements[nextIndex]) ||
          elements[nextIndex].contains(elements[index])
        const intersects =
          first.left < second.right &&
          first.right > second.left &&
          first.top < second.bottom &&
          first.bottom > second.top
        if (intersects && !sameAncestor) {
          return true
        }
      }
    }

    return false
  })

  expect(overlaps).toBe(false)
}

async function expectSelectedNavigationIsBlue(page: Page) {
  const activeLinkColors = await page.locator('a[aria-current="page"]').evaluate((element) => {
    const style = window.getComputedStyle(element)
    return {
      className: element.className,
      color: style.color,
    }
  })

  expect(activeLinkColors.className).toContain('router-link-active')
  expect(isBlueRgb(activeLinkColors.color)).toBe(true)
}

async function expectNonPrimaryBadge(badge: Locator) {
  const colorData = await badge.evaluate((element) => {
    const style = window.getComputedStyle(element)
    return {
      background: style.backgroundColor,
      color: style.color,
      variant: element.getAttribute('data-variant'),
    }
  })

  expect(colorData.variant).not.toBe('default')
  expect(colorData.background).not.toBe('rgb(0, 72, 184)')
}

function isBlueRgb(value: string) {
  const channels = /rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(value)
  if (!channels) {
    return value.includes('oklch') || value.includes('color')
  }

  const red = Number(channels[1])
  const green = Number(channels[2])
  const blue = Number(channels[3])
  return blue > red && blue >= green
}

function envelope<T>(data: T) {
  return {
    success: true,
    data,
  }
}

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}
