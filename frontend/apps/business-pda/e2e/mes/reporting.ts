import { test, expect } from '@playwright/test'
import { routeBusinessConsoleApi, productionReportReceipt } from '../fixtures'

export function registerReporting() {
  test('报工：选工单 → 选工序 → 录良品数 → 提交 → 成功结果', async ({ page }) => {
    let submittedReport: Record<string, unknown> | undefined
    let readbackCount = 0
    await page.route(
      '**/api/business-console/v1/mes/production-reports/RPT-E2E-0001**',
      async (route) => {
        readbackCount += 1
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              report: {
                productionReportId: '019f-e2e-production-report',
                reportNo: 'RPT-E2E-0001',
                workOrderId: 'WO-1',
                operationTaskId: 'OP-1',
                goodQuantity: 5,
                scrapQuantity: 0,
                reworkQuantity: 0,
              },
              consumedMaterialLots: [],
              laborAllocations: [],
            },
          }),
        })
      },
    )
    await page.route('**/api/business-console/v1/mes/production-reports', async (route) => {
      if (route.request().method() !== 'POST') {
        return routeBusinessConsoleApi(route)
      }
      submittedReport = route.request().postDataJSON() as Record<string, unknown>
      const productionReportId = '019f-e2e-production-report'
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            productionReportId,
            reportNo: 'RPT-E2E-0001',
            // 写操作必须带权威回执，否则前端只会判「结果尚未核实」（#1219）。
            operationReceipt: productionReportReceipt(
              productionReportId,
              String(submittedReport.idempotencyKey ?? ''),
            ),
          },
        }),
      })
    })
    await page.goto('/mes/report')

    await expect(page.getByRole('heading', { name: '报工' })).toBeVisible()

    // 步骤 1：选工单（WO-1）。
    const workOrderRow = page.getByText('WO-1', { exact: true })
    await expect(workOrderRow).toBeVisible()
    await workOrderRow.click()

    // 步骤 2：选工序（按工单过滤后仍返回 mock 工序）。
    await expect(page.getByText('当前工单')).toBeVisible()
    const taskRow = page.getByText('WO-1 · 工序 10')
    await expect(taskRow).toBeVisible()
    await taskRow.click()

    // 步骤 3：录数量（BottomSheet）→ 良品数。
    const goodQty = page.getByTestId('good-quantity')
    await expect(goodQty).toBeVisible()
    await goodQty.fill('5')

    // 提交报工 → 成功 Result。
    await page.getByTestId('submit-report').click()
    const result = page.locator('[data-result][data-status="success"]')
    await expect(result).toBeVisible()
    await expect(result.getByText('报工成功')).toBeVisible()
    await expect(result).toContainText('RPT-E2E-0001')
    await expect(result).not.toContainText('019f-e2e-production-report')
    expect(readbackCount).toBe(1)
    expect(submittedReport).toMatchObject({
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
      goodQuantity: 5,
    })
  })

  test('报工：POST confirmed 但公开 GET 回读错实体时不得显示成功', async ({ page }) => {
    await page.route('**/api/business-console/v1/mes/production-reports/RPT-E2E-WRONG**', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            report: {
              productionReportId: '019f-e2e-wrong',
              reportNo: 'RPT-E2E-WRONG',
              workOrderId: 'WO-OTHER',
              operationTaskId: 'OP-OTHER',
            },
          },
        }),
      }),
    )
    await page.route('**/api/business-console/v1/mes/production-reports', async (route) => {
      if (route.request().method() !== 'POST') return routeBusinessConsoleApi(route)
      const submitted = route.request().postDataJSON() as Record<string, unknown>
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            productionReportId: '019f-e2e-wrong',
            reportNo: 'RPT-E2E-WRONG',
            operationReceipt: productionReportReceipt(
              '019f-e2e-wrong',
              String(submitted.idempotencyKey ?? ''),
            ),
          },
        }),
      })
    })

    await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
    await page.getByTestId('good-quantity').fill('1')
    await page.getByTestId('submit-report').click()

    const result = page.locator('[data-result][data-status="error"]')
    await expect(result).toBeVisible()
    await expect(result).toContainText('尚未回读到同一工单与工序')
    await expect(page.locator('[data-result][data-status="success"]')).toHaveCount(0)
  })

  test('报工：workshop 写范围只消费服务端权威 reportable 集合', async ({ page }) => {
    const reportableQueries: Array<Record<string, string>> = []
    await page.route('**/api/business-console/v1/me/work-context*', async (route) => {
      const url = new URL(route.request().url())
      const scopeKind = url.searchParams.get('scopeKind') ?? ''
      const scopeId = url.searchParams.get('scopeId') ?? ''
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            organizationId: 'org-001',
            environmentId: 'env-dev',
            principal: { id: 'user-001', principalType: 'User' },
            authorizedScopes: [{ kind: 'workshop', id: 'WS-1', displayName: '一车间' }],
            selectedScope:
              scopeKind === 'workshop' && scopeId === 'WS-1'
                ? { kind: 'workshop', id: 'WS-1', displayName: '一车间' }
                : null,
          },
        }),
      })
    })
    await page.route(
      '**/api/business-console/v1/mes/reportable-operation-tasks*',
      async (route) => {
        const url = new URL(route.request().url())
        reportableQueries.push(Object.fromEntries(url.searchParams.entries()))
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              items: [
                {
                  operationTaskId: 'OP-1',
                  workOrderId: 'WO-1',
                  status: 'InProgress',
                  allowedActions: ['report'],
                  operationSequence: 10,
                  workCenterId: 'WC-A',
                  qualityStatus: 'Pending',
                },
              ],
              total: 1,
            },
          }),
        })
      },
    )

    await page.goto('/mes/report?workOrderId=WO-1')
    await expect(page.getByText('WO-1 · 工序 10')).toBeVisible()
    await expect(page.getByText('WO-1 · 工序 20')).toHaveCount(0)
    expect(reportableQueries).toContainEqual(
      expect.objectContaining({
        scopeKind: 'workshop',
        scopeId: 'WS-1',
        workOrderId: 'WO-1',
      }),
    )

    await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-2')
    await expect(page.getByTestId('report-route-issue')).toContainText('当前不可报工')
    await expect(page.getByTestId('submit-report')).toHaveCount(0)
  })

  test('返工报工：router pair 切换、延迟旧请求与浏览器 back/forward 始终重绑同一实体', async ({
    page,
  }) => {
    let operationTaskDiscoveryCalls = 0
    let reportableAuthorityCalls = 0
    let resolveFirstDetailStarted!: () => void
    let resolveFirstDetailRelease!: () => void
    const firstDetailStarted = new Promise<void>((resolve) => {
      resolveFirstDetailStarted = resolve
    })
    const firstDetailRelease = new Promise<void>((resolve) => {
      resolveFirstDetailRelease = resolve
    })
    const reworkDetail = (suffix: 'A' | 'B') => ({
      success: true,
      data: {
        workOrderId: `WO-RW-${suffix}`,
        skuId: 'SKU-RW',
        quantity: 3,
        status: 'Released',
        readinessStatus: 'Ready',
        blockingReasons: [],
        workOrderType: 'rework',
        sourceWorkOrderId: `WO-SOURCE-${suffix}`,
        sourceNcrId: `ncr-${suffix.toLowerCase()}`,
        sourceNcrCode: `NCR-2026-000${suffix === 'A' ? '1' : '2'}`,
        operationTasks: [
          {
            operationTaskId: `OP-RW-${suffix}`,
            workOrderId: `WO-RW-${suffix}`,
            status: 'InProgress',
            operationSequence: suffix === 'A' ? 10 : 20,
            workCenterId: 'WC-A',
            qualityStatus: 'Pending',
            allowedActions: ['report'],
            blockReasons: [],
            workOrderType: 'rework',
            sourceWorkOrderId: `WO-SOURCE-${suffix}`,
            sourceNcrId: `ncr-${suffix.toLowerCase()}`,
            sourceNcrCode: `NCR-2026-000${suffix === 'A' ? '1' : '2'}`,
          },
        ],
      },
    })
    let workOrderADetailCalls = 0
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      operationTaskDiscoveryCalls += 1
      return routeBusinessConsoleApi(route)
    })
    await page.route(
      '**/api/business-console/v1/mes/reportable-operation-tasks**',
      async (route) => {
        reportableAuthorityCalls += 1
        const workOrderId = new URL(route.request().url()).searchParams.get('workOrderId')
        const suffix = workOrderId === 'WO-RW-A' ? 'A' : workOrderId === 'WO-RW-B' ? 'B' : null
        const items = suffix ? reworkDetail(suffix).data.operationTasks : []
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ success: true, data: { items, total: items.length } }),
        })
      },
    )
    await page.route('**/api/business-console/v1/mes/work-orders/WO-RW-A**', async (route) => {
      workOrderADetailCalls += 1
      if (workOrderADetailCalls === 1) {
        resolveFirstDetailStarted()
        await firstDetailRelease
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(reworkDetail('A')),
      })
    })
    await page.route('**/api/business-console/v1/mes/work-orders/WO-RW-B**', (route) => {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(reworkDetail('B')),
      })
    })

    try {
      await page.goto('/mes/report')
      await expect(page.getByRole('heading', { name: '报工' })).toBeVisible()
      await page.evaluate(async (target) => {
        const { router } = await import(/* @vite-ignore */ '/src/router/index.ts')
        await router.push(target)
      }, '/mes/report?workOrderId=WO-RW-A&operationTaskId=OP-RW-A')
      await firstDetailStarted
      await page.evaluate(async (target) => {
        const { router } = await import(/* @vite-ignore */ '/src/router/index.ts')
        await router.push(target)
      }, '/mes/report?workOrderId=WO-RW-B&operationTaskId=OP-RW-B')

      await expect(page.getByText('当前工单')).toBeVisible()
      await expect(
        page.getByRole('heading', { name: '返工 · WO-RW-B · 工序 20', exact: true }),
      ).toBeVisible()
      await expect(page.getByTestId('report-rework-source')).toContainText(
        '来源 NCR NCR-2026-0002 · 源工单 WO-SOURCE-B',
      )
      await expect(page.getByTestId('good-quantity')).toBeVisible()
      await expect(page.getByTestId('report-route-issue')).toHaveCount(0)
      resolveFirstDetailRelease()
      await expect(page.getByText(/WO-RW-A/)).toHaveCount(0)
      expect(operationTaskDiscoveryCalls).toBe(0)
      expect(reportableAuthorityCalls).toBeGreaterThan(0)

      await page.goBack()
      await expect(
        page.getByRole('heading', { name: '返工 · WO-RW-A · 工序 10', exact: true }),
      ).toBeVisible()
      await expect(page.getByTestId('report-rework-source')).toContainText(
        '来源 NCR NCR-2026-0001 · 源工单 WO-SOURCE-A',
      )
      await expect(page.getByTestId('good-quantity')).toBeVisible()

      await page.goForward()
      await expect(
        page.getByRole('heading', { name: '返工 · WO-RW-B · 工序 20', exact: true }),
      ).toBeVisible()
      await expect(page.getByTestId('good-quantity')).toBeVisible()
    } finally {
      // Never leave the intercepted route pending when an assertion or navigation fails.
      resolveFirstDetailRelease()
    }
  })

  test('报工：详情前 500 项不含目标时，完整 pair URL 分页解析第 501 项', async ({ page }) => {
    await page.goto('/mes/report?workOrderId=WO-501&operationTaskId=OP-501')

    await expect(page.getByText('当前工单')).toBeVisible()
    await expect(
      page.getByRole('heading', { name: 'WO-501 · 工序 501', exact: true }),
    ).toBeVisible()
    await expect(page.getByTestId('good-quantity')).toBeVisible()
    await expect(page.getByTestId('report-route-issue')).toHaveCount(0)
  })
}
