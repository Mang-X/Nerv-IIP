import { expect, test, type Route } from '@playwright/test'

const principal = {
  principalId: 'responder-chen',
  principalType: 'User',
  loginName: '班长陈工',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: [
    'business.mes.operations.read',
    'business.mes.operations.manage',
    'business.mes.work-orders.read',
  ],
}
const session = {
  principal,
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'andon-session',
  expiresAtUtc: '2099-01-01T00:00:00Z',
}
const envelope = (data: unknown) => ({ success: true, data })
const fulfill = (route: Route, data: unknown, status = 200) =>
  route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) })

test('安灯服务端队列、分页、认领冲突与本人关闭，保存实际构建界面', async ({ page }, testInfo) => {
  const requests: URL[] = []
  let readFailed = true
  let claimed = false
  let closed = false
  let conflict = true
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname.endsWith('/auth/refresh')) return fulfill(route, envelope(session))
    if (url.pathname.endsWith('/auth/me')) return fulfill(route, envelope(principal))
    if (url.pathname.endsWith('/me/work-context'))
      return fulfill(
        route,
        envelope({
          authorizedScopes: [
            { kind: 'work-center', id: 'WC-A', displayName: '总装一线' },
            { kind: 'work-center', id: 'WC-B', displayName: '总装二线' },
          ],
          selectedScope: url.searchParams.has('scopeId')
            ? { kind: url.searchParams.get('scopeKind'), id: url.searchParams.get('scopeId') }
            : null,
        }),
      )
    if (url.pathname.endsWith('/andon-calls/call-1/claim')) {
      expect(route.request().postDataJSON()).not.toHaveProperty('responderId')
      conflict = false
      return fulfill(route, { success: false, message: '呼叫已由其他人员认领' }, 409)
    }
    if (url.pathname.endsWith('/andon-calls/call-2/claim')) {
      expect(route.request().postDataJSON()).toMatchObject({
        scopeKind: 'work-center',
        scopeId: 'WC-B',
      })
      claimed = true
      return fulfill(
        route,
        envelope({ id: 'call-2', status: 'claimed', responderId: `user:${principal.principalId}` }),
      )
    }
    if (url.pathname.endsWith('/andon-calls/call-2/close')) {
      expect(route.request().postDataJSON()).toMatchObject({
        scopeKind: 'work-center',
        scopeId: 'WC-B',
      })
      closed = true
      return fulfill(route, envelope({ id: 'call-2', status: 'closed' }))
    }
    if (url.pathname.endsWith('/andon-calls')) {
      expect(url.searchParams.get('scopeKind')).toBe('work-center')
      expect(['WC-A', 'WC-B']).toContain(url.searchParams.get('scopeId'))
      requests.push(url)
      if (readFailed) {
        readFailed = false
        return fulfill(route, { success: false, message: '服务暂时不可用，请稍后重试。' }, 503)
      }
      const rows = Array.from({ length: 21 }, (_, i) => ({
        id: `call-${i + 1}`,
        category: i < 2 ? 'equipment' : 'quality',
        status:
          i === 0 && !conflict
            ? 'claimed'
            : i === 1 && closed
              ? 'closed'
              : i === 1 && claimed
                ? 'claimed'
                : 'open',
        workOrderId: `WO-20260920-${String(i + 1).padStart(3, '0')}`,
        operationTaskId: `WO-20260920-${String(i + 1).padStart(3, '0')}-OP-20`,
        workCenterId: url.searchParams.get('scopeId'),
        raisedAtUtc: '2026-09-20T04:00:00Z',
        responderId:
          i === 0 && !conflict
            ? 'user:responder-li'
            : i === 1 && claimed
              ? `user:${principal.principalId}`
              : null,
        responseDurationSeconds: i === 0 && !conflict ? 100 : i === 1 && claimed ? 125 : null,
        escalatedAtUtc: i === 0 ? '2026-09-20T04:05:00Z' : null,
        escalationRecipientId: i === 0 ? '设备值班员' : null,
      })).filter(
        (row) =>
          (!url.searchParams.has('category') ||
            row.category === url.searchParams.get('category')) &&
          (url.searchParams.get('queue') === 'all' ||
            (url.searchParams.get('queue') === 'awaitingResponse'
              ? row.status === 'open'
              : row.status !== 'closed')),
      )
      const skip = Number(url.searchParams.get('skip'))
      const take = Number(url.searchParams.get('take'))
      return fulfill(route, envelope({ items: rows.slice(skip, skip + take), total: rows.length }))
    }
    return fulfill(route, envelope({ items: [], total: 0 }))
  })
  await page.goto('/mes/andon', { waitUntil: 'domcontentloaded' })
  await expect(page.getByText('数据加载失败', { exact: true })).toBeVisible()
  await expect(page.getByText('暂无安灯呼叫')).toBeHidden()
  await page.getByRole('button', { name: '重新加载', exact: true }).click()
  await expect(page.getByRole('link', { name: 'WO-20260920-001-OP-20', exact: true })).toBeVisible()
  await expect(page.getByText('未响应', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('已升级', { exact: true })).toBeVisible()
  await page.getByRole('combobox', { name: '作业范围' }).click()
  await page.getByRole('option', { name: '总装二线（工作中心）', exact: true }).click()
  await expect(page.getByText('WC-B', { exact: true }).first()).toBeVisible()
  await page.getByRole('button', { name: '下一页', exact: true }).click()
  await expect(page.getByRole('link', { name: 'WO-20260920-011-OP-20', exact: true })).toBeVisible()
  await expect(page).toHaveURL(/page=2/)
  await page.getByRole('link', { name: 'WO-20260920-011-OP-20', exact: true }).click()
  await expect(page).toHaveURL(/work-orders\/WO-20260920-011/)
  await page.goBack()
  await expect(page).toHaveURL(/page=2/)
  await expect(page.getByRole('link', { name: 'WO-20260920-011-OP-20', exact: true })).toBeVisible()
  await page.getByRole('combobox', { name: '呼叫分类' }).click()
  await page.getByRole('option', { name: '设备', exact: true }).click()
  await expect(page.getByRole('link', { name: 'WO-20260920-001-OP-20', exact: true })).toBeVisible()
  await page.getByRole('combobox', { name: '呼叫队列' }).click()
  await page.getByRole('option', { name: '待响应与处理中', exact: true }).click()
  await page.getByRole('button', { name: '认领', exact: true }).first().click()
  await expect(page.getByText('认领失败：呼叫已由其他人员认领', { exact: true })).toBeVisible()
  await expect(page.getByText('user:responder-li', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: '认领', exact: true }).click()
  await expect(page.getByText('125 秒', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '关闭呼叫', exact: true })).toBeVisible()
  const source = page.getByRole('link', { name: 'WO-20260920-002-OP-20', exact: true })
  await expect(source).toHaveAttribute(
    'href',
    '/mes/work-orders/WO-20260920-002?operationTaskId=WO-20260920-002-OP-20',
  )
  await page.screenshot({ path: testInfo.outputPath('andon-processing.png'), fullPage: true })
  await page.getByRole('button', { name: '关闭呼叫', exact: true }).click()
  await expect(page.getByText('安灯呼叫已关闭。', { exact: true })).toBeVisible()
  expect(
    requests.some(
      (url) => url.searchParams.get('skip') === '10' && url.searchParams.get('take') === '10',
    ),
  ).toBe(true)
  expect(
    requests.some(
      (url) =>
        url.searchParams.get('category') === 'equipment' && url.searchParams.get('skip') === '0',
    ),
  ).toBe(true)
  expect(requests.some((url) => url.searchParams.get('queue') === 'unclosed')).toBe(true)
  expect(pageErrors).toEqual([])
})
