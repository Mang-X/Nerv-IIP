import { expect, test, type Page } from '@playwright/test'
import {
  mesOperationTasks,
  mesWorkOrders,
  productionReportReceipt,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
} from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
  await page.route('**/master-data/resources/sku/**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: {
          resourceType: 'sku',
          code: 'SKU-1',
          active: true,
          serialTrackingPolicy: 'on-production',
          defaultBarcodeRuleCode: 'UNIT',
        },
      },
    }),
  )
  await page.route('**/barcode/templates?**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: {
          templates: [
            {
              templateId: 'tpl-1',
              templateCode: 'FINISHED',
              templateName: '成品标签',
              status: 'active',
            },
          ],
          total: 1,
        },
      },
    }),
  )
  await page.route('**/mes/production-reports/RPT-SERIAL?**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: {
          report: {
            productionReportId: 'report-serial',
            reportNo: 'RPT-SERIAL',
            workOrderId: 'WO-1',
            operationTaskId: 'OP-1',
          },
        },
      },
    }),
  )
})

async function enterGood(page: Page, quantity: string) {
  await page.getByTestId('good-quantity').tap()
  const keyboard = page.locator('[data-slot="number-keyboard"]')
  for (const digit of quantity)
    await keyboard.getByRole('button', { name: digit, exact: true }).tap()
  await keyboard.getByRole('button', { name: '完成', exact: true }).tap()
}
async function selectTemplate(page: Page) {
  await page.getByRole('button', { name: '选择标签模板', exact: true }).tap()
  await page.getByText('成品标签 · FINISHED', { exact: true }).tap()
  // NvPicker confirms its current selection explicitly.
  await page.getByRole('button', { name: '确定', exact: true }).tap()
}
function receipt(body: Record<string, unknown>, status = 'sent-to-printer', pending = false) {
  return {
    success: true,
    data: {
      productionReportId: 'report-serial',
      reportNo: 'RPT-SERIAL',
      serialNumbers: ['SN-0001', 'SN-0002'],
      printBatchId: 'batch-serial',
      printStatus: status,
      printingPreparationPending: pending,
      operationReceipt: productionReportReceipt('report-serial', String(body.idempotencyKey)),
    },
  }
}

test('完工报工刷新后只恢复原标签准备，不重新开放新报工', async ({ page }) => {
  const writes: Record<string, unknown>[] = []
  await page.route('**/mes/work-orders/WO-1?**', (route) => {
    if (!writes.length) return routeBusinessConsoleApi(route)
    return route.fulfill({
      json: {
        success: true,
        data: {
          ...mesWorkOrders[0],
          readinessStatus: 'ready',
          blockingReasons: [],
          operationTasks: [{ ...mesOperationTasks[0], status: 'Completed', allowedActions: [] }],
        },
      },
    })
  })
  await page.route(/\/mes\/(?:reportable-operation-tasks|operation-tasks)(?:\?|$)/, (route) => {
    if (!writes.length) return routeBusinessConsoleApi(route)
    const items = route.request().url().includes('/reportable-operation-tasks')
      ? []
      : [{ ...mesOperationTasks[0], status: 'Completed', allowedActions: [] }]
    return route.fulfill({ json: { success: true, data: { items, total: items.length } } })
  })
  // Reporting operators need no additional operations.read permission to recover a report.
  await page.route('**/mes/operation-tasks?**', (route) =>
    route.fulfill({ status: 403, json: { success: false, message: '没有工序执行读取权限' } }),
  )
  await page.route('**/mes/production-reports', (route) => {
    const body = route.request().postDataJSON()
    writes.push(body)
    return route.fulfill({
      json: receipt(
        body,
        writes.length === 1 ? 'reserved' : 'sent-to-printer',
        writes.length === 1,
      ),
    })
  })
  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await enterGood(page, '2')
  await selectTemplate(page)
  await page.getByTestId('completes-operation').check()
  await page.getByTestId('submit-report').tap()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await page.reload()
  await expect(page.getByTestId('retry-label-preparation')).toBeVisible()
  await page.getByTestId('retry-label-preparation').tap()
  await expect(page.getByText('已发送至打印机，请到现场核对出纸。')).toBeVisible()
  expect(writes).toHaveLength(2)
  expect(writes[1]).toEqual(writes[0])
  expect(writes[0].completesOperation).toBe(true)
  await page.reload()
  await expect(page.getByText('工序任务 OP-1 当前不可报工。')).toBeVisible()
  await expect(page.getByTestId('submit-report')).toHaveCount(0)
})

