import { test, expect } from '@playwright/test'
import { session } from './fixture'
import { writeFile } from 'node:fs/promises'

export function registerDowntime() {
  test.describe('停机列表长内容（#4470）', () => {
    // #4455 批准规格 r1：fixture 仅证明浏览器布局和既有交互，真实栈由父票另验。
    const deviceName = '制管一线高精度数控双端自动切割及端面加工设备'
    const deviceCode = 'EQ-TUB-CUTTING-DOUBLE-END-AUTOMATIC-20261017-001'
    const reasonName = '机械故障：主轴轴承温升异常，需要检查传动机构、润滑系统和密封组件后确认恢复'
    const rows = Array.from({ length: 12 }, (_, index) => ({
      downtimeEventId: `DT-TUB-20261017-${String(index + 1).padStart(3, '0')}`,
      workCenterId: 'WC-TUB-DOUBLE-END-CUTTING-01',
      workOrderId: `wo-${index + 1}`,
      workOrderNo: `WO-TUB-20261017-${String(index + 1).padStart(3, '0')}`,
      deviceAssetId: 'device-1',
      deviceAssetCode: deviceCode,
      deviceAssetName: deviceName,
      reasonCode: 'DT-MECH',
      reasonName,
      status: index % 2 === 0 ? 'Open' : 'Recovered',
      startedAtUtc: '2026-10-17T01:23:45Z',
      recoveredAtUtc: index % 2 === 0 ? null : '2026-10-17T02:34:56Z',
    }))
    const principal = {
      ...session.principal,
      loginName: 'mes-team-leader',
      permissionCodes: ['business.mes.downtime.read', 'business.mes.downtime.manage'],
    }

    for (const viewport of [
      { width: 1024, height: 768 },
      { width: 1280, height: 800 },
      { width: 1440, height: 900 },
    ]) {
      for (const theme of ['light', 'dark']) {
        test(`停机可读性与恢复 ${viewport.width}×${viewport.height} ${theme}`, async ({
          page,
        }, testInfo) => {
          await page.setViewportSize(viewport)
          await page.addInitScript(
            ({ principal, session, theme }) => {
              localStorage.setItem(
                'nerv-iip.business-console.auth',
                JSON.stringify({ ...session, principal }),
              )
              localStorage.setItem('nerv-iip-color-mode', theme)
            },
            { principal, session, theme },
          )
          let recoveredId = ''
          const recoveries: { id: string; body: Record<string, unknown> }[] = []
          const queries: URLSearchParams[] = []
          await page.route('**/api/console/v1/**', async (route) => {
            const path = new URL(route.request().url()).pathname
            await route.fulfill({
              json: {
                success: true,
                data: path.endsWith('/auth/me') ? principal : { ...session, principal },
              },
            })
          })
          await page.route('**/api/business-console/v1/**', async (route) => {
            const url = new URL(route.request().url())
            let data: unknown = {}
            if (url.pathname.endsWith('/me/work-context')) data = { organizationId: 'org-001' }
            if (url.pathname.endsWith('/directories/downtime-reason'))
              data = { items: [{ code: 'DT-MECH', displayName: reasonName }] }
            if (url.pathname.endsWith('/mes/downtime-events')) {
              queries.push(url.searchParams)
              const keyword = url.searchParams.get('keyword') ?? ''
              const status = url.searchParams.get('status')
              const matching = rows
                .map((row) =>
                  row.downtimeEventId === recoveredId
                    ? { ...row, status: 'Recovered', recoveredAtUtc: '2026-10-17T03:00:00Z' }
                    : row,
                )
                .filter(
                  (row) =>
                    (!status || row.status.toLowerCase() === status.toLowerCase()) &&
                    row.workOrderNo.includes(keyword),
                )
              const skip = Number(url.searchParams.get('skip') ?? 0)
              const take = Number(url.searchParams.get('take') ?? 10)
              data = {
                items: matching.slice(skip, skip + take),
                total: matching.length,
                reasonSummary: [],
              }
            }
            if (url.pathname.endsWith('/recover')) {
              recoveredId = decodeURIComponent(url.pathname.split('/').at(-2)!)
              recoveries.push({ id: recoveredId, body: route.request().postDataJSON() })
            }
            await route.fulfill({ json: { success: true, data } })
          })
          await page.goto('/mes/downtime', { waitUntil: 'domcontentloaded' })
          const table = page.locator('table.nv-dt-table')
          await expect(table.locator('tbody tr')).toHaveCount(10)
          await expect(
            page.getByRole('button', {
              name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
            }),
          ).toBeVisible()
          await expect(table).toContainText(deviceName)
          await expect(table).toContainText(deviceCode)
          await expect(table).toContainText(reasonName)
          await expect(table).toContainText('未恢复')
          await expect(table).toContainText('已恢复')
          const measure = await table.evaluate((element) => ({
            headers: Array.from(element.querySelectorAll('th')).map((cell) => ({
              text: cell.textContent?.trim(),
              width: cell.getBoundingClientRect().width,
            })),
            cells: Array.from(element.querySelectorAll('tbody tr:first-child td')).map((cell) => ({
              text: cell.textContent?.trim(),
              width: cell.getBoundingClientRect().width,
              height: cell.getBoundingClientRect().height,
            })),
            overflow: Array.from(element.querySelectorAll('tbody td')).flatMap((cell) => {
              const bounds = cell.getBoundingClientRect()
              const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
              const failures: string[] = []
              let node: Node | null
              while ((node = walker.nextNode())) {
                if (!node.textContent?.trim()) continue
                const range = document.createRange()
                range.selectNodeContents(node)
                if (
                  Array.from(range.getClientRects()).some(
                    (rect) => rect.left < bounds.left - 1 || rect.right > bounds.right + 1,
                  )
                )
                  failures.push(node.textContent)
              }
              return failures
            }),
          }))
          const measurementPath = testInfo.outputPath('dom-measurements.json')
          await writeFile(measurementPath, JSON.stringify({ viewport, theme, ...measure }, null, 2))
          await testInfo.attach('dom-measurements', {
            path: measurementPath,
            contentType: 'application/json',
          })
          await page.screenshot({ path: testInfo.outputPath('downtime.png'), fullPage: true })
          expect.soft(measure.overflow, '文字必须留在所属单元格且完整显示').toEqual([])
          expect(
            await page.evaluate(() => document.documentElement.scrollWidth),
          ).toBeLessThanOrEqual(viewport.width)
          // 显式移动表格自己的滚动容器，再操作右侧恢复入口。
          const scroll = await table.evaluate((element) => {
            const parent = element.parentElement!
            parent.scrollLeft = parent.scrollWidth
            return {
              left: parent.scrollLeft,
              width: parent.clientWidth,
              content: parent.scrollWidth,
            }
          })
          expect(scroll.content).toBeGreaterThan(scroll.width)
          expect(scroll.left).toBeGreaterThan(0)
          await writeFile(
            testInfo.outputPath('scroll-measurements.json'),
            JSON.stringify(scroll, null, 2),
          )
          const firstRow = table.locator('tbody tr').first()
          const recover = firstRow.getByRole('button', { name: '恢复', exact: true })
          await expect(recover).toBeInViewport()
          await page.screenshot({
            path: testInfo.outputPath('downtime-scrolled.png'),
            fullPage: true,
          })
          await recover.click()
          const dialog = page.getByRole('dialog')
          await expect(dialog).toContainText(rows[0]!.downtimeEventId)
          await expect(dialog).toContainText(deviceName)
          await dialog.getByRole('button', { name: '确认恢复', exact: true }).click()
          await expect(dialog).not.toBeVisible()
          expect(recoveries).toHaveLength(1)
          expect(recoveries[0]).toMatchObject({
            id: rows[0]!.downtimeEventId,
            body: { organizationId: 'org-001', environmentId: 'env-dev' },
          })
          expect(Number.isFinite(Date.parse(String(recoveries[0]!.body.recoveredAtUtc)))).toBe(true)
          await expect(firstRow).toContainText('已恢复')
          await expect(firstRow.getByRole('button', { name: '恢复', exact: true })).toHaveCount(0)
          await page.getByRole('button', { name: '下一页', exact: true }).click()
          await expect(table.locator('tbody tr')).toHaveCount(2)
          await expect(table).toContainText(rows[10]!.downtimeEventId)
          await page.getByRole('textbox', { name: '搜索停机事件' }).fill(rows[0]!.workOrderNo)
          await expect(table.locator('tbody tr')).toHaveCount(1)
          await expect(table).toContainText(rows[0]!.downtimeEventId)
          expect(queries.at(-1)!.get('skip')).toBe('0')
          expect(queries.at(-1)!.get('keyword')).toBe(rows[0]!.workOrderNo)
          await page.getByRole('textbox', { name: '搜索停机事件' }).fill('')
          await expect(table.locator('tbody tr')).toHaveCount(10)
          await page.getByRole('combobox', { name: '停机状态', exact: true }).click()
          await page.getByRole('option', { name: '未恢复', exact: true }).click()
          await expect(table.locator('tbody tr')).toHaveCount(5)
          expect(queries.at(-1)!.get('status')).toBe('Open')
          await expect(table.locator('tbody')).not.toContainText('已恢复')
          await page.getByRole('combobox', { name: '按停机原因筛选', exact: true }).click()
          await page.getByRole('option', { name: reasonName, exact: true }).click()
          await expect.poll(() => queries.at(-1)!.get('reasonCode')).toBe('DT-MECH')
          await expect(table.locator('tbody tr')).toHaveCount(5)
        })
      }
    }
  })
}
