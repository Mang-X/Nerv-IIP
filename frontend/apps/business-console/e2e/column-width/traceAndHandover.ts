import { test, expect } from '@playwright/test'
import { principal, session, STORAGE_KEY } from './fixture'
import { writeFile } from 'node:fs/promises'

export function registerTraceAndHandover() {
  test.describe('追溯与交接长内容（#4469 fixture）', () => {
    const workOrder = 'WO-TUBE-20261017-NINGBO-LINE-01-000128'
    const reportNo = 'RPT-TUBE-20261017-NINGBO-NIGHT-SHIFT-000128'
    const teamName = '精密制管一车间夜班设备质量联合保障班组'
    const shiftName = '夜班（跨日连续生产二十点至次日八点）'
    const handover = {
      handoverId: 'SH-TUBE-20261017-NINGBO-LINE-01-000128',
      shiftId: 'NIGHT',
      teamId: 'TEAM-TUBE-01',
      teamName,
      outgoingUserId: 'outgoing-1',
      outgoingUserName: '欧阳海生（制管线交班负责人）',
      incomingUserId: 'incoming-1',
      incomingUserName: '司徒晓明（制管线接班负责人）',
      handoverStatus: 'Open',
      createdAtUtc: '2026-10-17T12:31:59Z',
      wipItemCount: 12,
      unfinishedWorkOrderCount: 23,
      openIssueDetailCount: 34,
      openIssueCount: 34,
    }
    const graph = {
      nodes: [
        {
          nodeId: workOrder,
          nodeType: 'WorkOrder',
          displayName: '精密无缝钢管总成生产工单（宁波一号工厂）',
          status: 'released',
        },
        {
          nodeId: reportNo,
          nodeType: 'ProductionReport',
          displayName: '精密无缝钢管总成夜班末道检验报工记录',
          status: 'Completed',
        },
      ],
      edges: [],
    }

    test.beforeEach(async ({ page }) => {
      const reader = {
        ...principal,
        permissionCodes: [
          'business.mes.traceability.read',
          'business.mes.work-orders.read',
          'business.mes.reporting.read',
          'business.mes.receipts.read',
          'business.mes.handovers.read',
          'business.mes.handovers.manage',
        ],
      }
      const stored = { ...session, principal: reader }
      await page.addInitScript(
        (value) => localStorage.setItem(STORAGE_KEY, JSON.stringify(value)),
        stored,
      )
      await page.route('**/api/console/v1/**', (route) =>
        route.fulfill({
          json: {
            success: true,
            data: route.request().url().endsWith('/auth/me') ? reader : stored,
          },
        }),
      )
      await page.route('**/api/business-console/v1/**', async (route) => {
        const url = new URL(route.request().url())
        const path = url.pathname
        let data: unknown = { items: [], total: 0, resources: [] }
        if (path.endsWith('/me/work-context'))
          data = {
            organizationId: 'org-001',
            environmentId: 'env-dev',
            resolutionStatus: 'resolved',
            authorizedScopes: [{ kind: 'workCenter', id: 'WC-TUBE-01', displayName: '制管一线' }],
            selectedScope: { kind: 'workCenter', id: 'WC-TUBE-01', displayName: '制管一线' },
          }
        if (path.includes('/mes/traceability/')) data = graph
        if (path.endsWith('/master-data/resources'))
          data = { resources: [{ code: 'NIGHT', displayName: shiftName, active: true }], total: 1 }
        if (path.endsWith('/mes/shift-handovers')) data = { items: [handover], total: 41 }
        if (path.endsWith(`/mes/shift-handovers/${handover.handoverId}`))
          data = {
            ...handover,
            wipItems: [],
            unfinishedWorkOrders: [],
            openIssues: [],
          }
        await route.fulfill({ json: { success: true, data } })
      })
    })

    test('交接筛选和分页继续传递原有查询（#4469）', async ({ page }) => {
      await page.goto('/mes/handovers')
      await expect(page.getByTestId('handovers-table')).toContainText(teamName)
      const next = page.waitForRequest((request) => {
        const url = new URL(request.url())
        return (
          url.pathname.endsWith('/mes/shift-handovers') && url.searchParams.get('skip') === '10'
        )
      })
      await page.getByRole('button', { name: '下一页', exact: true }).click()
      await next
      const filtered = page.waitForRequest((request) => {
        const url = new URL(request.url())
        return (
          url.pathname.endsWith('/mes/shift-handovers') &&
          url.searchParams.get('keyword') === teamName &&
          url.searchParams.get('skip') === '0'
        )
      })
      await page.getByRole('textbox', { name: '搜索班次交接' }).fill(teamName)
      await filtered
      await page.getByRole('combobox', { name: '交接状态', exact: true }).click()
      const status = page.waitForRequest((request) => {
        const url = new URL(request.url())
        return (
          url.pathname.endsWith('/mes/shift-handovers') && url.searchParams.get('status') === 'Open'
        )
      })
      await page.getByRole('option', { name: '待接班', exact: true }).click()
      await status
      await expect(page.getByTestId('handovers-table')).toContainText(teamName)
    })

    for (const viewport of [
      { width: 1024, height: 768 },
      { width: 1280, height: 800 },
      { width: 1440, height: 900 },
    ]) {
      for (const theme of ['light', 'dark']) {
        for (const kind of ['traceability', 'handovers']) {
          test(`${kind} ${viewport.width}×${viewport.height} ${theme}`, async ({
            page,
          }, testInfo) => {
            await page.setViewportSize(viewport)
            await page.addInitScript(
              (mode) => localStorage.setItem('nerv-iip-color-mode', mode),
              theme,
            )
            await page.goto(
              kind === 'traceability'
                ? '/mes/traceability?mode=batch&batchOrSerial=LOT-TUBE-20261017-000128'
                : '/mes/handovers',
            )
            const table = page.locator('table.nv-dt-table').first()
            await expect(table.locator('tbody tr').first()).toContainText(
              kind === 'traceability' ? workOrder : teamName,
            )
            await expect(
              page.getByRole('button', {
                name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
              }),
            ).toBeVisible()
            const measurement = await table.evaluate((element) => {
              const failures: string[] = []
              const widths = Array.from(element.querySelectorAll('thead th')).map((head) => ({
                header: head.textContent?.trim(),
                width: head.getBoundingClientRect().width,
              }))
              for (const cell of element.querySelectorAll('tbody td')) {
                const bounds = cell.getBoundingClientRect()
                const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
                let node: Node | null
                while ((node = walker.nextNode())) {
                  if (!node.textContent?.trim()) continue
                  const range = document.createRange()
                  range.selectNodeContents(node)
                  for (const rect of range.getClientRects()) {
                    if (rect.left < bounds.left - 1 || rect.right > bounds.right + 1)
                      failures.push(node.textContent)
                  }
                }
              }
              return {
                widths,
                failures,
                scrollWidth: element.parentElement!.scrollWidth,
                clientWidth: element.parentElement!.clientWidth,
              }
            })
            await writeFile(
              testInfo.outputPath('DOM-measurement.json'),
              JSON.stringify({ viewport, theme, kind, measurement }, null, 2),
            )
            await testInfo.attach('DOM-measurement', {
              body: JSON.stringify({ viewport, theme, kind, measurement }, null, 2),
              contentType: 'application/json',
            })
            await page.screenshot({ path: testInfo.outputPath(`${kind}-left.png`), fullPage: true })
            expect(measurement.failures, '文字必须留在所属列，且完整可读').toEqual([])
            expect(
              await page.evaluate(() => document.documentElement.scrollWidth),
            ).toBeLessThanOrEqual(viewport.width)
            if (kind === 'traceability') {
              await expect(
                table.getByRole('link', { name: workOrder, exact: true }),
              ).toHaveAttribute('href', new RegExp(`/mes/work-orders/${workOrder}`))
              const link = table.getByRole('link', { name: `报工 · ${reportNo}`, exact: true })
              await link.scrollIntoViewIfNeeded()
              expect(await table.evaluate((el) => el.parentElement!.scrollLeft)).toBeGreaterThan(0)
              await page.screenshot({
                path: testInfo.outputPath('traceability-right.png'),
                fullPage: true,
              })
              await link.click()
              await expect(page).toHaveURL(
                new RegExp(`/mes/production-reports\\?reportNo=${reportNo}`),
              )
            } else {
              await expect(table).toContainText(shiftName)
              await expect(table).toContainText(handover.outgoingUserName)
              await expect(table).toContainText(handover.incomingUserName)
              await expect(table).toContainText('待接班')
              const actions = table.getByRole('button', { name: '班次交接操作' })
              await expect(actions).toBeVisible()
              const scrolledRight = await table.evaluate((el) => {
                const container = el.parentElement!
                container.scrollLeft = container.scrollWidth
                return container.scrollLeft
              })
              expect(scrolledRight).toBeGreaterThan(0)
              await page.screenshot({
                path: testInfo.outputPath('handovers-right.png'),
                fullPage: true,
              })
              await actions.click()
              await page.getByRole('menuitem', { name: '接班', exact: true }).click()
              await expect(page.getByRole('dialog', { name: '确认接班' })).toBeVisible()
              await page.getByRole('button', { name: '取消', exact: true }).click()
              await table.locator('tbody tr').first().click()
              await expect(page.getByTestId('handover-detail')).toContainText(teamName)
            }
          })
        }
      }
    }
  })
}
