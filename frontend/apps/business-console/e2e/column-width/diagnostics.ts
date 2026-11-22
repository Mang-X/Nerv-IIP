import { test, type Page, expect } from '@playwright/test'

export function registerDiagnostics() {
  test.describe('长诊断内容留在所属列（#4330）', () => {
    // Regression / #4330、#4286 Spec r1：实际渲染中长内容留在所属列，原文及业务动作可达。
    // API fixture 只证明浏览器布局；真实服务证据单独记录。
    const eventType = 'erp.OperationActualTimeLaborCostSettlementRequestedForCompletedWorkOrder'
    const consumerName = 'business-erp.operation-actual-time-labor-cost-settlement-consumer'
    const failureCode = 'missing-work-center-cost-rate'
    const failureMessage =
      '工作中心 WC-TUB-01（制管一线）在工序完工时间没有生效的人工成本费率，请维护费率后再处理。'
    const principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'admin',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionVersion: 1,
      permissionCodes: [
        'business.dlq.read',
        'business.dlq.manage',
        'business.mes.receipts.read',
        'business.mes.receipts.manage',
      ],
    }
    const session = {
      accessToken: 'access-token',
      refreshToken: 'refresh-token',
      sessionId: 'session-1',
      expiresAtUtc: '2099-01-01T00:00:00.000Z',
      principal,
    }
    const deadLetter = {
      id: 'dl-1',
      eventType,
      consumerName,
      failureCode,
      failureMessage,
      status: 'pending',
      deadLetteredAtUtc: '2026-10-12T02:31:59Z',
      eventJson: JSON.stringify({ workOrderNo: 'WO-TUB-20261012-001' }),
    }

    test.beforeEach(async ({ page }) => {
      await page.addInitScript((stored) => {
        localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored))
      }, session)
      await page.route('**/api/console/v1/**', async (route) => {
        const path = new URL(route.request().url()).pathname
        await route.fulfill({
          json: { success: true, data: path.endsWith('/auth/me') ? principal : session },
        })
      })
      await page.route('**/api/business-console/v1/**', async (route) => {
        const path = new URL(route.request().url()).pathname
        let data: unknown = {}
        if (path.endsWith('/me/work-context')) data = { organizationId: 'org-001' }
        if (path.endsWith('/dead-letters/metrics'))
          data = {
            pendingCount: 1,
            failedCount: 0,
            replayedCount: 0,
            ignoredCount: 0,
            services: [{ service: 'Erp', metrics: { pendingCount: 1, failedCount: 0 } }],
          }
        if (path.endsWith('/dead-letters')) data = { items: [{ service: 'Erp', deadLetter }] }
        if (path.endsWith('/dead-letters/Erp/dl-1')) data = deadLetter
        if (path.endsWith('/mes/finished-goods-receipt-requests'))
          data = {
            totalCount: 2,
            items: [
              {
                receiptRequestId: 'receipt-1',
                requestNo: 'FGR-20261012-001',
                workOrderId: 'wo-1',
                workOrderNo: 'WO-TUB-20261012-001',
                skuCode: 'FG-TUB-001',
                quantity: 8,
                receiptStatus: 'Requested',
                costCapitalization: { workOrderCompleted: false },
                requestedAtUtc: '2026-10-12T02:31:59Z',
              },
              {
                receiptRequestId: 'receipt-2',
                requestNo: 'FGR-20261012-002',
                workOrderId: 'wo-2',
                workOrderNo: 'WO-TUB-20261012-002',
                skuCode: 'FG-TUB-001',
                quantity: 8,
                receiptStatus: 'InventoryPostingFailed',
                inventoryPostingFailureMessage:
                  failureMessage + ' CostSettlementReference-WC-TUB-20261012-001',
                requestedAtUtc: '2026-10-12T02:31:59Z',
              },
            ],
          }
        await route.fulfill({ json: { success: true, data } })
      })
    })

    async function assertTextContained(page: Page) {
      const overflow = await page.locator('table tbody td').evaluateAll((cells) =>
        cells.flatMap((cell) => {
          const bounds = cell.getBoundingClientRect()
          const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
          const failures: string[] = []
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
          return failures
        }),
      )
      expect(overflow, '文字跨出了所属单元格').toEqual([])
    }

    for (const width of [1024, 1280, 1440, 1366]) {
      for (const theme of ['light', 'dark']) {
        test(`死信长内容 ${width}×900 ${theme}`, async ({ page }, testInfo) => {
          await page.setViewportSize({ width, height: 900 })
          await page.addInitScript(
            (mode) => localStorage.setItem('nerv-iip-color-mode', mode),
            theme,
          )
          await page.goto('/ops/dead-letters', { waitUntil: 'domcontentloaded' })
          await expect(page.getByText(eventType, { exact: true })).toBeVisible()
          await expect(page.getByText(consumerName, { exact: true })).toBeVisible()
          await expect(
            page.getByRole('button', {
              name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
            }),
          ).toBeVisible()
          expect(
            await page.evaluate(() => document.documentElement.scrollWidth),
          ).toBeLessThanOrEqual(width)
          const row = page.getByRole('row').filter({ hasText: eventType })
          await expect(row.getByText(failureCode, { exact: true })).toBeVisible()
          await expect(row.getByText('待处理', { exact: true })).toBeVisible()
          await assertTextContained(page)
          const detail = page.getByRole('button', { name: '详情', exact: true })
          await detail.scrollIntoViewIfNeeded()
          await expect(page.getByRole('button', { name: `重放死信：${eventType}` })).toBeEnabled()
          await page.screenshot({ path: testInfo.outputPath('dead-letters.png'), fullPage: true })
          await detail.click()
          const dialog = page.getByRole('dialog')
          await expect(dialog.getByText(eventType, { exact: true })).toBeVisible()
          await expect(dialog.getByText(consumerName, { exact: true })).toBeVisible()
          await expect(dialog.getByText(failureMessage, { exact: true })).toBeVisible()
          await expect(dialog.locator('pre')).toContainText('WO-TUB-20261012-001')
          await page.screenshot({
            path: testInfo.outputPath('dead-letter-detail.png'),
            fullPage: true,
          })
        })
        test(`完工长内容 ${width}×900 ${theme}`, async ({ page }, testInfo) => {
          await page.setViewportSize({ width, height: 900 })
          await page.addInitScript(
            (mode) => localStorage.setItem('nerv-iip-color-mode', mode),
            theme,
          )
          await page.goto('/mes/receipts', { waitUntil: 'domcontentloaded' })
          await expect(page.getByTestId('receipt-pending-reason')).toContainText(
            '等待 ERP 收到工单完工',
          )
          await expect(
            page.getByRole('button', {
              name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
            }),
          ).toBeVisible()
          expect(
            await page.evaluate(() => document.documentElement.scrollWidth),
          ).toBeLessThanOrEqual(width)
          await expect(
            page.getByText(failureMessage + ' CostSettlementReference-WC-TUB-20261012-001', {
              exact: true,
            }),
          ).toBeVisible()
          await expect(page.getByText('入库失败', { exact: true })).toBeVisible()
          await assertTextContained(page)
          const retry = page.getByRole('button', { name: '重试', exact: true })
          await retry.scrollIntoViewIfNeeded()
          await expect(retry).toBeEnabled()
          await expect(page.getByText('待入库', { exact: true })).toBeVisible()
          await page.screenshot({ path: testInfo.outputPath('receipts.png'), fullPage: true })
        })
      }
    }
  })
}
