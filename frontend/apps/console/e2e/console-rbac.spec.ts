import { expect, test, type Page } from '@playwright/test'

async function mockSession(page: Page, permissionCodes: string[]) {
  const principal = {
    principalId: 'user-reader',
    principalType: 'user',
    loginName: 'reader',
    organizationId: 'org-1',
    environmentId: 'env-1',
    permissionVersion: 3,
    permissionCodes,
  }
  const session = {
    accessToken: 'test-access-token',
    refreshToken: 'test-refresh-token',
    sessionId: 'session-reader',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
    principal,
  }
  await page.route('**/api/console/v1/auth/refresh', (route) =>
    route.fulfill({ json: { success: true, data: session } }),
  )
  await page.route('**/api/console/v1/auth/me', (route) =>
    route.fulfill({ json: { success: true, data: principal } }),
  )
  await page.addInitScript(
    (storedSession) => {
      localStorage.setItem('nerv-iip.console.auth', JSON.stringify(storedSession))
    },
    { refreshToken: session.refreshToken, sessionId: session.sessionId, principal },
  )
}

test('a user reader can open the effective-access preview at 1440px', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 })
  await mockSession(page, ['iam.users.read'])
  const user = {
    userId: 'user-target',
    loginName: 'production-planner',
    email: 'production-planner@plant.example',
    enabled: true,
  }
  await page.route('**/api/console/v1/iam/users?*', (route) =>
    route.fulfill({ json: { success: true, data: { items: [user], totalCount: 1 } } }),
  )
  await page.route('**/api/console/v1/iam/users/user-target/effective-access?*', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: {
          ...user,
          status: 'active',
          permissionVersion: 3,
          roles: [{ roleId: 'role-planner', roleName: '生产计划员' }],
          permissions: [
            {
              permissionCode: 'business.planning.demands.read',
              roleIds: ['role-planner'],
              scopeGrants: [
                {
                  sourceKind: 'role',
                  sourceId: 'role-planner',
                  scopeKind: 'site',
                  scopeId: 'SITE-A',
                },
              ],
            },
          ],
        },
      },
    }),
  )

  await page.goto('/')
  const menu = page.getByRole('button', { name: '操作 production-planner' })
  await menu.waitFor()
  const menuBounds = await menu.boundingBox()
  await menu.click()
  const previewRequest = page.waitForRequest((request) =>
    request.url().includes('/effective-access?'),
  )
  await page.getByRole('menuitem', { name: '有效权限' }).click()
  const query = new URL((await previewRequest).url()).searchParams
  const dialog = page.getByRole('dialog', { name: '有效权限与来源' })
  await dialog.getByText('business.planning.demands.read', { exact: true }).click()

  await expect(page).toHaveURL('/iam/users')
  expect(menuBounds?.x).toBeGreaterThanOrEqual(0)
  expect(menuBounds!.x + menuBounds!.width).toBeLessThanOrEqual(1440)
  expect(query.get('orgId')).toBe('org-1')
  expect(query.get('envId')).toBe('env-1')
  await expect(dialog.getByText(/SITE-A/)).toBeVisible()
  await expect(dialog.getByRole('button', { name: '保存范围' })).toHaveCount(0)
})

test('operation detail stops polling once the task completes', async ({ page }) => {
  await mockSession(page, ['ops.tasks.read'])
  await page.clock.install()
  let observed = 0
  await page.route('**/api/console/v1/operation-tasks/task-1?*', (route) => {
    observed++
    return route.fulfill({
      json: {
        success: true,
        data: {
          operationTaskId: 'task-1',
          operationCode: 'lifecycle.restart',
          status: observed === 1 ? 'queued' : 'completed',
        },
      },
    })
  })

  await page.goto('/operations/task-1')
  await page.getByText('排队中', { exact: true }).waitFor()
  await page.clock.runFor(1000)
  await page.getByText('已完成', { exact: true }).waitFor()
  await page.clock.runFor(5000)

  expect(observed).toBe(2)
  await expect(page.getByRole('link', { name: '返回实例' })).toHaveCount(0)
})
