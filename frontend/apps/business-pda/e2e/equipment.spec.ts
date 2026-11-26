import { test, expect } from '@playwright/test'
import { routeConsoleApi, routeBusinessConsoleApi, seedStoredSession } from './fixtures'
import { registerSelfLookup } from './equipment/selfLookup'
import { registerSelfReadback } from './equipment/selfReadback'
import { registerRepair } from './equipment/repair'
import { registerInspection } from './equipment/inspection'
import { registerAlarmToRepair } from './equipment/alarmToRepair'

// 网关 Mock + 已登录主体（含 org/env + loginName，见 fixtures.principal）。
test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await page.route('**/api/business-console/v2/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

registerSelfLookup()

registerSelfReadback()

registerRepair()

registerInspection()

registerAlarmToRepair()

test('首页 → 报修：点应用墙「报修」跳 /equipment/repair', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('home-name')).toBeVisible()

  await page.getByRole('button', { name: '报修' }).click()
  await expect(page).toHaveURL('/equipment/repair')
  await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()
})

test.describe('维修动作', () => {
  test.use({ viewport: { width: 375, height: 812 } })

  test('完成开工中的工单并读取服务端时间线', async ({ page }) => {
    const { principal } = await import('./fixtures')
    const workOrderId = '019f1000-0000-7000-8000-000000000099'
    const changedAtUtc = '2026-08-02T01:30:00.000Z'
    const path = `/api/business-console/v1/maintenance/work-orders/${workOrderId}`
    let completed = false
    let readCount = 0
    await page.route(`**${path}**`, async (route) => {
      const request = route.request()
      if (request.method() === 'GET') {
        readCount++
        return route.fulfill({
          json: {
            success: true,
            data: {
              workOrderId,
              deviceAssetId: 'DEV-CNC-01',
              priority: 'high',
              status: completed ? 'Completed' : 'InProgress',
              openedAtUtc: '2026-08-02T01:00:00.000Z',
              version: completed ? 4 : 3,
              allowedActions: completed ? [] : ['complete'],
              blockReasons: [],
              lifecycle: completed
                ? [
                    {
                      action: 'complete',
                      fromStatus: 'InProgress',
                      toStatus: 'Completed',
                      actorPrincipalId: principal.principalId,
                      technicianUserId: principal.principalId,
                      teamId: null,
                      resultingVersion: 4,
                      occurredAtUtc: changedAtUtc,
                      reason: '更换传感器',
                    },
                  ]
                : [],
              assignedTechnicianUserId: principal.principalId,
              assignedTeamId: null,
            },
          },
        })
      }

      expect(new URL(request.url()).pathname).toBe(`${path}/actions`)
      expect(readCount).toBeGreaterThanOrEqual(2)
      const body = request.postDataJSON()
      expect(body).toMatchObject({
        action: 'complete',
        expectedVersion: 3,
        reason: '更换传感器',
        result: '复测正常',
        downtimeReasonCode: 'DT-MECH',
        downtimeMinutes: 15,
        actualLaborMinutes: 20,
        scopeKind: 'self',
        scopeId: principal.principalId,
      })
      expect(body.idempotencyKey).toBeTruthy()
      completed = true
      return route.fulfill({
        json: {
          success: true,
          data: {
            operationReceipt: {
              operationType: 'maintenance.work-order.complete',
              authority: 'business-gateway',
              resourceType: 'maintenance-work-order',
              resourceId: workOrderId,
              outcome: 'confirmed',
              stateConfirmed: true,
              readbackRequired: false,
              resourceStatus: 'Completed',
              changedAtUtc,
              idempotencyKey: body.idempotencyKey,
            },
          },
        },
      })
    })

    await page.goto(`/equipment/work-orders/${workOrderId}`)
    await expect(page.getByRole('button', { name: '开工', exact: true })).toHaveCount(0)
    await page.getByRole('button', { name: '完工', exact: true }).click()
    await expect(page.getByLabel('处理结果')).toHaveValue('')
    await expect(page.getByLabel('停机时长（分钟）')).toHaveValue('')
    await page.getByLabel('操作原因').fill('更换传感器')
    await page.getByLabel('处理结果').fill('复测正常')
    await page.getByLabel('停机原因编码').fill('DT-MECH')
    await page.getByLabel('停机时长（分钟）').fill('15')
    await page.getByLabel('实际工时（分钟）').fill('20')
    await page.getByRole('button', { name: '提交完工' }).click()
    await expect(page.getByText('操作已确认。')).toBeVisible()
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('更换传感器')
    await expect(page.getByRole('button', { name: '完工', exact: true })).toHaveCount(0)
  })
})
