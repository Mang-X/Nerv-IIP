import { expect, test } from '@playwright/test'
import { routeBusinessConsoleApi, routeConsoleApi, seedStoredSession } from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
  await page.route('**/api/business-console/v1/mes/operation-tasks**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: {
          items: [
            {
              workOrderId: 'WO-ANDON',
              operationTaskId: 'OP-ANDON',
              workOrderNo: 'MO-ANDON',
              operationSequence: 10,
              workCenterId: 'WC-A',
              status: 'Blocked',
              allowedActions: [],
              blockReasons: [
                'MATERIAL_SHORTAGE: 物料未齐套',
                'DEVICE_UNAVAILABLE: 设备不可用',
                'QUALITY_HOLD: 质量保留',
              ],
              evaluatedAtUtc: '2026-09-20T08:00:00Z',
            },
          ],
          total: 1,
        },
      },
    }),
  )
})

test('四类异常可从阻塞工序呼叫，失败重试沿用同一载荷并呈现服务端凭据', async ({ page }) => {
  const requests: Array<Record<string, unknown>> = []
  await page.route('**/api/business-console/v1/mes/andon-calls', async (route) => {
    const body = route.request().postDataJSON()
    requests.push(body)
    if (requests.length === 1) return route.abort('failed')
    return route.fulfill({
      json: {
        success: true,
        data: {
          id: `andon-${body.category}`,
          organizationId: body.organizationId,
          environmentId: body.environmentId,
          workOrderId: body.workOrderId,
          operationTaskId: body.operationTaskId,
          workCenterId: body.workCenterId,
          category: body.category,
          status: 'open',
          callerId: 'principal-1',
          raisedAtUtc: '2026-09-20T08:00:00Z',
          responderId: null,
          firstRespondedAtUtc: null,
          responseDurationSeconds: null,
          closedAtUtc: null,
          escalatedAtUtc: null,
          escalationRecipientId: null,
        },
      },
    })
  })
  await page.goto('/mes/operation?workOrderId=WO-ANDON&operationTaskId=OP-ANDON')
  const panel = page.getByRole('region', { name: '异常呼叫' })
  await panel.getByRole('button', { name: '缺料呼叫' }).click()
  await panel.getByRole('button', { name: '发起呼叫', exact: true }).click()
  await expect(panel).toContainText('结果待核实')
  await expect(panel.getByRole('button', { name: '设备呼叫' })).toBeDisabled()
  await expect(panel).not.toContainText('呼叫已确认')
  await panel.getByRole('button', { name: '重试原呼叫' }).click()
  await expect(panel).toContainText('andon-materialShortage')
  expect(requests[1]).toEqual(requests[0])
  expect(requests[0]).toMatchObject({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    scopeKind: 'work-center',
    scopeId: 'WC-A',
    workOrderId: 'WO-ANDON',
    operationTaskId: 'OP-ANDON',
    workCenterId: 'WC-A',
  })
  for (const [label, category] of [
    ['设备呼叫', 'equipment'],
    ['质量呼叫', 'quality'],
    ['工艺呼叫', 'process'],
  ]) {
    await panel.getByRole('button', { name: '发起另一笔呼叫' }).click()
    await panel.getByRole('button', { name: label }).click()
    await panel.getByRole('button', { name: '发起呼叫', exact: true }).click()
    await expect(panel).toContainText(`andon-${category}`)
  }
  expect(requests).toHaveLength(5)
  expect(new Set(requests.map((body) => body.idempotencyKey)).size).toBe(4)
  await expect(page.getByTestId('action-start')).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  )
})

test('呼叫权限被拒绝时不发起业务写', async ({ page }) => {
  const writes: string[] = []
  await page.route('**/api/business-console/v1/me/work-context**', (route) => {
    if (
      new URL(route.request().url()).searchParams.get('permissionCode') ===
      'business.mes.operations.manage'
    ) {
      return route.fulfill({ status: 403, json: { success: false, message: '无操作权限' } })
    }
    return routeBusinessConsoleApi(route)
  })
  await page.route('**/api/business-console/v1/mes/andon-calls', (route) => {
    writes.push(route.request().url())
    return route.fulfill({ status: 403 })
  })
  await page.goto('/mes/operation')
  await page.getByText('MO-ANDON · 工序 10', { exact: true }).click()
  const panel = page.getByRole('region', { name: '异常呼叫' })
  await expect(panel.getByRole('button', { name: '缺料呼叫' })).toBeDisabled()
  await expect(panel).toContainText('作业范围核验失败')
  expect(writes).toHaveLength(0)
})
