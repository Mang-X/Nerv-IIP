import { expect, test } from '@playwright/test'
import { requireBrowserEvidenceOutputDir } from '../playwright.config'

// #4074：仅用 HTTP fixture 证明业务台交互，不声明真实后端或 full-chain。
const principal = {
  principalId: 'planner-1',
  principalType: 'User',
  loginName: '计划员',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionCodes: ['notifications.messages.read'],
}
const session = {
  principal,
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00Z',
}

for (const writeFails of [false, true]) {
  test(`通知收件箱${writeFails ? '保留失败操作前状态' : '筛选、详情与已读数同步'}`, async ({
    page,
  }, testInfo) => {
    let read = false
    let writes = 0
    await page.addInitScript(
      (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
      session,
    )
    await page.route('**/api/console/v1/**', (route) =>
      route.fulfill({ json: { success: true, data: session } }),
    )
    await page.route('**/api/business-console/v1/**', async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      if (url.pathname.endsWith('/notifications/messages')) {
        expect(url.searchParams.get('organizationId')).toBe('org-001')
        expect(url.searchParams.get('environmentId')).toBe('env-dev')
        await route.fulfill({
          json: {
            success: true,
            data: {
              items: [
                {
                  messageId: 'msg-plan',
                  title: '排产计划已发布',
                  summary: '总装一线明日排产已发布，请确认物料准备。',
                  status: read ? 'read' : 'unread',
                  readAtUtc: read ? '2026-09-30T01:00:00Z' : null,
                  resource: {
                    resourceType: 'schedule-plan',
                    resourceId: 'SCH-2026-0930',
                    fileId: 'FILE-0930',
                  },
                  createdAtUtc: '2026-09-30T00:00:00Z',
                },
                {
                  messageId: 'msg-approval',
                  title: '采购审批完成',
                  status: 'read',
                  resource: { resourceType: 'approval-chain', resourceId: 'AP-2026-0930' },
                  createdAtUtc: '2026-09-29T00:00:00Z',
                },
              ],
            },
          },
        })
      } else if (url.pathname.endsWith('/notifications/messages/msg-plan/read')) {
        writes++
        expect(request.method()).toBe('POST')
        expect(request.postDataJSON()).toEqual({
          organizationId: 'org-001',
          environmentId: 'env-dev',
        })
        if (!writeFails) read = true
        await route.fulfill({
          json: writeFails
            ? { success: false, message: '无法标记该通知' }
            : {
                success: true,
                data: { messageId: 'msg-plan', status: 'read', readAtUtc: '2026-09-30T01:00:00Z' },
              },
        })
      } else await route.fulfill({ json: { success: true, data: { items: [] } } })
    })
    await page.goto('/')
    await page.getByRole('button', { name: '通知收件箱，1 条未读' }).click()
    const inbox = page.getByRole('dialog', { name: '通知收件箱' })
    await expect(inbox.getByText('采购审批完成')).toBeVisible()
    await inbox.getByRole('combobox', { name: '业务类型' }).selectOption('schedule-plan')
    await expect(inbox.getByText('采购审批完成')).toHaveCount(0)
    await inbox.getByRole('button', { name: '排产计划已发布', exact: true }).click()
    const detail = inbox.getByRole('region', { name: '通知详情' })
    await expect(detail.getByText('SCH-2026-0930')).toBeVisible()
    await expect(detail.getByText('FILE-0930')).toBeVisible()
    await expect(detail.getByText('总装一线明日排产已发布，请确认物料准备。')).toBeVisible()
    await expect(page).toHaveURL(/\/$/)
    await detail.getByRole('button', { name: '标记已读', exact: true }).click()
    if (writeFails) {
      await expect(inbox.getByRole('alert')).toContainText('无法标记该通知')
      await expect(detail.getByText('未读', { exact: true })).toBeVisible()
      await expect(inbox.getByText('1 条未读通知')).toBeVisible()
    } else {
      await expect(inbox.getByText('0 条未读通知')).toBeVisible()
      await expect(detail.getByText('已读', { exact: true })).toBeVisible()
      await expect(inbox.getByRole('button', { name: '标记已读', exact: true })).toHaveCount(0)
      await page.screenshot({
        path: `${requireBrowserEvidenceOutputDir()}/${testInfo.project.name}-inbox.png`,
      })
    }
    expect(writes).toBe(1)
    await expect(page).toHaveURL(/\/$/)
    await inbox.getByRole('button', { name: 'Close', exact: true }).click()
    await expect(
      page.getByRole('button', { name: `通知收件箱，${writeFails ? 1 : 0} 条未读` }),
    ).toBeVisible()
  })
}
