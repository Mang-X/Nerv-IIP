import { expect, test } from '@playwright/test'

// #4460：真实页面、query/mutation 和 HTTP fixture；不证明真实后端、岗位授权或消息投影。
test('默认窗口显示页面打开后登记的同一停机，刷新和恢复一致，历史窗口固定（HTTP fixture）', async ({
  page,
}, testInfo) => {
  const principal = {
    principalId: 'leader-assembly-01',
    principalType: 'User',
    loginName: 'assembly.leader',
    organizationId: 'org-qj',
    environmentId: 'env-production',
    permissionVersion: 1,
    permissionCodes: [
      'business.mes.downtime.read',
      'business.mes.downtime.manage',
      'business.mes.operations.read',
      'business.mes.work-orders.read',
    ],
  }
  const session = {
    principal,
    accessToken: 'fixture-access',
    refreshToken: 'fixture-refresh',
    sessionId: 'assembly-leader',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
  }
  const scope = { kind: 'work-center', id: 'WC-ASM-01', displayName: '总成装配一线' }
  const task = {
    workOrderId: 'WO-QJ-1001',
    workOrderNo: 'WO-QJ-1001',
    operationTaskId: 'WO-QJ-1001-OP-10',
    operationTaskNo: 'WO-QJ-1001-OP-10',
    workCenterId: scope.id,
    workCenterName: scope.displayName,
    deviceAssetId: 'EQ-ASM-01',
    deviceAssetCode: 'EQ-ASM-01',
    deviceAssetName: '总成装配机',
    status: 'Queued',
  }
  const eventId = 'DOWNTIME-20260830-000001'
  let event:
    | (typeof task & {
        downtimeEventId: string
        reasonCode: string
        reasonName: string
        startedAtUtc: string
        recoveredAtUtc?: string
        status: string
      })
    | undefined
  const listQueries: Array<Record<string, string>> = []
  await page.clock.setFixedTime(new Date('2026-08-30T08:00:05.000Z'))
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route(/\/api\/(?:business-console|console)\/v[12]\//, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname
    const json = (data: unknown) => route.fulfill({ json: { success: true, data } })
    if (path.endsWith('/auth/refresh')) return json(session)
    if (path.endsWith('/auth/me')) return json(principal)
    if (path.endsWith('/me/work-context')) {
      return json({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolutionStatus: 'resolved',
        authorizedScopes: [scope],
        selectedScope: url.searchParams.has('scopeId') ? scope : null,
        availableScopeKinds: ['work-center'],
        issues: [],
      })
    }
    if (path.endsWith('/mes/operation-tasks')) return json({ items: [task], total: 1 })
    if (path.includes('/directories/downtime-reason')) {
      return json({ items: [{ code: 'DT-PM', displayName: '计划保养' }], total: 1 })
    }
    if (path.endsWith('/downtime-events') && request.method() === 'POST') {
      const body = request.postDataJSON()
      event = {
        ...task,
        downtimeEventId: eventId,
        reasonCode: body.reasonCode,
        reasonName: '计划保养',
        startedAtUtc: body.startedAtUtc,
        status: 'Open',
      }
      return json({ accepted: true, downstreamDocumentId: eventId })
    }
    if (path.endsWith(`/${eventId}/recover`)) {
      event = {
        ...event!,
        status: 'Recovered',
        recoveredAtUtc: request.postDataJSON().recoveredAtUtc,
      }
      return json({ accepted: true })
    }
    if (path.endsWith('/downtime-events')) {
      const query = Object.fromEntries(url.searchParams)
      listQueries.push(query)
      const items =
        event &&
        event.startedAtUtc >= query.windowStartUtc! &&
        event.startedAtUtc < query.windowEndUtc!
          ? [event]
          : []
      return json({ items, total: items.length, reasonSummary: [] })
    }
    return json({ items: [], total: 0 })
  })

  await page.goto('/mes/downtime?workOrderId=WO-QJ-1001&operationTaskId=WO-QJ-1001-OP-10')
  await expect(page.getByText('全部恢复', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '登记停机', exact: true })).toBeEnabled()
  expect(listQueries.at(-1)?.windowEndUtc).toBe('2026-08-30T08:00:05.000Z')
  await page.clock.setFixedTime(new Date('2026-08-30T08:02:05.000Z'))
  await page.getByRole('button', { name: '登记停机', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox').filter({ hasText: '选择停机原因' }).press('Space')
  await expect(page.getByRole('option', { name: '计划保养（DT-PM）', exact: true })).toBeVisible()
  await page.getByRole('option', { name: '计划保养（DT-PM）', exact: true }).click()
  await dialog.getByRole('button', { name: '确认登记', exact: true }).click()
  const row = page.getByRole('row').filter({ hasText: eventId })
  await expect(row).toContainText('未恢复')
  await expect(page.getByText('需跟进恢复', { exact: true })).toBeVisible()
  await expect(page.getByText('全部恢复', { exact: true })).toHaveCount(0)
  expect(event?.startedAtUtc).toBe('2026-08-30T08:02:00.000Z')
  expect(listQueries.at(-1)?.windowEndUtc).toBe('2026-08-30T08:02:05.000Z')

  await page.clock.setFixedTime(new Date('2026-08-30T08:03:05.000Z'))
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await expect.poll(() => listQueries.at(-1)?.windowEndUtc).toBe('2026-08-30T08:03:05.000Z')
  await expect(row).toContainText('未恢复')
  await page.screenshot({ path: testInfo.outputPath('open-after-refresh.png'), fullPage: true })
  await row.getByRole('button', { name: '恢复', exact: true }).click()
  await page.getByRole('button', { name: '确认恢复', exact: true }).click()
  await expect(row).toContainText('已恢复')
  await expect(page.getByText('全部恢复', { exact: true })).toBeVisible()
  await expect(
    page.getByRole('link', { name: '返回工单 WO-QJ-1001', exact: true }),
  ).toHaveAttribute('href', /work-orders\/WO-QJ-1001\?operationTaskId=WO-QJ-1001-OP-10/)

  await page.locator('[data-slot="nv-date-range-picker"] button').click()
  await page.getByRole('button', { name: '下个月', exact: true }).click()
  await page.getByRole('button', { name: '10', exact: true }).click()
  await page.getByRole('button', { name: '15', exact: true }).click()
  await expect(page.locator('[data-slot="nv-date-range-picker"]')).toContainText(
    '2026-08-10 ~ 2026-08-15',
  )
  await expect(row).toHaveCount(0)
  const historical = { ...listQueries.at(-1)! }
  await page.clock.setFixedTime(new Date('2026-08-31T09:00:00.000Z'))
  const refreshed = page.waitForResponse((response) =>
    response.url().includes('/mes/downtime-events?'),
  )
  await page.getByRole('button', { name: '刷新', exact: true }).click()
  await refreshed
  expect(listQueries.at(-1)).toEqual(historical)
  await page.screenshot({ path: testInfo.outputPath('historical-window.png'), fullPage: true })
  await testInfo.attach('list-query-evidence', {
    body: JSON.stringify({ listQueries, event }, null, 2),
    contentType: 'application/json',
  })
})
