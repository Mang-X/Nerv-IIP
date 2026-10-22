import { expect, test, type Locator, type Route } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'

import { requireBrowserEvidenceOutputDir } from '../playwright.config'

const STORAGE_KEY = 'nerv-iip.business-console.auth'
const toolingRegistrations: Record<string, unknown>[] = []

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
  toolingRegistrations.length = 0
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
  const screenshotDir = requireBrowserEvidenceOutputDir()
  await mkdir(screenshotDir, { recursive: true })

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
    path: path.join(screenshotDir, '01-tooling-workbench.png'),
  })

  await page.getByRole('button', { name: 'MOULD-FLOOR-OP10', exact: true }).click()
  const detail = page.locator('[data-slot="nv-sheet-content"]')
  await expect(detail).toContainText('WC-PRESS-01')
  await expect(detail).toContainText('SKU-FLOOR-ASSY')
  await page.screenshot({
    path: path.join(screenshotDir, '02-tooling-detail.png'),
  })
  await page.keyboard.press('Escape')
  await expect(detail).toBeHidden()

  await page.getByRole('button', { name: '注册工装' }).click()
  await page.getByRole('button', { name: '适用工作中心' }).click()
  const workCenterSearch = page.getByLabel('搜索适用工作中心')
  const delayedRequest = page.waitForRequest(
    (request) => new URL(request.url()).searchParams.get('keyword') === '冲压',
  )
  await workCenterSearch.fill('冲压')
  await delayedRequest
  await workCenterSearch.fill('精加工')
  const lateWorkCenter = page.getByRole('option').filter({ hasText: 'WC-MACHINING-201' })
  await expect(lateWorkCenter).toContainText('精加工工作中心')
  await page.waitForTimeout(900)
  await expect(lateWorkCenter).toContainText('精加工工作中心')
  await lateWorkCenter.click()
  await expect(page.getByText('精加工工作中心', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: '适用工作中心' }).click()
  await page.getByLabel('搜索适用工作中心').fill('冲压')
  await page.getByRole('option').filter({ hasText: 'WC-PRESS-01' }).click()
  await page.getByRole('button', { name: '移除 冲压一线压力机工作中心' }).click()

  await page.getByRole('button', { name: '适用 SKU' }).click()
  await page.getByLabel('搜索适用 SKU').fill('门槛')
  await page.getByRole('option').filter({ hasText: 'SKU-SILL-LH' }).click()
  await page.getByRole('button', { name: '适用 SKU' }).click()
  await page.getByLabel('搜索适用 SKU').fill('前地板')
  await page.getByRole('option').filter({ hasText: 'SKU-FLOOR-ASSY' }).click()

  await page.getByLabel('工装名称 *').fill('前地板拉延模')
  await page.getByLabel('工装类型 *').click()
  await page.getByRole('option', { name: '模具' }).click()
  await page.getByRole('button', { name: '确认注册' }).click()
  await expect.poll(() => toolingRegistrations.length).toBe(1)
  expect(toolingRegistrations[0]).toMatchObject({
    name: '前地板拉延模',
    toolingType: 'mould',
    workCenterCodes: ['WC-MACHINING-201'],
    skuCodes: ['SKU-SILL-LH', 'SKU-FLOOR-ASSY'],
  })

  await page.getByRole('button', { name: '注册工装' }).click()
  const nameInput = page.getByLabel('工装名称 *')
  const typeTrigger = page.getByLabel('工装类型 *')
  const lifeInput = page.getByLabel('保养使用寿命（次）')
  const nameFrame = nameInput.locator('..')
  const lifeFrame = lifeInput.locator('..')
  const nameLabel = page.locator('label[for="tooling-name"] > span')
  const firstCandidate = page.getByRole('button', { name: '适用工作中心' }).locator('..')
  const beforeBorder = await nameFrame.evaluate((element) => getComputedStyle(element).borderColor)
  const beforeLabel = await nameLabel.evaluate((element) => getComputedStyle(element).color)
  const beforeCandidate = await firstCandidate.evaluate(
    (element) => getComputedStyle(element).color,
  )
  const beforeLifeBorder = await lifeFrame.evaluate(
    (element) => getComputedStyle(element).borderColor,
  )
  const beforeTypeBorder = await typeTrigger.evaluate(
    (element) => getComputedStyle(element).borderColor,
  )
  await expect(typeTrigger).toContainText('请选择工装类型')
  await lifeInput.fill('0')
  await page.getByRole('button', { name: '确认注册' }).click()
  await expect(page.getByText('请填写工装名称。', { exact: true })).toBeVisible()
  await expect(page.getByText('请选择工装类型。', { exact: true })).toBeVisible()
  await expect(page.getByText('使用寿命必须是正整数。', { exact: true })).toBeVisible()
  await expect(nameFrame).toHaveAttribute('data-invalid', 'true')
  await expect(lifeFrame).toHaveAttribute('data-invalid', 'true')
  await expect(typeTrigger).toHaveAttribute('data-invalid', 'true')
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
  await expect
    .poll(() => typeTrigger.evaluate((element) => getComputedStyle(element).borderColor))
    .not.toBe(beforeTypeBorder)
  await page.screenshot({
    path: path.join(screenshotDir, '03-register-validation.png'),
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
  await expect(completionDialog).not.toContainText(
    '完成保养后将清零累计使用次数，并恢复为可用状态。',
  )
  await completionDialog.getByRole('button', { name: '取消' }).click()

  await page.getByRole('button', { name: '工装操作 GAUGE-DOOR-04' }).click()
  await page.getByRole('menuitem', { name: '完成保养' }).click()
  await expect(completionDialog).toContainText('完成保养后将清零累计使用次数，并恢复为可用状态。')
  await expect(completionDialog).toContainText('请说明本次状态变更原因。')
  await page.screenshot({
    path: path.join(screenshotDir, '05-maintenance-completion-disclosure.png'),
  })
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
    path: path.join(screenshotDir, '04-retire-confirmation.png'),
  })
})

