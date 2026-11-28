import { test, expect } from '@playwright/test'
import { principal, session } from './fixture'
import { writeFile } from 'node:fs/promises'

export function registerDocumentIdentity() {
  test.describe('采购与盘点长标识可读性（#3940）', () => {
    for (const width of [1440, 1024, 390]) {
      test(`${width}：完整文本留在所属列，横向滚动后盘点操作可达`, async ({ page }, testInfo) => {
        const operator = {
          ...principal,
          permissionCodes: [
            'business.erp.procurement.read',
            'business.inventory.counts.manage',
            'business.masterdata.products.read',
          ],
        }
        await page.setViewportSize({ width, height: 900 })
        await page.addInitScript(
          (stored) =>
            localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
          { ...session, principal: operator },
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
          let data: unknown = { items: [], total: 0, totalCount: 0, resources: [] }
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
          if (path.endsWith('/master-data/skus'))
            data = { resources: [{ code: 'SEAL-020', displayName: '油封 φ20 骨架式' }] }
          if (path.endsWith('/master-data/resources'))
            data = {
              resources:
                url.searchParams.get('resourceType') === 'business-partner'
                  ? [{ code: 'SUP-SEAL-01', displayName: '宁波密封件制造有限公司' }]
                  : [],
            }
          if (path.endsWith('/erp/procurement/purchase-orders'))
            data = {
              total: 1,
              items: [
                {
                  purchaseOrderNo: 'PO-20260928-000001',
                  supplierCode: 'SUP-SEAL-01',
                  status: 'draft',
                  receiptReadiness: 'pending',
                  lines: [
                    {
                      lineNo: '1',
                      skuCode: 'SEAL-020',
                      orderedQuantity: 120,
                      receivedQuantity: 0,
                      unitPrice: 12,
                      sources: [{ purchaseRequisitionNo: 'PR-20260928-000001-精密制造采购申请' }],
                    },
                  ],
                },
              ],
            }
          if (path.endsWith('/inventory/count-tasks'))
            data = {
              totalCount: 2,
              items: ['SCT-20260928-000001', 'COUNT-1790553600000'].map((countTaskCode, index) => ({
                countTaskId: `count-${index}`,
                countTaskCode,
                skuCode: 'SEAL-020',
                siteCode: 'SITE-001',
                locationCode: 'loc-raw-01',
                lotNo: index ? 'LOT-20260928-000001-原料来料批次' : null,
                countedQuantity: 120,
                varianceQuantity: 0,
                status: 'open',
              })),
            }
          await route.fulfill({ json: { success: true, data } })
        })

        const measurements: unknown[] = []
        async function assertStandardCodeFits(code: string) {
          const cell = page.getByRole('cell', { name: code, exact: true })
          await expect(cell).toBeVisible()
          const lines = await cell.evaluate((element) => {
            const lineTops = new Set<number>()
            const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
            const range = document.createRange()
            while (walker.nextNode()) {
              const node = walker.currentNode
              if (!node.textContent?.trim()) continue
              range.selectNodeContents(node)
              for (const rect of range.getClientRects()) {
                if (rect.width > 0) lineTops.add(rect.top)
              }
            }
            return lineTops.size
          })
          expect(lines, '常规单号应有足够宽度保持一行').toBe(1)
        }
        async function assertTextStaysInCells() {
          const layout = await page.locator('table.nv-dt-table').evaluate((table) => {
            const escaped: string[] = []
            const widths = Array.from(table.querySelectorAll('thead th')).map((cell) => ({
              header: cell.textContent?.trim(),
              width: cell.getBoundingClientRect().width,
            }))
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
            return {
              escaped,
              widths,
              pageWidth: document.documentElement.scrollWidth,
              viewport: innerWidth,
            }
          })
          measurements.push({ url: page.url(), ...layout })
          expect(layout.escaped, '文字不得越过所属单元格').toEqual([])
          expect(layout.pageWidth, '水平滚动应留在表内').toBeLessThanOrEqual(layout.viewport)
        }

        await page.goto('/erp/procurement/purchase-orders')
        await expect(
          page.getByRole('cell', { name: '宁波密封件制造有限公司 SUP-SEAL-01' }),
        ).toBeVisible()
        await assertStandardCodeFits('PO-20260928-000001')
        if (width === 1440) {
          const approval = page.getByRole('columnheader', { name: '审批', exact: true })
          await expect(approval).toBeInViewport({ ratio: 1 })
          for (const name of ['订单状态', '收货状态']) {
            const status = page.getByRole('columnheader', { name, exact: true })
            await expect(status).toBeInViewport({ ratio: 1 })
            const statusBox = (await status.boundingBox())!
            expect(statusBox.x + statusBox.width).toBeLessThanOrEqual(
              (await approval.boundingBox())!.x,
            )
          }
        }
        await expect(page.getByRole('cell', { name: '油封 φ20 骨架式 SEAL-020' })).toBeVisible()
        await page.screenshot({ path: testInfo.outputPath('purchase-orders.png') })
        await assertTextStaysInCells()
        await page.goto('/inventory/counts')
        await assertStandardCodeFits('SCT-20260928-000001')
        await assertStandardCodeFits('COUNT-1790553600000')
        await expect(
          page.getByRole('cell', { name: 'SITE-001 / loc-raw-01', exact: true }).first(),
        ).toBeVisible()
        await expect(
          page.getByRole('cell', { name: '油封 φ20 骨架式 SEAL-020' }).first(),
        ).toBeVisible()
        await page.screenshot({ path: testInfo.outputPath('counts.png') })
        await assertTextStaysInCells()
        await page
          .getByRole('button', { name: '盘点操作 SCT-20260928-000001', exact: true })
          .click()
        await page.getByRole('menuitem', { name: '确认差异' }).click()
        await expect(page.getByRole('dialog')).toContainText('SCT-20260928-000001')
        await writeFile(testInfo.outputPath('layout.json'), JSON.stringify(measurements, null, 2))
      })
    }
  })
}