async function independentOrders(page: Page) {
  const tasks = [
    mesOperationTasks[0],
    {
      ...mesOperationTasks[0],
      workOrderId: 'WO-INDEPENDENT',
      operationTaskId: 'OP-NEW',
      operationSequence: 10,
    },
  ]
  const orders = [mesWorkOrders[0], { ...mesWorkOrders[0], workOrderId: 'WO-INDEPENDENT' }]
  await page.route(/\/mes\/work-orders(?:\?|$)/, (route) =>
    route.fulfill({ json: { success: true, data: { items: orders, total: 2 } } }),
  )
  await page.route(/\/mes\/work-orders\/(?:WO-1|WO-INDEPENDENT)(?:\?|$)/, (route) => {
    const id = new URL(route.request().url()).pathname.split('/').at(-1)
    return route.fulfill({
      json: {
        success: true,
        data: {
          ...orders.find((order) => order.workOrderId === id),
          readinessStatus: 'ready',
          blockingReasons: [],
          operationTasks: tasks.filter((task) => task.workOrderId === id),
        },
      },
    })
  })
  await page.route(/\/mes\/(?:reportable-operation-tasks|operation-tasks)(?:\?|$)/, (route) => {
    const query = new URL(route.request().url()).searchParams
    const items = tasks.filter(
      (task) =>
        (!query.get('operationTaskId') || task.operationTaskId === query.get('operationTaskId')) &&
        (!query.get('workOrderId') || task.workOrderId === query.get('workOrderId')),
    )
    return route.fulfill({ json: { success: true, data: { items, total: items.length } } })
  })
}

for (const reload of [false, true]) {
  test(`首次确定 400 释放占用，${reload ? '刷新后' : '当页纠正后'} A/B 可重新报工`, async ({
    page,
  }) => {
    await independentOrders(page)
    const writes: Record<string, unknown>[] = []
    await page.route('**/mes/production-reports', (route) => {
      const body = route.request().postDataJSON()
      writes.push(body)
      if (writes.length === 1)
        return route.fulfill({
          status: 400,
          json: { success: false, message: '标签模板已停用，请选择可用模板。' },
        })
      const response = receipt(body)
      if (body.workOrderId === 'WO-INDEPENDENT') {
        response.data.productionReportId = 'report-b'
        response.data.reportNo = 'RPT-B'
        response.data.operationReceipt = productionReportReceipt(
          'report-b',
          String(body.idempotencyKey),
        )
      }
      return route.fulfill({ json: response })
    })
    await page.route('**/mes/production-reports/RPT-B?**', (route) =>
      route.fulfill({
        json: {
          success: true,
          data: {
            report: {
              reportNo: 'RPT-B',
              productionReportId: 'report-b',
              workOrderId: 'WO-INDEPENDENT',
              operationTaskId: 'OP-NEW',
            },
          },
        },
      }),
    )
    await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
    await enterGood(page, '2')
    await selectTemplate(page)
    await page.getByTestId('submit-report').tap()
    await expect(page.getByText('标签模板已停用，请选择可用模板。', { exact: true })).toBeVisible()
    expect(writes).toHaveLength(1)
    if (reload) await page.reload()
    else await page.getByRole('button', { name: '修改后重新报工', exact: true }).tap()
    await expect(page.getByTestId('good-quantity')).toBeVisible()
    expect(writes).toHaveLength(1)
    await page.goto('/mes/report?workOrderId=WO-INDEPENDENT&operationTaskId=OP-NEW')
    await enterGood(page, '2')
    await selectTemplate(page)
    await expect(page.getByTestId('occupied-report')).toHaveCount(0)
    await expect(page.getByTestId('submit-report')).toBeEnabled()
    await page.getByTestId('submit-report').tap()
    await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
    expect(writes).toHaveLength(2)
    expect(writes[1].workOrderId).toBe('WO-INDEPENDENT')
    expect(writes[1].idempotencyKey).not.toBe(writes[0].idempotencyKey)
    await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
    await enterGood(page, '2')
    await selectTemplate(page)
    await page.getByTestId('submit-report').tap()
    await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
    expect(writes).toHaveLength(3)
    expect(writes[2].workOrderId).toBe('WO-1')
    expect(writes[2].idempotencyKey).not.toBe(writes[0].idempotencyKey)
  })
}

