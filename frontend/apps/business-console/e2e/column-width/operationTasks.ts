import { test, expect } from '@playwright/test'
import { principal, session } from './fixture'

export function registerOperationTasks() {
  test.describe('工序执行关键列可读性（#4468）', () => {
    const workOrderNo = 'WO-ROD-20261017-000128-精密活塞杆'
    const assignedUserName = '陈立华（精密加工一线交接负责人）'
    const task = {
      operationTaskId: 'OPT-ROD-20261017-000128-OP-020',
      operationTaskNo: 'OPT-ROD-20261017-000128-OP-020',
      workOrderId: workOrderNo,
      workOrderNo,
      operationSequence: 20,
      workCenterId: 'WC-ROD-01',
      workCenterName: '活塞杆精密加工一线工作中心',
      deviceAssetId: 'EQ-ROD-CNC-20261017-000128',
      deviceAssetCode: 'EQ-ROD-CNC-20261017-000128',
      deviceAssetName: '数控外圆磨床（精密活塞杆加工单元）',
      shiftId: 'SHIFT-DAY',
      status: 'InProgress',
      qualityStatus: 'QUALITY_PLAN_MISSING',
      plannedStartUtc: '2026-10-17T01:30:00Z',
      assignedUserId: 'operator-chen',
      assignedUserName,
      actualLaborHours: 12.5,
      actualMachineHours: 10.25,
    }

    for (const viewport of [
      { width: 1280, height: 800 },
      { width: 1440, height: 900 },
      { width: 1024, height: 768 },
    ]) {
      for (const theme of ['light', 'dark'] as const) {
        test(`${viewport.width}×${viewport.height} ${theme}：长标识不跨列、滚动后行操作可用`, async ({
          page,
        }, testInfo) => {
          const operator = {
            ...principal,
            permissionCodes: [
              'business.mes.operations.read',
              'business.mes.work-orders.read',
              'business.mes.reporting.write',
              'business.master-data.read',
            ],
          }
          await page.setViewportSize(viewport)
          await page.emulateMedia({ colorScheme: theme })
          await page.addInitScript(
            ({ stored, theme }) => {
              localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored))
              localStorage.setItem('nerv-iip-color-mode', theme)
            },
            { stored: { ...session, principal: operator }, theme },
          )
          await page.route('**/api/console/v1/**', (route) =>
            route.fulfill({
              json: {
                success: true,
                data: new URL(route.request().url()).pathname.endsWith('/auth/me')
                  ? operator
                  : { ...session, principal: operator },
              },
            }),
          )
          await page.route('**/api/business-console/v1/**', async (route) => {
            const url = new URL(route.request().url())
            const path = url.pathname
            let data: unknown = { items: [], total: 0 }
            if (path.endsWith('/me/work-context')) {
              const scope = {
                kind: 'workCenter',
                id: 'WC-ROD-01',
                displayName: '活塞杆精密加工一线',
              }
              data = {
                organizationId: 'org-001',
                environmentId: 'env-dev',
                applicablePermissionCode: url.searchParams.get('permissionCode'),
                resolutionStatus: 'resolved',
                authorizedScopes: [scope],
                availableScopeKinds: ['workCenter'],
                selectedScope: scope,
                issues: [],
              }
            }
            if (path.endsWith('/mes/operation-tasks')) data = { items: [task], total: 1 }
            if (path.endsWith(`/mes/work-orders/${encodeURIComponent(workOrderNo)}`))
              data = {
                workOrderId: workOrderNo,
                workOrderNo,
                status: 'InProgress',
                operationTasks: [task],
              }
            if (
              path.endsWith('/master-data/resources') &&
              url.searchParams.get('resourceType') === 'shift'
            )
              data = {
                items: [{ code: 'SHIFT-DAY', displayName: '白班（08:00—16:00）' }],
                total: 1,
              }
            await route.fulfill({ json: { success: true, data } })
          })
          await page.goto('/mes/operation-tasks')
          // 使用实际主题类，避免系统主题或本机偏好令两组截图落在同一主题。
          await page
            .locator('html')
            .evaluate((el, dark) => el.classList.toggle('dark', dark), theme === 'dark')
          const table = page.locator('table.nv-dt-table').first()
          const row = table.locator('tbody tr').first()
          await expect(row).toContainText(task.operationTaskNo)
          await expect(row).toContainText(task.deviceAssetName)
          await page.screenshot({
            path: testInfo.outputPath('operations-left.png'),
            fullPage: true,
          })
          const assigned = row.getByLabel(`状态：已派 ${assignedUserName}`, { exact: true })
          await assigned.scrollIntoViewIfNeeded()
          await page.screenshot({
            path: testInfo.outputPath('operations-assigned.png'),
            fullPage: true,
          })
          const measurement = await table.evaluate((table) => {
            const headers = Array.from(table.querySelectorAll('thead th')).map((th) => ({
              header: th.textContent?.trim(),
              width: th.getBoundingClientRect().width,
            }))
            const escaped: string[] = []
            for (const cell of table.querySelectorAll('tbody tr:first-child td')) {
              const bounds = cell.getBoundingClientRect()
              const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
              while (walker.nextNode()) {
                const node = walker.currentNode
                if (!node.textContent?.trim() || node.parentElement?.closest('.sr-only')) continue
                const range = document.createRange()
                range.selectNodeContents(node)
                for (const rect of range.getClientRects()) {
                  // Text Range 包含被 truncate 裁掉的部分，只判断实际可绘制区域。
                  let left = rect.left
                  let right = rect.right
                  let top = rect.top
                  let bottom = rect.bottom
                  for (let parent = node.parentElement; parent; parent = parent.parentElement) {
                    const style = getComputedStyle(parent)
                    const clip = parent.getBoundingClientRect()
                    if (['hidden', 'clip', 'auto', 'scroll'].includes(style.overflowX)) {
                      left = Math.max(left, clip.left)
                      right = Math.min(right, clip.right)
                    }
                    if (['hidden', 'clip', 'auto', 'scroll'].includes(style.overflowY)) {
                      top = Math.max(top, clip.top)
                      bottom = Math.min(bottom, clip.bottom)
                    }
                  }
                  if (
                    right > left &&
                    bottom > top &&
                    (left < bounds.left - 1 || right > bounds.right + 1)
                  )
                    escaped.push(node.textContent.trim())
                }
              }
            }
            const container = table.closest('[data-slot="table-container"]') as HTMLElement
            return {
              headers,
              assigned: (() => {
                const cell = table.querySelectorAll('tbody tr:first-child td')[8]!
                const badge = cell.querySelector('[aria-label]')!
                return {
                  cell: cell.getBoundingClientRect().toJSON(),
                  badge: badge.getBoundingClientRect().toJSON(),
                }
              })(),
              escaped,
              containerWidth: container.clientWidth,
              scrollWidth: container.scrollWidth,
              documentWidth: document.documentElement.scrollWidth,
              viewportWidth: window.innerWidth,
            }
          })
          await testInfo.attach('DOM测量（浏览器fixture）', {
            body: JSON.stringify(measurement, null, 2),
            contentType: 'application/json',
          })
          console.log(JSON.stringify({ viewport, theme, ...measurement }))
          expect(measurement.escaped, '文本应留在所属单元格内').toEqual([])
          expect(measurement.assigned.badge.left).toBeGreaterThanOrEqual(
            measurement.assigned.cell.left,
          )
          expect(measurement.assigned.badge.right).toBeLessThanOrEqual(
            measurement.assigned.cell.right,
          )
          await expect(assigned).toHaveAttribute('title', `已派 ${assignedUserName}`)
          await expect(row).toContainText('12.5')
          await expect(row).toContainText('10.25')
          expect(measurement.documentWidth).toBeLessThanOrEqual(measurement.viewportWidth)
          await row.getByRole('button', { name: workOrderNo, exact: true }).click()
          await expect(
            page.getByRole('dialog', { name: '工单速览' }).getByText('工序 20', { exact: true }),
          ).toBeVisible()
          await page.keyboard.press('Escape')
          await expect(page.getByRole('dialog', { name: '工单速览' })).toBeHidden()
          const scroll = table.locator('..')
          await scroll.evaluate((el) => {
            el.scrollLeft = el.scrollWidth
          })
          expect(await scroll.evaluate((el) => el.scrollLeft)).toBeGreaterThan(0)
          await page.screenshot({
            path: testInfo.outputPath('operations-right.png'),
            fullPage: true,
          })
          await row.getByRole('button', { name: '工序任务操作 工序 20' }).click()
          await expect(page.getByRole('menuitem', { name: '查看工单' })).toBeVisible()
          await page.getByRole('menuitem', { name: '查看工单' }).click()
          await expect(page.getByRole('dialog', { name: '工单速览' })).toBeVisible()
          await page.keyboard.press('Escape')
          await expect(page.getByRole('dialog', { name: '工单速览' })).toBeHidden()

          // 同一页面继续走真实分页/搜索事件；fixture 按请求的页窗返回数据。
          await page.route('**/mes/operation-tasks?**', async (route) => {
            const query = new URL(route.request().url()).searchParams
            const skip = Number(query.get('skip') ?? 0)
            const take = Number(query.get('take') ?? 10)
            const all = Array.from({ length: 11 }, (_, index) => ({
              ...task,
              operationTaskId: `${task.operationTaskId}-${index}`,
              operationTaskNo: `${task.operationTaskNo}-${index}`,
            }))
            const filtered = query.get('keyword')
              ? all.filter((item) => item.operationTaskNo.endsWith('-10'))
              : all
            await route.fulfill({
              json: {
                success: true,
                data: { items: filtered.slice(skip, skip + take), total: filtered.length },
              },
            })
          })
          await page.getByRole('button', { name: '刷新', exact: true }).click()
          await expect(table.locator('tbody tr')).toHaveCount(10)
          await page.getByRole('button', { name: '下一页', exact: true }).click()
          await expect(table.locator('tbody tr')).toHaveCount(1)
          await expect(row).toContainText(`${task.operationTaskNo}-10`)
          const searchRequest = page.waitForRequest((request) => {
            const url = new URL(request.url())
            return (
              url.pathname.endsWith('/mes/operation-tasks') &&
              url.searchParams.get('keyword') === '-10' &&
              url.searchParams.get('skip') === '0'
            )
          })
          await page.getByPlaceholder('搜索任务、工单、设备').fill('-10')
          await searchRequest
          await expect(row).toContainText(`${task.operationTaskNo}-10`)
          await expect(page.getByRole('button', { name: '下一页', exact: true })).toHaveCount(0)
        })
      }
    }
  })
}
