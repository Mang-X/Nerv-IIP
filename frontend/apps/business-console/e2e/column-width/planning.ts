import { test, expect } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

export function registerPlanning() {
  test.describe('MPS 物料与工厂可读性（#4471）', () => {
    // Regression / #4471、父 #4455 r1：路由 fixture 仅证明浏览器布局与交互，不证明真实栈。
    const skuCode = 'FG-PRECISION-HYDRAULIC-SHOCK-ABSORBER-20261017-001'
    const skuName = '高精度液压减振器总成（重载车辆加强型长行程）'
    const siteCode = 'SITE-NINGBO-PRECISION-MANUFACTURING-20261017-01'
    const siteName = '宁波精密制造第一工厂液压减振器总装生产基地'
    const principal = {
      principalId: 'planner-1',
      principalType: 'User',
      loginName: 'planner',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionVersion: 1,
      permissionCodes: [
        'business.planning.demands.read',
        'business.planning.mps.read',
        'business.planning.mps.manage',
        'business.planning.mps.release',
        'business.masterdata.products.read',
        'business.masterdata.resources.read',
        'business.planning.mrp.read',
        'business.planning.suggestions.manage',
      ],
    }
    const session = {
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      sessionId: 'session-1',
      expiresAtUtc: '2099-01-01T00:00:00.000Z',
      principal,
    }

    for (const viewport of [
      { width: 1024, height: 768 },
      { width: 1280, height: 800 },
      { width: 1440, height: 900 },
    ]) {
      for (const theme of ['light', 'dark']) {
        test(`MPS 长物料与工厂 ${viewport.width}×${viewport.height} ${theme}`, async ({
          page,
        }, testInfo) => {
          await page.setViewportSize(viewport)
          await page.addInitScript(
            ({ session, theme }) => {
              localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(session))
              localStorage.setItem('nerv-iip-color-mode', theme)
            },
            { session, theme },
          )
          let status = 'Draft'
          const actions: string[] = []
          await page.route('**/api/console/v1/**', async (route) => {
            const path = new URL(route.request().url()).pathname
            await route.fulfill({
              json: { success: true, data: path.endsWith('/refresh') ? session : principal },
            })
          })
          await page.route('**/api/business-console/v1/**', async (route) => {
            const url = new URL(route.request().url())
            let data: unknown = { items: [], total: 0 }
            if (url.pathname.endsWith('/me/work-context'))
              data = { organizationId: 'org-001', environmentId: 'env-dev' }
            else if (url.pathname.endsWith('/master-data/skus'))
              data = { resources: [{ code: skuCode, displayName: skuName }], total: 1 }
            else if (url.pathname.endsWith('/master-data/resources')) {
              const type = url.searchParams.get('resourceType')
              data = {
                resources:
                  type === 'site'
                    ? [{ code: siteCode, displayName: siteName }]
                    : type === 'unit-of-measure'
                      ? [{ code: 'pcs', displayName: '件' }]
                      : [],
                total: 1,
              }
            } else if (url.pathname.endsWith('/planning/mps')) {
              data = {
                items: Array.from({ length: 11 }, (_, index) => ({
                  mpsId: `mps-${index + 1}`,
                  skuCode,
                  siteCode,
                  uomCode: 'pcs',
                  bucketDate: index === 10 ? '2026-10-28' : '2026-10-17',
                  quantity: 123456.75,
                  status: index === 0 ? status : 'Released',
                })),
                total: 11,
              }
            } else if (url.pathname.endsWith('/mps-1/review')) {
              status = 'Reviewed'
              actions.push('review')
            } else if (url.pathname.endsWith('/mps-1/release')) {
              status = 'Released'
              actions.push('release')
            }
            await route.fulfill({ json: { success: true, data } })
          })
          await page.goto('/planning', { waitUntil: 'domcontentloaded' })
          await page.getByRole('tab', { name: /MPS 主计划/ }).click()
          const table = page.locator('table.nv-dt-table')
          const row = table.locator('tbody tr').first()
          await expect(row).toContainText(skuName)
          await expect(row).toContainText(siteName)
          await expect(row).toContainText('123456.75 件')
          await expect(row).toContainText('2026-10-17')
          await expect(row).toContainText('草稿')
          await expect(
            page.getByRole('button', {
              name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
            }),
          ).toBeVisible()
          await page.screenshot({ path: testInfo.outputPath('mps-left.png'), fullPage: true })
          const measurement = await table.evaluate((table) => {
            const failures: string[] = []
            const widths = Array.from(table.querySelectorAll('thead th')).map((th) => ({
              header: th.textContent?.trim(),
              width: th.getBoundingClientRect().width,
            }))
            for (const cell of table.querySelectorAll('tbody td')) {
              const bounds = cell.getBoundingClientRect()
              const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
              while (walker.nextNode()) {
                const node = walker.currentNode
                if (!node.textContent?.trim()) continue
                const range = document.createRange()
                range.selectNodeContents(node)
                for (const rect of range.getClientRects()) {
                  if (rect.left < bounds.left - 1 || rect.right > bounds.right + 1)
                    failures.push(node.textContent)
                }
              }
            }
            return { widths, failures, documentWidth: document.documentElement.scrollWidth }
          })
          const measurementPath = testInfo.outputPath('mps-dom-measurement.json')
          await writeFile(
            measurementPath,
            JSON.stringify({ viewport, theme, ...measurement }, null, 2),
          )
          await testInfo.attach('mps-dom-measurement', {
            path: measurementPath,
            contentType: 'application/json',
          })
          expect(measurement.failures, '文字不得跨出所属列').toEqual([])
          expect(measurement.documentWidth).toBeLessThanOrEqual(viewport.width)
          await expect(row).toContainText(skuCode)
          await expect(row).toContainText(siteCode)
          const edit = row.getByRole('button', { name: `编辑主计划行 ${skuCode}` })
          await edit.scrollIntoViewIfNeeded()
          const scroll = await edit.evaluate((button) => {
            const container = button.closest('[data-slot="table-container"]')!
            const bounds = container.getBoundingClientRect()
            const action = button.getBoundingClientRect()
            return {
              clientWidth: container.clientWidth,
              scrollWidth: container.scrollWidth,
              scrollLeft: container.scrollLeft,
              actionContained: action.left >= bounds.left && action.right <= bounds.right,
            }
          })
          await writeFile(testInfo.outputPath('mps-scroll.json'), JSON.stringify(scroll, null, 2))
          if (scroll.scrollWidth > scroll.clientWidth) expect(scroll.scrollLeft).toBeGreaterThan(0)
          expect(scroll.actionContained, '滚动后编辑操作必须在表格可视区域内').toBe(true)
          await page.screenshot({ path: testInfo.outputPath('mps-actions.png'), fullPage: true })
          await edit.click()
          const dialog = page.getByRole('dialog')
          await expect(dialog.getByRole('heading', { name: '编辑主计划行' })).toBeVisible()
          await expect(dialog.locator('#mps-qty')).toHaveValue('123456.75')
          await dialog.getByRole('button', { name: '取消', exact: true }).click()
          await row.getByRole('button', { name: '评审', exact: true }).click()
          await expect(row).toContainText('已评审')
          await row.getByRole('button', { name: '发布', exact: true }).click()
          await expect(row).toContainText('已发布')
          expect(actions).toEqual(['review', 'release'])
          await expect(row.getByRole('button', { name: `编辑主计划行 ${skuCode}` })).toHaveCount(0)
          await page.getByRole('button', { name: '下一页', exact: true }).click()
          await expect(table.locator('tbody tr')).toHaveCount(1)
          await expect(table).toContainText('2026-10-28')
          await page.getByRole('button', { name: '上一页', exact: true }).click()
          await expect(table.locator('tbody tr')).toHaveCount(10)
        })
      }
    }
  })
}
