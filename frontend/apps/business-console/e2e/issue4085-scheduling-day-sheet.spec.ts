import { readFile } from 'node:fs/promises'
import { expect, test } from '@playwright/test'

test.use({ timezoneId: 'Asia/Shanghai' })

// #4085: production page + HTTP fixture proves print rendering/download consumption, not live backend/physical paper.
test('当前页按本地日与工作中心打印，完整方案 CSV 不受日筛选影响', async ({ page }) => {
  const principal = {
    principalId: 'planner-1',
    principalType: 'User',
    loginName: 'planner',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: ['business.scheduling.plans.read'],
  }
  const session = {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    sessionId: 'session-1',
    expiresAtUtc: '2099-01-01T00:00:00Z',
    principal,
  }
  const planId = 'APS-260930-001'
  const csv =
    '\uFEFFPlanId,OrderReference\r\nAPS-260930-001,WO-260929-001\r\nAPS-260930-001,WO-260930-002\r\n'
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route('**/api/console/v1/**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: new URL(route.request().url()).pathname.endsWith('/me') ? principal : session,
      },
    }),
  )
  let csvUrl = ''
  await page.route('**/api/business-console/v1/**', async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname.endsWith('/scheduling/plans/history'))
      return route.fulfill({
        json: {
          success: true,
          data: { total: 1, items: [{ planId, status: 'released', assignmentCount: 2 }] },
        },
      })
    if (url.pathname.endsWith(`/scheduling/plans/${planId}/csv`)) {
      csvUrl = url.toString()
      return route.fulfill({
        body: csv,
        contentType: 'text/csv; charset=utf-8',
        headers: { 'content-disposition': 'attachment; filename=schedule.csv' },
      })
    }
    if (url.pathname.endsWith(`/scheduling/plans/${planId}`))
      return route.fulfill({
        json: {
          success: true,
          data: {
            planId,
            status: 'released',
            assignments: [
              {
                assignmentId: 'A-10',
                orderId: 'WO-260929-001',
                operationId: '粗车',
                operationSequence: 10,
                resourceId: 'CNC-01',
                workCenterId: 'WC-TURN',
                startUtc: '2026-09-29T15:00:00Z',
                endUtc: '2026-09-29T18:00:00Z',
              },
              {
                assignmentId: 'A-20',
                orderId: 'WO-260930-002',
                operationId: '装配',
                operationSequence: 20,
                resourceId: 'LINE-01',
                workCenterId: 'WC-ASSEMBLY',
                startUtc: '2026-09-30T00:00:00Z',
                endUtc: '2026-09-30T02:00:00Z',
              },
            ],
          },
        },
      })
    return route.fulfill({ json: { success: true, data: { items: [], total: 0 } } })
  })
  await page.goto('/scheduling')
  await page.getByRole('tab', { name: '甘特' }).click()
  await page.getByRole('combobox', { name: '排程方案' }).click()
  await page.getByRole('option', { name: `${planId} · 已发布` }).click()
  const sheet = page.getByRole('region', { name: '车间日排程单' })
  await sheet.getByLabel('排程日期').fill('2026-09-30')
  await sheet.getByRole('combobox', { name: '工作中心', exact: true }).click()
  await page.getByRole('option', { name: 'WC-TURN', exact: true }).click()
  await expect(sheet).toContainText('WO-260929-001')
  await expect(sheet).not.toContainText('WO-260930-002')
  await page.screenshot({ path: test.info().outputPath('day-sheet-screen.png'), fullPage: true })
  const downloadEvent = page.waitForEvent('download')
  await page.getByRole('button', { name: '下载方案 CSV' }).click()
  const download = await downloadEvent
  expect(await readFile((await download.path())!)).toEqual(Buffer.from(csv))
  expect(csvUrl).toContain('organizationId=org-001&environmentId=env-dev')
  expect(download.suggestedFilename()).toBe(`schedule-${planId}.csv`)
  await page.evaluate(() => {
    window.print = () => {
      document.body.dataset.printRequested = 'true'
    }
  })
  await sheet.getByRole('button', { name: '打印日排程单' }).click()
  await expect(page.locator('body')).toHaveAttribute('data-print-requested', 'true')
  await page.emulateMedia({ media: 'print' })
  const printSheet = page.locator('.scheduling-day-print')
  await expect(printSheet).toBeVisible()
  await expect(page.getByRole('heading', { name: '排产工作台', exact: true })).not.toBeVisible()
  await expect(printSheet).toContainText('已发布')
  await expect(printSheet).toContainText('2026-09-29 23:00 至 2026-09-30 02:00')
  await expect(printSheet).not.toContainText('WO-260930-002')
  await page.pdf({ path: test.info().outputPath('day-sheet.pdf'), preferCSSPageSize: true })
  await page.screenshot({ path: test.info().outputPath('day-sheet-print.png'), fullPage: true })
})
