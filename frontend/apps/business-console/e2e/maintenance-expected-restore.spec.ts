import { expect, test } from '@playwright/test'
import path from 'node:path'
import { requireBrowserEvidenceOutputDir } from '../playwright.config'

test('PC 预计恢复填写、权威读回、刷新、清除与拒绝反馈（HTTP fixture）', async ({ page }) => {
  const session = {
    principal: {
      principalId: 'maintenance-engineer',
      principalType: 'User',
      loginName: '张工',
      organizationId: 'org-a',
      environmentId: 'env-a',
      permissionVersion: 1,
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.maintenance.work-orders.manage',
      ],
    },
    accessToken: 'browser-fixture-access',
    refreshToken: 'browser-fixture-refresh',
    sessionId: 'maintenance-browser',
    expiresAtUtc: '2099-01-01T00:00:00Z',
  }
  const row = {
    workOrderId: 'wo-etr-01',
    sourceReferenceId: 'MWO-2026-0108',
    deviceAssetId: 'PRESS-01',
    priority: 'high',
    status: 'InProgress',
    assignedTechnicianUserId: 'maintenance-engineer',
    assignedTeamId: 'TEAM-A',
    version: 2,
    openedAtUtc: '2026-10-07T02:00:00Z',
    expectedRestoreAtUtc: null as string | null,
  }
  let reject = false
  const requests: Record<string, unknown>[] = []
  let detailReads = 0
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    if (!url.pathname.startsWith('/api/')) return route.continue()
    let data: unknown = { items: [], total: 0 }
    if (url.pathname.endsWith('/auth/refresh')) data = session
    else if (url.pathname.endsWith('/me/work-context')) {
      data = {
        authorizedScopes: [
          { kind: 'work-center', id: 'WC-B', displayName: '冲压中心' },
          { kind: 'team', id: 'TEAM-A', displayName: '维修一班' },
        ],
      }
    } else if (url.pathname.endsWith('/actions')) {
      const body = route.request().postDataJSON()
      requests.push(body)
      if (reject) {
        await route.fulfill({
          status: 409,
          json: { success: false, message: '工单已变化，请刷新后重试。' },
        })
        return
      }
      // 权威回读刻意与提交相差一分钟，证明页面消费服务端结果。
      row.expectedRestoreAtUtc = body.expectedRestoreAtUtc
        ? new Date(Date.parse(body.expectedRestoreAtUtc) + 60_000).toISOString()
        : null
      row.version += 1
      data = {
        workOrderId: row.workOrderId,
        version: row.version,
        operationReceipt: {
          operationType: 'maintenance.work-order.updateexpectedrestore',
          authority: 'maintenance',
          resourceType: 'maintenance-work-order',
          resourceId: row.workOrderId,
          idempotencyKey: body.idempotencyKey,
          outcome: 'confirmed',
          stateConfirmed: true,
          readbackRequired: false,
          changedAtUtc: '2026-10-07T02:10:00Z',
          resourceStatus: row.status,
        },
      }
    } else if (url.pathname.endsWith('/work-orders/wo-etr-01')) {
      detailReads += 1
      data = row
    } else if (url.pathname.endsWith('/maintenance/work-orders')) {
      data = { items: [row], total: 1, skip: 0, take: 100 }
    }
    await route.fulfill({ json: { success: true, data } })
  })
  await page.goto('/maintenance/work-orders')
  const open = async () => {
    await page.getByRole('button', { name: '维护工单操作 MWO-2026-0108' }).click()
    await page.getByRole('menuitem', { name: '更新预计恢复时间' }).click()
  }
  await open()
  await page.locator('#mwo-expected-restore').fill('2026-10-10T13:30')
  await page.getByRole('button', { name: '保存预计恢复时间' }).click()
  await expect(page.getByText('预计恢复时间已保存', { exact: true })).toBeVisible()
  expect(requests[0]).toMatchObject({
    action: 'updateExpectedRestore',
    expectedVersion: 2,
    scopeKind: 'team',
    scopeId: 'TEAM-A',
  })
  expect(detailReads).toBe(1)
  await page.reload()
  await open()
  await expect(page.locator('#mwo-expected-restore')).toHaveValue('2026-10-10T13:31')
  await page.screenshot({
    path: path.join(requireBrowserEvidenceOutputDir(), 'expected-restore-readback.png'),
    fullPage: true,
    animations: 'disabled',
  })
  reject = true
  await page.locator('#mwo-expected-restore').fill('2026-10-10T14:00')
  await page.getByRole('button', { name: '保存预计恢复时间' }).click()
  await expect(page.getByText('预计恢复时间保存失败', { exact: false }).first()).toBeVisible()
  await expect(page.locator('#mwo-expected-restore')).toHaveValue('2026-10-10T14:00')
  await page.screenshot({
    path: path.join(requireBrowserEvidenceOutputDir(), 'expected-restore-error.png'),
    fullPage: true,
    animations: 'disabled',
  })
  reject = false
  await page.locator('#mwo-expected-restore').fill('')
  await page.getByRole('button', { name: '保存预计恢复时间' }).click()
  await expect(page.locator('#mwo-expected-restore')).not.toBeVisible()
  expect(requests.at(-1)?.expectedRestoreAtUtc).toBeNull()
  await page.reload()
  await open()
  await expect(page.locator('#mwo-expected-restore')).toHaveValue('')
})
