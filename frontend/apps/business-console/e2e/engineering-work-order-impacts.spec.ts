import { expect, test } from '@playwright/test'

const changeNumber = 'ECO/2602-01'
const scheduledNumber = 'ECO-2602-02'
const workOrderId = 'WO-2602-11'
const reason = '已完成首件检验'
const actor = '工艺工程师'
const decidedAtUtc = '2026-02-10T08:00:00Z'

test('工程变更与工单双向定位，保存后重新读取决策，计划生效操作可访问（API fixture）', async ({
  page,
}) => {
  const principal = {
    principalId: 'engineering-operator',
    principalType: 'User',
    loginName: 'operator',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: [
      'business.engineering.changes.read',
      'business.engineering.changes.manage',
      'business.mes.work-orders.read',
      'business.mes.work-orders.manage',
    ],
  }
  const session = {
    principal,
    accessToken: 'mock-access',
    refreshToken: 'mock-refresh',
    sessionId: 'mock-session',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
  }
  await page.addInitScript((value) => {
    if (sessionStorage.getItem('engineering-fixture-read-only'))
      value.principal.permissionCodes = ['business.mes.work-orders.read']
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(value))
  }, session)

  const impactReads: URLSearchParams[] = []
  const decisionWrites: unknown[] = []
  let saved = false
  const published = { changeNumber, status: 'Published', reason: '更新精加工夹具' }
  const scheduled = {
    changeNumber: scheduledNumber,
    status: 'Scheduled',
    reason: '更新总装路线',
    effectiveDate: '2099-01-01',
  }
  await page.route(/\/api\/(?:business-console|console)\/v1\//, async (route) => {
    const request = route.request()
    const { pathname, searchParams } = new URL(request.url())
    const json = (data: unknown) => route.fulfill({ json: { success: true, data } })
    if (pathname.endsWith('/auth/me')) return json(principal)
    if (pathname.endsWith('/auth/refresh')) return json(session)
    if (pathname.endsWith('/me/work-context')) {
      const scope = { kind: 'organization', id: principal.organizationId, displayName: '一号工厂' }
      return json({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        applicablePermissionCode: searchParams.get('permissionCode'),
        principal: { id: principal.principalId, principalType: principal.principalType },
        resolutionStatus: 'resolved',
        authorizedScopes: [scope],
        availableScopeKinds: ['organization'],
        selectedScope: scope,
      })
    }
    if (pathname.endsWith('/engineering/engineering-changes'))
      return json({ items: [scheduled], total: 1 })
    if (decodeURIComponent(pathname).endsWith(`/engineering/engineering-changes/${changeNumber}`))
      return json(published)
    if (pathname.endsWith(`/engineering/engineering-changes/${scheduledNumber}`))
      return json(scheduled)
    if (pathname.endsWith(`/mes/work-orders/${workOrderId}`))
      return json({
        workOrderId,
        workOrderNo: workOrderId,
        skuId: 'SKU-HOUSING',
        status: 'started',
      })
    if (pathname.endsWith(`/mes/work-orders/${workOrderId}/engineering-change-decisions`)) {
      decisionWrites.push(request.postDataJSON())
      saved = true
      return json({})
    }
    if (pathname.endsWith('/mes/work-order-engineering-change-impacts')) {
      impactReads.push(searchParams)
      const target = {
        changeNumber,
        workOrderId,
        disposition: saved ? 'continue-with-archived-version' : 'pending-decision',
        ...(saved ? { decidedBy: actor, decidedAtUtc, decisionReason: reason } : {}),
      }
      if (searchParams.has('workOrderId')) return json({ items: [target], total: 1 })
      const items = Array.from({ length: 10 }, (_, index) => ({
        changeNumber,
        workOrderId: `WO-2602-${String(index + 1).padStart(2, '0')}`,
        disposition: 'auto-rebound',
      }))
      return json({ items: searchParams.get('skip') === '10' ? [target] : items, total: 11 })
    }
    return json({ items: [], total: 0 })
  })

  const ecoPath = '/engineering/eco?changeNumber=ECO%2F2602-01'
  await page.goto(ecoPath, { waitUntil: 'domcontentloaded' })
  const ecoImpacts = page.getByRole('region', { name: '在制影响' })
  await expect(ecoImpacts.getByRole('link', { name: 'WO-2602-01', exact: true })).toBeVisible()
  await ecoImpacts.getByRole('button', { name: '下一页' }).click()
  const targetOrder = ecoImpacts.getByRole('link', { name: workOrderId, exact: true })
  await expect(targetOrder).toBeVisible()
  expect(
    impactReads.map((query) => [query.get('changeNumber'), query.get('skip'), query.get('take')]),
  ).toEqual([
    [changeNumber, '0', '10'],
    [changeNumber, '10', '10'],
  ])
  await targetOrder.click()
  await expect(page).toHaveURL(new RegExp(`/mes/work-orders/${workOrderId}$`))

  const workOrderImpacts = page.getByRole('region', { name: '工程变更影响' })
  const ecoLink = workOrderImpacts.getByRole('link', { name: changeNumber, exact: true })
  await expect(ecoLink).toBeVisible()
  await expect(workOrderImpacts.getByText('待确认', { exact: true })).toBeVisible()
  await page.getByTestId('record-engineering-change-decision').click()
  await page.locator('#engineering-change-number').fill(changeNumber)
  await page.locator('#engineering-change-decision').click()
  await page.getByRole('option', { name: '继续使用现行版本' }).click()
  await page.locator('#engineering-change-reason').fill(reason)
  await page.getByRole('button', { name: '保存决策' }).click()
  await expect(workOrderImpacts.getByText(reason, { exact: true })).toBeVisible()
  expect(decisionWrites).toEqual([
    expect.objectContaining({ changeNumber, decision: 'continue-with-archived-version', reason }),
  ])

  await page.reload({ waitUntil: 'domcontentloaded' })
  await expect(workOrderImpacts.getByText('继续旧版', { exact: true })).toBeVisible()
  await expect(workOrderImpacts.getByText(actor, { exact: true })).toBeVisible()
  await expect(workOrderImpacts.getByText(reason, { exact: true })).toBeVisible()
  await expect(workOrderImpacts.locator('time')).toHaveAttribute('datetime', decidedAtUtc)
  await ecoLink.click()
  await expect(page).toHaveURL((url) => url.pathname + url.search === ecoPath)
  await expect(page.getByText(`${changeNumber} · 更新精加工夹具`, { exact: true })).toBeVisible()

  await page.goto(`/engineering/eco?changeNumber=${scheduledNumber}`, {
    waitUntil: 'domcontentloaded',
  })
  const detail = page.getByRole('dialog', { name: '工程变更详情' })
  await expect(detail.getByText('计划生效', { exact: true })).toBeVisible()
  await detail.getByRole('button', { name: '改期', exact: true }).click()
  await expect(page.getByRole('dialog', { name: '确认工程变更改期' })).toBeVisible()
  await page.keyboard.press('Escape')
  await detail.getByRole('button', { name: '取消调度', exact: true }).click()
  await expect(page.getByRole('dialog', { name: '确认取消工程变更调度' })).toBeVisible()
  expect(impactReads.some((query) => query.get('changeNumber') === scheduledNumber)).toBe(false)

  principal.permissionCodes = ['business.mes.work-orders.read']
  await page.evaluate(() => sessionStorage.setItem('engineering-fixture-read-only', '1'))
  const readsBeforeReadOnly = impactReads.length
  await page.goto(`/mes/work-orders/${workOrderId}`, { waitUntil: 'domcontentloaded' })
  await expect(page.getByRole('link', { name: `工单 ${workOrderId}`, exact: true })).toBeVisible()
  await expect(page.getByText('已开工', { exact: true })).toBeVisible()
  await expect(workOrderImpacts).toHaveCount(0)
  await expect(page.getByTestId('record-engineering-change-decision')).toHaveCount(0)
  expect(impactReads).toHaveLength(readsBeforeReadOnly)
  expect(decisionWrites).toHaveLength(1)
})