test('独立工单 A 占用时 B 可查看但零提交，只有 A 收敛后 B 才开始新意图', async ({ page }) => {
  await independentOrders(page)
  const writes: Record<string, unknown>[] = []
  await page.route('**/mes/production-reports', (route) => {
    const body = route.request().postDataJSON()
    writes.push(body)
    const response = receipt(
      body,
      writes.length === 1 ? 'reserved' : 'sent-to-printer',
      writes.length === 1,
    )
    if (body.workOrderId === 'WO-INDEPENDENT') {
      response.data.productionReportId = 'report-b'
      response.data.reportNo = 'RPT-B'
      response.data.operationReceipt = productionReportReceipt(
        'report-b',
        String(body.idempotencyKey),
      )
    }
    return route.fulfill({ json: response })
  })
  await page.route('**/mes/production-reports/RPT-B?**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: {
          report: {
            reportNo: 'RPT-B',
            productionReportId: 'report-b',
            workOrderId: 'WO-INDEPENDENT',
            operationTaskId: 'OP-NEW',
          },
        },
      },
    }),
  )
  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await enterGood(page, '2')
  await selectTemplate(page)
  await page.getByTestId('submit-report').tap()
  await expect(page.getByTestId('retry-label-preparation')).toBeVisible()
  await page.goto('/mes/report?workOrderId=WO-INDEPENDENT&operationTaskId=OP-NEW')
  await enterGood(page, '2')
  await selectTemplate(page)
  await expect(page.getByTestId('occupied-report')).toBeVisible()
  await expect(page.getByTestId('submit-report')).toBeDisabled()
  expect(writes).toHaveLength(1)
  await page.reload()
  await expect(page.getByTestId('return-to-preparation')).toBeVisible()
  await page.getByTestId('return-to-preparation').tap()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await page.getByTestId('retry-label-preparation').tap()
  await expect(page.getByTestId('continue-report')).toBeEnabled()
  expect(writes).toHaveLength(2)
  expect(writes[1]).toEqual(writes[0])
  await page.goto('/mes/report?workOrderId=WO-INDEPENDENT&operationTaskId=OP-NEW')
  await enterGood(page, '2')
  await selectTemplate(page)
  await page.getByTestId('submit-report').tap()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  expect(writes).toHaveLength(3)
  expect(writes[2].workOrderId).toBe('WO-INDEPENDENT')
  expect(writes[2].idempotencyKey).not.toBe(writes[0].idempotencyKey)
})

test('单手录入、模板选择和全部序列号；运输成功不等于出纸', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  const writes: Record<string, unknown>[] = []
  await page.route('**/mes/production-reports', (route) => {
    const body = route.request().postDataJSON()
    writes.push(body)
    return route.fulfill({ json: receipt(body) })
  })
  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByText('待分配序列号：0 个')).toBeVisible()
  await expect(page.getByTestId('good-quantity')).toHaveAttribute('readonly', '')
  await enterGood(page, '2')
  await expect(page.getByText('待分配序列号：2 个')).toBeVisible()
  await expect(page.getByTestId('submit-report')).toBeDisabled()
  expect(writes).toHaveLength(0)
  const templateButton = await page
    .getByRole('button', { name: '选择标签模板', exact: true })
    .boundingBox()
  expect(templateButton!.height).toBeGreaterThanOrEqual(44)
  await selectTemplate(page)
  await page.getByTestId('submit-report').tap()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await expect(page.getByRole('list', { name: '本次全部序列号' }).getByRole('listitem')).toHaveText(
    ['SN-0001', 'SN-0002'],
  )
  await expect(page.getByText('已发送至打印机，请到现场核对出纸。')).toBeVisible()
  await expect(page.locator('body')).not.toContainText('已打印')
  expect(writes[0]).toMatchObject({
    goodQuantity: 2,
    labelTemplateId: 'tpl-1',
    workOrderId: 'WO-1',
    operationTaskId: 'OP-1',
  })
  expect(writes[0]).not.toHaveProperty('serialNumbers')
  expect(writes[0]).not.toHaveProperty('serialTrackingPolicy')
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  const button = await page.getByTestId('continue-report').boundingBox()
  expect(button!.height).toBeGreaterThanOrEqual(44)
})

test('响应丢失和标签准备重试冻结同一键、时间、模板与数量', async ({ page }) => {
  const writes: Record<string, unknown>[] = []
  await page.route('**/mes/production-reports', (route) => {
    const body = route.request().postDataJSON()
    writes.push(body)
    if (writes.length === 1) return route.abort('failed')
    return route.fulfill({
      json: receipt(body, writes.length === 2 ? 'reserved' : 'failed', writes.length === 2),
    })
  })
  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByText('待分配序列号：0 个')).toBeVisible()
  await enterGood(page, '2')
  await selectTemplate(page)
  await page.getByTestId('submit-report').tap()
  await expect(page.getByTestId('retry-report')).toBeVisible()
  await page.getByTestId('retry-report').tap()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await page.getByTestId('retry-label-preparation').tap()
  await expect(
    page.getByText('打印发送失败，请联系现场打印负责人处理本批标签，勿重复报工。'),
  ).toBeVisible()
  expect(writes).toHaveLength(3)
  expect(writes[1]).toEqual(writes[0])
  expect(writes[2]).toEqual(writes[0])
})

