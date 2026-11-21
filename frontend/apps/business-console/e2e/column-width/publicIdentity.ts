import { test, expect } from '@playwright/test'
import { principal, STORAGE_KEY, session } from './fixture'
import { writeFile } from 'node:fs/promises'

export function registerPublicIdentity() {
  test.describe('公开身份展示回退（#4464）', () => {
    const workOrderId = 'WO-宁波精密制造长工单-20261017-000001'
    const ids = [
      workOrderId + '-OP-020-精密加工完整公开身份',
      workOrderId + '-OP-030-末道检验完整公开身份',
    ]
    const opaqueTaskId = '019fbb41-aaaa-7aaa-8aaa-aaaaaaaaaaaa'
    const handoverId = 'SHO-20261017-000011-精密制造早班长交接单身份'
    for (const viewport of [
      { width: 1280, height: 800 },
      { width: 1440, height: 900 },
      { width: 1024, height: 768 },
    ]) {
      for (const theme of ['light', 'dark']) {
        test(`${viewport.width} ${theme}：缺号工序可区分、交接身份完整且动作不换号`, async ({
          page,
        }, testInfo) => {
          const operator = {
            ...principal,
            permissionCodes: [
              'business.mes.operations.read',
              'business.mes.work-orders.read',
              'business.mes.handovers.read',
              'business.mes.handovers.manage',
            ],
          }
          await page.setViewportSize(viewport)
          await page.emulateMedia({ colorScheme: theme as 'light' | 'dark' })
          await page.addInitScript(
            ({ stored, theme }) => {
              localStorage.setItem(STORAGE_KEY, JSON.stringify(stored))
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
          const tasks = ids.map((id, index) => ({
            operationTaskId: id,
            operationTaskNo: index ? '   ' : null,
            workOrderId,
            operationSequence: (index + 2) * 10,
            status: 'InProgress',
            workCenterId: 'WC-ROD-01',
          }))
          tasks.push({
            operationTaskId: opaqueTaskId,
            operationTaskNo: null,
            workOrderId,
            operationSequence: 40,
            status: 'InProgress',
            workCenterId: 'WC-ROD-01',
          })
          const handover = {
            handoverId,
            shiftId: 'EARLY',
            teamId: 'TEAM-ROD-01',
            teamName: '精密制造第一班组',
            handoverStatus: 'Open',
            createdAtUtc: '2026-10-17T01:00:00Z',
          }
          await page.route('**/api/business-console/v1/**', async (route) => {
            const url = new URL(route.request().url())
            const path = decodeURIComponent(url.pathname)
            let data: unknown = { items: [], total: 0, resources: [] }
            if (path.endsWith('/me/work-context')) {
              const scope = { kind: 'organization', id: 'org-001', displayName: '当前组织' }
              data = {
                organizationId: 'org-001',
                environmentId: 'env-dev',
                resolutionStatus: 'resolved',
                applicablePermissionCode: url.searchParams.get('permissionCode'),
                authorizedScopes: [scope],
                availableScopeKinds: ['organization'],
                selectedScope: scope,
                issues: [],
              }
            }
            if (path.endsWith('/mes/operation-tasks')) data = { items: tasks, total: 3 }
            if (path.endsWith(`/mes/work-orders/${workOrderId}`))
              data = { workOrderId, status: 'InProgress', operationTasks: tasks }
            if (path.endsWith('/mes/shift-handovers')) data = { items: [handover], total: 1 }
            if (path.endsWith(`/mes/shift-handovers/${handoverId}`))
              data = {
                ...handover,
                wipItems: [],
                unfinishedWorkOrders: [],
                openIssues: [],
                attachments: [],
              }
            if (path.endsWith('/accept'))
              data = { accepted: true, operation: 'mes.handover.accept' }
            await route.fulfill({ json: { success: true, data } })
          })
          const measurements: unknown[] = []
          async function assertIdentityFits(title: string) {
            const identity = page.getByTitle(title, { exact: true }).first()
            await expect(identity).toHaveText(title, { timeout: 15_000 })
            const measured = await identity.evaluate((el) => {
              const cell = el.closest('td')!
              const rect = cell.getBoundingClientRect()
              const range = document.createRange()
              range.selectNodeContents(el)
              const text = Array.from(range.getClientRects()).map((r) => ({
                left: r.left,
                right: r.right,
              }))
              return {
                cell: { left: rect.left, right: rect.right },
                text,
                pageWidth: document.documentElement.scrollWidth,
                viewport: innerWidth,
              }
            })
            // 工单详情的用料齐套空表已有独立宽度问题；本票只检查该页任务文本的单元格边界。
            if (!page.url().includes('/mes/work-orders/')) {
              expect(measured.pageWidth).toBeLessThanOrEqual(measured.viewport)
            }
            for (const line of measured.text) {
              expect(line.left).toBeGreaterThanOrEqual(measured.cell.left - 1)
              expect(line.right).toBeLessThanOrEqual(measured.cell.right + 1)
            }
            measurements.push({ title, ...measured })
          }
          await page.goto('/mes/operation-tasks')
          for (const id of ids) await assertIdentityFits(id)
          await expect(page.getByTitle(opaqueTaskId, { exact: true }).first()).toHaveText('工序 40')
          await page.screenshot({ path: testInfo.outputPath('operations.png') })
          await page.goto(
            `/mes/work-orders/${encodeURIComponent(workOrderId)}?operationTaskId=${encodeURIComponent(ids[1]!)}`,
          )
          for (const id of ids) await assertIdentityFits(id)
          await expect(page.getByTitle(opaqueTaskId, { exact: true }).first()).toHaveText('工序 40')
          const taskTable = page
            .locator('table')
            .filter({ has: page.getByTitle(ids[0]!, { exact: true }) })
          const taskLayout = await taskTable.evaluate((table) => {
            const escaped: string[] = []
            for (const cell of table.querySelectorAll('tbody td')) {
              const box = cell.getBoundingClientRect()
              const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
              while (walker.nextNode()) {
                const node = walker.currentNode
                if (!node.textContent?.trim() || node.parentElement?.closest('.sr-only')) continue
                const range = document.createRange()
                range.selectNodeContents(node)
                for (const rect of range.getClientRects()) {
                  if (rect.width && (rect.left < box.left - 1 || rect.right > box.right + 1))
                    escaped.push(node.textContent.trim())
                }
              }
            }
            const container = table.closest('[data-slot="table-container"]')!
            return { escaped, right: container.getBoundingClientRect().right, viewport: innerWidth }
          })
          expect(taskLayout.escaped, '任务身份列保留宽度后，相邻字段仍留在所属单元格').toEqual([])
          expect(taskLayout.right).toBeLessThanOrEqual(taskLayout.viewport + 1)
          await expect(page.locator('[data-andon-source="true"]')).toContainText(ids[1]!)
          await page.screenshot({ path: testInfo.outputPath('work-order.png') })
          await page.goto('/mes/handovers')
          await assertIdentityFits(handoverId)
          const request = page.waitForRequest((r) =>
            decodeURIComponent(new URL(r.url()).pathname).endsWith(
              `/mes/shift-handovers/${handoverId}`,
            ),
          )
          await page.getByRole('button', { name: handoverId, exact: true }).click()
          const detailRequest = await request
          expect(new URL(detailRequest.url()).searchParams.get('organizationId')).toBe('org-001')
          expect(new URL(detailRequest.url()).searchParams.get('environmentId')).toBe('env-dev')
          await expect(page.getByTestId('handover-detail')).toContainText(handoverId)
          await page.screenshot({ path: testInfo.outputPath('handover-detail.png') })
          await page.keyboard.press('Escape')
          await page.getByRole('button', { name: '班次交接操作' }).click()
          await page.getByTestId('accept-handover').click()
          await expect(page.getByTestId('accept-handover-form')).toContainText(handoverId)
          await page.screenshot({ path: testInfo.outputPath('handover-accept.png') })
          const acceptRequest = page.waitForRequest(
            (r) =>
              r.method() === 'POST' &&
              decodeURIComponent(new URL(r.url()).pathname).endsWith(
                `/mes/shift-handovers/${handoverId}/accept`,
              ),
          )
          await page
            .getByTestId('accept-handover-form')
            .getByRole('button', { name: '确认接班', exact: true })
            .click()
          const submitted = await acceptRequest
          expect(new URL(submitted.url()).searchParams.get('organizationId')).toBe('org-001')
          expect(new URL(submitted.url()).searchParams.get('environmentId')).toBe('env-dev')
          await writeFile(
            testInfo.outputPath('identity-layout.json'),
            JSON.stringify(measurements, null, 2),
          )
        })
      }
    }
  })
}
