import { expect, test } from '@playwright/test'
import {
  expectNoHorizontalOverflow,
  expectTouchTargets,
  principal,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
  workerProfile,
} from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await page.route('**/api/business-console/v1/me/work-context?**', async (route) => {
    const permissionCode = new URL(route.request().url()).searchParams.get('permissionCode')
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        message: null,
        data: {
          organizationId: principal.organizationId,
          environmentId: principal.environmentId,
          applicablePermissionCode: permissionCode,
          resolvedAtUtc: '2026-07-31T12:00:00.000Z',
          principal: {
            id: principal.principalId,
            principalType: principal.principalType,
            loginName: principal.loginName,
            roles: [{ id: 'role-pda-operator', displayName: 'PDA 操作员' }],
          },
          resolutionStatus: 'resolved',
          worker: {
            id: 'worker-012',
            userId: principal.principalId,
            employeeNo: workerProfile.employeeNo,
            name: workerProfile.displayName,
            departmentName: '生产部',
            jobTitle: workerProfile.jobTitle,
            employmentStatus: 'active',
          },
          teams: [{ id: 'team-a', name: workerProfile.teams[0]?.teamName ?? '', isLeader: false }],
          authorizedScopes: [
            {
              kind: 'self',
              id: principal.principalId,
              displayName: workerProfile.displayName,
              relationship: 'self',
              authorizationPaths: [],
            },
          ],
          availableScopeKinds: ['self'],
          issues: [],
        },
      }),
    })
  })
})

test('logs in and lands on the workbench home', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('账号').fill('operator01')
  await page.getByLabel('密码').fill('Operator123!')
  await page.getByRole('button', { name: '登录' }).click()

  await expect(page).toHaveURL('/')
  // 首页头部呈现登录人身份（员工目录档案：姓名 + 岗位/班组）。
  await expect(page.getByTestId('home-name')).toHaveText('李秀英')
})

test('failed login shows an error and stays on the login route', async ({ page }) => {
  // Override just the login endpoint with a 401; later-registered routes win in Playwright.
  await page.route('**/api/console/v1/auth/login', (route) =>
    route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ success: false, message: null, data: null }),
    }),
  )

  await page.goto('/login')
  await page.getByLabel('账号').fill('operator01')
  await page.getByLabel('密码').fill('wrong-password')
  await page.getByRole('button', { name: '登录' }).click()

  // Error surfaces, the router did NOT navigate away, and the login form is still shown.
  await expect(page.getByText('账号密码错误或会话已过期。')).toBeVisible()
  await expect(page).toHaveURL('/login')
  await expect(page.getByRole('button', { name: '登录' })).toBeVisible()
})

test('home shows scan bar, my dispatch tasks and a permission-gated app wall', async ({ page }) => {
  await seedStoredSession(page)
  await page.goto('/')

  await expect(page.getByTestId('home-name')).toHaveText('李秀英')
  // scan bar focus input present
  await expect(page.locator('input[placeholder^="扫描"]')).toBeVisible()
  // 「我的任务」呈现派工到本人的工序任务（服务端 assignedUserId 过滤，中文状态标签）。
  await expect(page.getByTestId('home-my-tasks')).toContainText('WO-2026-00001')
  await expect(page.getByTestId('home-my-tasks')).toContainText('进行中')
  // 仓储摘要块随 WMS 读权限出现（计数来自各 open 列表 total）。
  await expect(page.getByTestId('home-warehouse')).toContainText('待上架')
  // 主体带全量 PDA 权限 → 应用墙入口全部可见可点。
  await expect(page.getByRole('button', { name: '收货入库' })).toBeEnabled()
  await expect(page.getByRole('button', { name: '报工' })).toBeEnabled()
  await expect(page.getByRole('button', { name: '报修' })).toBeEnabled()

  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)
})

test('clicking an app-wall entry navigates to its work page', async ({ page }) => {
  await seedStoredSession(page)
  await page.goto('/')
  // 入口已点亮 → 点击直达对应作业页（以 WMS 收货入库为代表）。
  await page.getByRole('button', { name: '收货入库' }).click()
  await expect(page).toHaveURL('/wms/inbound')
})

test('home scan: type + Enter echoes in-page and keeps the operator on the workbench', async ({
  page,
}) => {
  // R3 fix: scanning must NOT navigate to the not-yet-existent /scan route; it echoes
  // the value in-page (`[data-testid="last-scan"]` → `已扫码：{value}`) so the operator
  // stays on the workbench instead of being dropped on a dead route.
  await seedStoredSession(page)
  await page.goto('/')

  const scanInput = page.locator('input[placeholder^="扫描"]')
  await scanInput.focus()
  await scanInput.type('SKU-12345')
  await scanInput.press('Enter')

  // Still on the workbench — no fake jump to /scan or any dead route.
  await expect(page).toHaveURL('/')
  // The in-page echo proves the scan was handled honestly.
  await expect(page.getByTestId('last-scan')).toContainText('已扫码：SKU-12345')
})

test('fixed four-entrance navigation adapts the workbench, tasks, scan and profile at 375x812', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await seedStoredSession(page)
  await page.goto('/')

  const tabBar = page.locator('[data-slot="tab-bar"]')
  await expect(tabBar.getByRole('button')).toHaveText(['工作台', '任务', '扫码', '我的'])

  await tabBar.getByRole('button', { name: '任务' }).click()
  await expect(page).toHaveURL('/tasks')
  await expect(page.getByText('WO-2026-00001').first()).toBeVisible()
  await expect(page.getByText('我的质检任务')).toBeVisible()

  await tabBar.getByRole('button', { name: '扫码' }).click()
  await expect(page).toHaveURL('/scan')
  const scanInput = page.locator('input[placeholder^="扫描"]')
  await scanInput.fill('WO-2026-00001')
  await scanInput.press('Enter')
  await expect(page.getByTestId('scan-result')).toContainText('WO-2026-00001')

  await tabBar.getByRole('button', { name: '我的' }).click()
  await expect(page).toHaveURL('/me')
  await expect(page.getByText('PDA 操作员')).toBeVisible()
  await expect(page.getByText('本人 · 李秀英')).toBeVisible()

  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)
})

test('profile logout clears the PDA session and returns to login', async ({ page }) => {
  await seedStoredSession(page)
  await page.goto('/me')

  await page.getByRole('button', { name: '退出登录' }).click()

  await expect(page).toHaveURL('/login')
  await expect(page.getByRole('button', { name: '登录' })).toBeVisible()
  await expect
    .poll(() => page.evaluate(() => localStorage.getItem('nerv-iip.business-pda.auth')))
    .toBeNull()
})