test('NvInput 与 NvSelectTrigger 在真实浏览器中呈现可区分的边框状态', async ({ page }) => {
  test.skip(test.info().project.name !== 'desktop', '桌面 NvUI 状态只取 desktop 证据')

  await page.goto('/master-data/tooling', { waitUntil: 'domcontentloaded' })
  await page.addStyleTag({
    content:
      '*, *::before, *::after { animation-duration: 0s !important; transition-duration: 0s !important; }',
  })
  await expect(page.getByText('MOULD-FLOOR-OP10', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '注册工装' }).click()

  const nameInput = page.getByLabel('工装名称 *')
  const nameFrame = nameInput.locator('..')
  const typeTrigger = page.getByLabel('工装类型 *')

  const inputDefaultBorder = await borderColor(nameFrame)
  await nameFrame.hover()
  await expect.poll(() => borderColor(nameFrame)).not.toBe(inputDefaultBorder)
  const inputHoverBorder = await borderColor(nameFrame)
  await nameInput.focus()
  await page.mouse.move(0, 0)
  const inputFocusBorder = await borderColor(nameFrame)
  expect(inputFocusBorder).not.toBe(inputDefaultBorder)
  expect(inputFocusBorder).not.toBe(inputHoverBorder)

  const selectDefaultBorder = await borderColor(typeTrigger)
  await typeTrigger.hover()
  await expect.poll(() => borderColor(typeTrigger)).not.toBe(selectDefaultBorder)
  const selectHoverBorder = await borderColor(typeTrigger)
  await nameInput.focus()
  await page.keyboard.press('Tab')
  await expect(typeTrigger).toBeFocused()
  await page.mouse.move(0, 0)
  const selectFocusBorder = await borderColor(typeTrigger)
  expect(selectFocusBorder).not.toBe(selectDefaultBorder)
  expect(selectFocusBorder).not.toBe(selectHoverBorder)
  expect(await boxShadow(typeTrigger)).not.toBe('none')

  await page.getByRole('button', { name: '确认注册' }).click()
  await expect(nameFrame).toHaveAttribute('data-invalid', 'true')
  await expect(typeTrigger).toHaveAttribute('data-invalid', 'true')
  await page.mouse.move(0, 0)

  const inputInvalidBorder = await borderColor(nameFrame)
  expect(inputInvalidBorder).not.toBe(inputDefaultBorder)
  expect(inputInvalidBorder).not.toBe(inputHoverBorder)
  expect(inputInvalidBorder).not.toBe(inputFocusBorder)
  await nameInput.focus()
  await expect.poll(() => borderColor(nameFrame)).toBe(inputInvalidBorder)
  await expect.poll(() => boxShadow(nameFrame)).not.toBe('none')

  const selectInvalidBorder = await borderColor(typeTrigger)
  expect(selectInvalidBorder).not.toBe(selectDefaultBorder)
  expect(selectInvalidBorder).not.toBe(selectHoverBorder)
  expect(selectInvalidBorder).not.toBe(selectFocusBorder)
  await page.keyboard.press('Tab')
  await expect(typeTrigger).toBeFocused()
  await expect.poll(() => borderColor(typeTrigger)).toBe(selectInvalidBorder)
  await expect.poll(() => boxShadow(typeTrigger)).not.toBe('none')
})

