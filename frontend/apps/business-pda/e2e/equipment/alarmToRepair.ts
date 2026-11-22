import { test, expect } from '@playwright/test'
import { CREATED_MAINTENANCE_WORK_ORDER_ID, principal } from '../fixtures'

export function registerAlarmToRepair() {
  test('报警 → 报修 → 已确认强 ID 详情：真实入口保留上下文并按 Self 重校验', async ({ page }) => {
    const sourceAlarmId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
    const detailRequests: URL[] = []
    let createdWorkOrderAssigned = false
    await page.route(
      `**/api/business-console/v1/maintenance/work-orders/${CREATED_MAINTENANCE_WORK_ORDER_ID}**`,
      async (route) => {
        const url = new URL(route.request().url())
        detailRequests.push(url)
        if (!createdWorkOrderAssigned) {
          await route.fulfill({
            status: 403,
            contentType: 'application/json',
            body: JSON.stringify({
              success: false,
              message: '新建工单尚未指派给当前维修人员',
              data: null,
            }),
          })
          return
        }
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              workOrderId: CREATED_MAINTENANCE_WORK_ORDER_ID,
              sourceReferenceId: 'MWO-2026-CREATED',
              deviceAssetId: 'DEV-A',
              priority: 'high',
              status: 'open',
              openedAtUtc: '2026-08-02T02:00:00.000Z',
              version: 1,
              allowedActions: ['accept'],
              blockReasons: [],
              lifecycle: [],
              assignedTechnicianUserId: principal.principalId,
              assignedTeamId: null,
              sourceAlarmId,
            },
          }),
        })
      },
    )
    await page.goto('/equipment/alarms')
    await expect(page.getByRole('heading', { name: '查看报警' })).toBeVisible()

    // 报警行渲染：设备 + 报警码 + 级别中文（严重，而非工程语言 'critical'）
    await expect(page.getByText('DEV-A · 报警码 E-101')).toBeVisible()
    await expect(page.getByText('严重')).toBeVisible()
    await expect(page.getByText('critical')).toHaveCount(0)

    // 去报修承载在行详情抽屉内（MAN-456 从行内移入详情）：先开详情再点。
    await page.getByTestId('detail-ALM-1').click()
    await page.getByTestId('repair-ALM-1').click()
    await expect(page).toHaveURL(/\/equipment\/repair\?/)
    const url = new URL(page.url())
    expect(url.pathname).toBe('/equipment/repair')
    expect(url.searchParams.get('deviceAssetId')).toBe('DEV-A')
    expect(url.searchParams.get('sourceAlarmId')).toBe('ALM-1')

    // 穿透后报修页设备已预填
    await expect(page.getByTestId('device-trigger')).toContainText('DEV-A')
    await expect(page.getByTestId('device-trigger')).toContainText('由报警发起报修')

    await page.getByTestId('priority-trigger').click()
    await page.getByRole('button', { name: '高', exact: true }).click()
    await page.getByTestId('submit').click()
    await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
    await expect(page.getByTestId('created-work-order-assignment-state')).toContainText(
      '工单指派状态暂不可核实',
    )
    await expect(page.getByTestId('view-created-work-order')).toHaveCount(0)

    // 创建回执真实保持未指派；此处模拟外部派工，再由用户显式回读 Self 详情。
    createdWorkOrderAssigned = true
    await page.getByTestId('recheck-created-work-order-assignment').click()
    await expect(page.getByTestId('created-work-order-assignment-state')).toContainText(
      '已确认工单指派给当前维修人员',
    )
    await expect(page.getByTestId('view-created-work-order')).toBeVisible()
    await page.getByTestId('view-created-work-order').click()

    await expect(page).toHaveURL(
      new RegExp(`/equipment/work-orders/${CREATED_MAINTENANCE_WORK_ORDER_ID}\\?`),
    )
    const detailUrl = new URL(page.url())
    expect(detailUrl.searchParams.get('source')).toBeNull()
    await page.goto(`${detailUrl.pathname}?sourceAlarmId=${sourceAlarmId.toUpperCase()}`)
    expect(new URL(page.url()).searchParams.get('sourceAlarmId')).toBe(sourceAlarmId.toUpperCase())
    await expect(page.getByTestId('maintenance-source-context')).toHaveText('由报警报修创建')
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('装配线冲压机')
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText(
      'WS-1 · LINE-A · ST-1',
    )
    expect(detailRequests.length).toBeGreaterThanOrEqual(2)
    for (const request of detailRequests) {
      expect(request.searchParams.get('scopeKind')).toBe('self')
      expect(request.searchParams.get('scopeId')).toBe(principal.principalId)
    }
  })
}