test('模板不可用与权限失败给出可执行反馈并阻断报工', async ({ page }) => {
  await page.route('**/barcode/templates?**', (route) =>
    route.fulfill({ status: 403, json: { success: false, message: 'permission-denied' } }),
  )
  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByText('待分配序列号：0 个')).toBeVisible()
  await enterGood(page, '2')
  await expect(page.getByRole('button', { name: '重新核对产品与模板' })).toBeVisible()
  await expect(page.getByTestId('submit-report')).toBeDisabled()
  await page.route('**/barcode/templates?**', (route) =>
    route.fulfill({ json: { success: true, data: { templates: [], total: 0 } } }),
  )
  await page.getByRole('button', { name: '重新核对产品与模板' }).tap()
  await expect(page.getByText('没有可用标签模板，请联系标签管理员启用模板后重试。')).toBeVisible()
  await expect(page.getByTestId('submit-report')).toBeDisabled()
})

test('标签准备收敛前不开始新报工，失败仍保留成功，收敛后同产量使用新意图', async ({ page }) => {
  const writes: Record<string, unknown>[] = []
  await page.route('**/mes/production-reports', (route) => {
    const body = route.request().postDataJSON()
    writes.push(body)
    if (writes.length === 2) return route.abort('failed')
    const response = receipt(
      body,
      writes.length === 1 ? 'reserved' : 'sent-to-printer',
      writes.length === 1,
    )
    if (writes.length === 4) {
      response.data.productionReportId = 'report-next'
      response.data.reportNo = 'RPT-NEXT'
      response.data.serialNumbers = ['SN-0003', 'SN-0004']
      response.data.operationReceipt = productionReportReceipt(
        'report-next',
        String(body.idempotencyKey),
      )
    }
    return route.fulfill({ json: response })
  })
  await page.route('**/mes/production-reports/RPT-NEXT?**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: {
          report: {
            productionReportId: 'report-next',
            reportNo: 'RPT-NEXT',
            workOrderId: 'WO-1',
            operationTaskId: 'OP-1',
          },
        },
      },
    }),
  )
  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByText('待分配序列号：0 个')).toBeVisible()
  await enterGood(page, '2')
  await selectTemplate(page)
  await page.getByTestId('submit-report').tap()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await expect(page.getByTestId('continue-report')).toBeDisabled()
  await page.reload()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await expect(page.getByTestId('good-quantity')).toHaveCount(0)
  await expect(page.getByTestId('continue-report')).toBeDisabled()
  expect(writes).toHaveLength(1)
  await page.goto('/mes/report?workOrderId=WO-2&operationTaskId=OP-3')
  await expect(page.getByText('工序任务 OP-3 当前不可报工。')).toBeVisible()
  await expect(page.getByRole('heading', { name: '报工成功' })).toHaveCount(0)
  await expect(page.getByTestId('retry-label-preparation')).toHaveCount(0)
  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  expect(writes).toHaveLength(1)
  await page.getByTestId('retry-label-preparation').tap()
  await expect(page.getByTestId('label-preparation-error')).toBeVisible()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await expect(page.getByTestId('continue-report')).toBeDisabled()
  await expect(page.getByRole('list', { name: '本次全部序列号' }).getByRole('listitem')).toHaveText(
    ['SN-0001', 'SN-0002'],
  )
  await page.goto('/tasks')
  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await expect(page.getByTestId('continue-report')).toBeDisabled()
  expect(writes).toHaveLength(2)
  await page.getByTestId('retry-label-preparation').tap()
  await expect(page.getByTestId('continue-report')).toBeEnabled()
  expect(writes).toHaveLength(3)
  expect(writes[1]).toEqual(writes[0])
  expect(writes[2]).toEqual(writes[0])
  await page.getByTestId('continue-report').tap()
  await page.getByRole('button', { name: /^WO-1 已下达/ }).tap()
  await page.getByRole('button', { name: /WO-1 · 工序 10/ }).tap()
  await expect(page.getByText('待分配序列号：0 个')).toBeVisible()
  await enterGood(page, '2')
  await selectTemplate(page)
  await page.getByTestId('submit-report').tap()
  await expect(page.getByRole('heading', { name: '报工成功' })).toBeVisible()
  await expect(page.getByRole('list', { name: '本次全部序列号' }).getByRole('listitem')).toHaveText(
    ['SN-0003', 'SN-0004'],
  )
  expect(writes).toHaveLength(4)
  expect(writes[3].idempotencyKey).not.toBe(writes[0].idempotencyKey)
  expect(writes[3]).toMatchObject({
    workOrderId: 'WO-1',
    operationTaskId: 'OP-1',
    goodQuantity: 2,
    labelTemplateId: 'tpl-1',
  })
  await page.reload()
  await expect(page.getByTestId('good-quantity')).toBeVisible()
  await expect(page.getByTestId('retry-label-preparation')).toHaveCount(0)
  expect(writes).toHaveLength(4)
})