function borderColor(locator: Locator) {
  return locator.evaluate((element) => getComputedStyle(element).borderColor)
}

function boxShadow(locator: Locator) {
  return locator.evaluate((element) => getComputedStyle(element).boxShadow)
}

async function routeConsoleApi(route: Route) {
  const pathname = new URL(route.request().url()).pathname
  if (pathname === '/api/console/v1/auth/refresh') return fulfillJson(route, envelope(session))
  if (pathname === '/api/console/v1/auth/me') return fulfillJson(route, envelope(principal))
  return route.fallback()
}

async function routeBusinessConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  if (url.pathname === '/api/business-console/v1/master-data/tooling-assets') {
    if (route.request().method() === 'POST') {
      toolingRegistrations.push(route.request().postDataJSON() as Record<string, unknown>)
      return fulfillJson(route, envelope({ code: 'MOULD-AUTO-001' }))
    }
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
          {
            code: 'GAUGE-DOOR-04',
            name: '右前门内板检具',
            toolingType: 'gauge',
            status: 'maintenance',
            maintenanceLifeCount: 30000,
            usageCount: 30000,
            isSchedulable: false,
            workCenterCodes: ['WC-QA-01'],
            skuCodes: ['SKU-DOOR-INNER-LH'],
          },
        ],
        total: 4,
      }),
    )
  }

  if (url.pathname === '/api/business-console/v1/master-data/resources') {
    const resourceType = url.searchParams.get('resourceType')
    const keyword = url.searchParams.get('keyword')
    if (resourceType === 'work-center' && keyword === '冲压') {
      await new Promise((resolve) => setTimeout(resolve, 800))
    }
    const resources =
      resourceType === 'work-center'
        ? keyword === '精加工'
          ? [
              {
                resourceType,
                code: 'WC-MACHINING-201',
                displayName: '精加工工作中心',
                active: true,
                snapshotVersion: 'v3',
              },
            ]
          : [
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
        : keyword === '门槛'
          ? [
              {
                resourceType,
                code: 'SKU-SILL-LH',
                displayName: '左门槛加强板',
                active: true,
                snapshotVersion: 'v14',
              },
            ]
          : keyword === '前地板'
            ? [
                {
                  resourceType,
                  code: 'SKU-FLOOR-ASSY',
                  displayName: '前地板总成',
                  active: true,
                  snapshotVersion: 'v18',
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
      envelope({
        resources,
        total: keyword ? resources.length : resourceType === 'work-center' ? 203 : 318,
      }),
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
