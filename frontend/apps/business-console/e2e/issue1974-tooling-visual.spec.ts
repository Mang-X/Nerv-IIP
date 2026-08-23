import { expect, test, type Route } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'

const STORAGE_KEY = 'nerv-iip.business-console.auth'
const SCREENSHOT_DIR = path.resolve(
  process.cwd(),
  '../../DESIGN/roadmaps/assets/2026-08-23-issue-1974-tooling-console',
)

const principal = {
  principalId: 'tooling-maintainer-1',
  principalType: 'User',
  loginName: 'tooling.maintainer',
  email: 'tooling.maintainer@nerv-iip.local',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: ['business.masterdata.resources.read', 'business.masterdata.resources.manage'],
}

const session = {
  accessToken: 'visual-evidence-access-token',
  refreshToken: 'visual-evidence-refresh-token',
  sessionId: 'visual-evidence-session',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

test.use({ viewport: { width: 1440, height: 900 } })

test.beforeEach(async ({ page }) => {
  await mkdir(SCREENSHOT_DIR, { recursive: true })
  await page.addInitScript(
    ({ key, storedSession }) => localStorage.setItem(key, JSON.stringify(storedSession)),
    {
      key: STORAGE_KEY,
      storedSession: {
        principal,
        refreshToken: session.refreshToken,
        sessionId: session.sessionId,
      },
    },
  )
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

test('工装维护台真实浏览器视觉核验', async ({ page }) => {
  test.skip(test.info().project.name !== 'desktop', '业务维护台只取 desktop 证据')

  await page.goto('/master-data/tooling', { waitUntil: 'domcontentloaded' })
  await page.addStyleTag({
    content:
      '*, *::before, *::after { animation-duration: 0s !important; transition-duration: 0s !important; }',
  })
  await expect(page.getByText('MOULD-FLOOR-OP10', { exact: true })).toBeVisible()
  await expect(page.getByText('即将达寿命', { exact: true })).toBeVisible()
  await expect(
    page
      .getByRole('row')
      .filter({ hasText: 'FIXTURE-WELD-07' })
      .getByText('不可参与排程', { exact: true }),
  ).toBeVisible()
  await page.screenshot({
    path: path.join(SCREENSHOT_DIR, '01-tooling-workbench.png'),
  })

  await page.getByRole('button', { name: 'MOULD-FLOOR-OP10', exact: true }).click()
  const detail = page.locator('[data-slot="nv-sheet-content"]')
  await expect(detail).toContainText('WC-PRESS-01')
  await expect(detail).toContainText('SKU-FLOOR-ASSY')
  await page.screenshot({
    path: path.join(SCREENSHOT_DIR, '02-tooling-detail.png'),
  })
  await page.keyboard.press('Escape')
  await expect(detail).toBeHidden()

  await page.getByRole('button', { name: '注册工装' }).click()
  const nameInput = page.getByLabel('工装名称 *')
  const lifeInput = page.getByLabel('保养使用寿命（次）')
  const nameFrame = nameInput.locator('..')
  const lifeFrame = lifeInput.locator('..')
  const nameLabel = page.locator('label[for="tooling-name"] > span')
  const firstCandidate = page.getByRole('checkbox').first().locator('..')
  const beforeBorder = await nameFrame.evaluate((element) => getComputedStyle(element).borderColor)
  const beforeLabel = await nameLabel.evaluate((element) => getComputedStyle(element).color)
  const beforeCandidate = await firstCandidate.evaluate(
    (element) => getComputedStyle(element).color,
  )
  const beforeLifeBorder = await lifeFrame.evaluate(
    (element) => getComputedStyle(element).borderColor,
  )
  await lifeInput.fill('0')
  await page.getByRole('button', { name: '确认注册' }).click()
  await expect(page.getByText('请填写工装名称。', { exact: true })).toBeVisible()
  await expect(page.getByText('使用寿命必须是正整数。', { exact: true })).toBeVisible()
  await expect(nameFrame).toHaveAttribute('data-invalid', 'true')
  await expect(lifeFrame).toHaveAttribute('data-invalid', 'true')
  await expect
    .poll(() => nameFrame.evaluate((element) => getComputedStyle(element).borderColor))
    .not.toBe(beforeBorder)
  await expect
    .poll(() => nameLabel.evaluate((element) => getComputedStyle(element).color))
    .not.toBe(beforeLabel)
  await expect
    .poll(() => firstCandidate.evaluate((element) => getComputedStyle(element).color))
    .toBe(beforeCandidate)
  await expect
    .poll(() => lifeFrame.evaluate((element) => getComputedStyle(element).borderColor))
    .not.toBe(beforeLifeBorder)
  await page.screenshot({
    path: path.join(SCREENSHOT_DIR, '03-register-validation.png'),
  })
  await page.keyboard.press('Escape')

  await page.getByRole('button', { name: '工装操作 MOULD-FLOOR-OP10' }).click()
  await page.getByRole('menuitem', { name: '转保养' }).click()
  const statusDialog = page.getByRole('dialog')
  const statusReason = statusDialog.getByLabel('原因 *')
  const statusFrame = statusReason.locator('..')
  const statusBorderBefore = await statusFrame.evaluate(
    (element) => getComputedStyle(element).borderColor,
  )
  await statusDialog.getByRole('button', { name: '确认转保养' }).click()
  await expect(statusFrame).toHaveAttribute('data-invalid', 'true')
  await expect
    .poll(() => statusFrame.evaluate((element) => getComputedStyle(element).borderColor))
    .not.toBe(statusBorderBefore)
  await statusDialog.getByRole('button', { name: '取消' }).click()

  await page.getByRole('button', { name: '工装操作 FIXTURE-WELD-07' }).click()
  await page.getByRole('menuitem', { name: '完成保养' }).click()
  const completionDialog = page.getByRole('dialog')
  await expect(completionDialog).toContainText('请说明本次状态变更原因。')
  await expect(completionDialog).not.toContainText('完成保养后将清零累计使用次数')
  await completionDialog.getByRole('button', { name: '取消' }).click()

  await page.getByRole('button', { name: '登记使用' }).first().click()
  const usageDialog = page.getByRole('dialog')
  const usageInput = usageDialog.getByLabel('本次使用次数 *')
  const usageFrame = usageInput.locator('..')
  const usageBorderBefore = await usageFrame.evaluate(
    (element) => getComputedStyle(element).borderColor,
  )
  await usageInput.fill('0')
  await usageDialog.getByRole('button', { name: '确认登记' }).click()
  await expect(usageFrame).toHaveAttribute('data-invalid', 'true')
  await expect
    .poll(() => usageFrame.evaluate((element) => getComputedStyle(element).borderColor))
    .not.toBe(usageBorderBefore)
  await usageDialog.getByRole('button', { name: '取消' }).click()

  await page.getByRole('button', { name: '工装操作 MOULD-FLOOR-OP10' }).click()
  await page.getByRole('menuitem', { name: '退役' }).click()
  await expect(page.getByRole('menu')).toBeHidden()
  const retireDialog = page.getByRole('alertdialog')
  await expect(retireDialog).toContainText('退役为终态，工装将永久退出排程。')
  const confirmRetire = retireDialog.getByRole('button', { name: '确认退役' })
  await expect(confirmRetire).toBeDisabled()
  await retireDialog.getByLabel('原因 *').fill('模具达到报废年限，经设备主管确认')
  await expect(confirmRetire).toBeEnabled()
  await expect(confirmRetire).toHaveClass(/bg-destructive/)
  await page.screenshot({
    path: path.join(SCREENSHOT_DIR, '04-retire-confirmation.png'),
  })
})

async function routeConsoleApi(route: Route) {
  const pathname = new URL(route.request().url()).pathname
  if (pathname === '/api/console/v1/auth/refresh') return fulfillJson(route, envelope(session))
  if (pathname === '/api/console/v1/auth/me') return fulfillJson(route, envelope(principal))
  return route.fallback()
}

async function routeBusinessConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  if (url.pathname === '/api/business-console/v1/master-data/tooling-assets') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            code: 'MOULD-FLOOR-OP10',
            name: '前地板拉延模',
            toolingType: 'mould',
            status: 'available',
            maintenanceLifeCount: 80000,
            usageCount: 79200,
            isSchedulable: true,
            workCenterCodes: ['WC-PRESS-01'],
            skuCodes: ['SKU-FLOOR-ASSY', 'SKU-SILL-LH'],
          },
          {
            code: 'FIXTURE-WELD-07',
            name: '左门槛总成焊接夹具',
            toolingType: 'fixture',
            status: 'maintenance',
            maintenanceLifeCount: 15000,
            usageCount: 12640,
            isSchedulable: false,
            workCenterCodes: ['WC-WELD-02'],
            skuCodes: ['SKU-SILL-LH'],
          },
          {
            code: 'GAUGE-DOOR-03',
            name: '左前门内板检具',
            toolingType: 'gauge',
            status: 'retired',
            maintenanceLifeCount: 30000,
            usageCount: 30120,
            isSchedulable: false,
            workCenterCodes: ['WC-QA-01'],
            skuCodes: ['SKU-DOOR-INNER-LH'],
          },
        ],
        total: 3,
      }),
    )
  }

  if (url.pathname === '/api/business-console/v1/master-data/resources') {
    const resourceType = url.searchParams.get('resourceType')
    const resources =
      resourceType === 'work-center'
        ? [
            {
              resourceType,
              code: 'WC-PRESS-01',
              displayName: '冲压一线压力机工作中心',
              active: true,
              snapshotVersion: 'v12',
            },
            {
              resourceType,
              code: 'WC-WELD-02',
              displayName: '车身二线焊装工作中心',
              active: true,
              snapshotVersion: 'v9',
            },
            {
              resourceType,
              code: 'WC-QA-01',
              displayName: '冲压件终检工作中心',
              active: true,
              snapshotVersion: 'v5',
            },
          ]
        : [
            {
              resourceType,
              code: 'SKU-FLOOR-ASSY',
              displayName: '前地板总成',
              active: true,
              snapshotVersion: 'v18',
            },
            {
              resourceType,
              code: 'SKU-SILL-LH',
              displayName: '左门槛加强板',
              active: true,
              snapshotVersion: 'v14',
            },
            {
              resourceType,
              code: 'SKU-DOOR-INNER-LH',
              displayName: '左前门内板',
              active: true,
              snapshotVersion: 'v7',
            },
          ]
    return fulfillJson(
      route,
      envelope({ resources, total: resourceType === 'work-center' ? 203 : 318 }),
    )
  }

  return fulfillJson(route, envelope({ items: [], total: 0 }))
}

function envelope<T>(data: T) {
  return { success: true, data, traceId: 'issue-1974-visual-evidence' }
}

function fulfillJson(route: Route, json: unknown) {
  return route.fulfill({ status: 200, contentType: 'application/json', json })
}
