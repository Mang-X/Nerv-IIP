import { expect, test } from '@playwright/test'

// 真实页面 + 公开 Gateway 契约 fixture；不证明真实主体授权或业务数据同一性。
test('追溯工单与工序经公开详情读面接力并返回原查询（fixture）', async ({ page }, testInfo) => {
  const workOrderId = 'WO-20261014-000001'
  const operationTaskId = 'op-20'
  const principal = {
    principalId: 'trace-reader',
    principalType: 'User',
    loginName: 'reader',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: ['business.mes.traceability.read', 'business.mes.work-orders.read'],
  }
  const session = {
    principal,
    accessToken: 'fixture-access',
    refreshToken: 'fixture-refresh',
    sessionId: 'trace-reader',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
  }
  const detailRequests: URL[] = []
  const traceRequests: URL[] = []
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route(/\/api\/(?:business-console|console)\/v1\//, async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const json = (data: unknown) => route.fulfill({ json: { success: true, data } })
    if (path.endsWith('/auth/refresh')) return json(session)
    if (path.endsWith('/auth/me')) return json(principal)
    if (
      path.endsWith('/me/work-context') &&
      !principal.permissionCodes.includes(url.searchParams.get('permissionCode') ?? '')
    ) {
      return json({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolutionStatus: 'no-authorized-scope',
        authorizedScopes: [],
      })
    }
    if (path.endsWith('/me/work-context'))
      return json({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolutionStatus: 'resolved',
        authorizedScopes: [{ kind: 'workCenter', id: 'WC-TUBE-01', displayName: '制管一线' }],
        selectedScope: { kind: 'workCenter', id: 'WC-TUBE-01', displayName: '制管一线' },
      })
    if (path.includes('/mes/traceability/')) {
      traceRequests.push(url)
      return json({
        nodes: [
          {
            nodeId: workOrderId,
            nodeType: 'WorkOrder',
            displayName: workOrderId,
            status: 'released',
          },
          {
            nodeId: operationTaskId,
            nodeType: 'OperationTask',
            displayName: `${workOrderId}-OP-20`,
            status: 'Queued',
          },
        ],
        edges: [
          { fromNodeId: workOrderId, toNodeId: operationTaskId, relationType: 'has-operation' },
        ],
      })
    }
    if (path.endsWith(`/mes/work-orders/${workOrderId}`)) {
      detailRequests.push(url)
      return json({
        workOrderId,
        skuId: 'FG-TUBE-01',
        quantity: 120,
        status: 'released',
        blockingReasons: [],
        operationTasks: [
          {
            operationTaskId: 'op-10',
            operationTaskNo: `${workOrderId}-OP-10`,
            status: 'Completed',
          },
          { operationTaskId, operationTaskNo: `${workOrderId}-OP-20`, status: 'Queued' },
        ],
      })
    }
    if (path.endsWith('/material-readiness'))
      return json({ readinessStatus: 'Ready', items: [], blockingReasons: [] })
    return json({ items: [], total: 0, resources: [] })
  })
  const query =
    '/mes/traceability?mode=batch&batchOrSerial=SER-TUBE-001&materialLotId=MAT-LOT-001&filter=retained'
  await page.goto(query)
  await expect(page.getByRole('link', { name: workOrderId, exact: true })).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('trace-nodes.png'), fullPage: true })
  await page.getByRole('link', { name: workOrderId, exact: true }).click()
  await expect(page).toHaveURL(new RegExp(`/mes/work-orders/${workOrderId}\\?`))
  await expect(page.getByRole('row').filter({ hasText: `${workOrderId}-OP-20` })).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: `${workOrderId}-OP-20` })).not.toContainText(
    '来源工序',
  )
  await page.getByRole('link', { name: '返回追溯', exact: true }).click()
  await expect(page.getByRole('link', { name: operationTaskId, exact: true })).toBeVisible()
  await page.getByRole('link', { name: operationTaskId, exact: true }).click()
  const sourceRow = page.getByRole('row').filter({ hasText: `${workOrderId}-OP-20` })
  await expect(sourceRow).toContainText('来源工序')
  const sourceMarker = sourceRow.getByText('来源工序', { exact: true })
  const markerLayout = await sourceMarker.evaluate((marker) => {
    const badge = marker.getBoundingClientRect()
    const cell = marker.closest('td')!.getBoundingClientRect()
    return {
      right: badge.right,
      bottom: badge.bottom,
      cellRight: cell.right,
      cellBottom: cell.bottom,
    }
  })
  expect(markerLayout.right).toBeLessThanOrEqual(markerLayout.cellRight)
  expect(markerLayout.bottom).toBeLessThanOrEqual(markerLayout.cellBottom)
  await expect(page.getByRole('row').filter({ hasText: `${workOrderId}-OP-10` })).not.toContainText(
    '来源工序',
  )
  await page.screenshot({
    path: testInfo.outputPath('work-order-source-operation.png'),
    fullPage: true,
  })
  expect(detailRequests.length).toBeGreaterThanOrEqual(1)
  for (const request of detailRequests) {
    expect(request.pathname).toContain(`/mes/work-orders/${workOrderId}`)
    expect(request.searchParams.get('organizationId')).toBe(principal.organizationId)
    expect(request.searchParams.get('environmentId')).toBe(principal.environmentId)
    expect(request.searchParams.get('scopeKind')).toBe('workCenter')
    expect(request.searchParams.get('scopeId')).toBe('WC-TUBE-01')
  }
  await page.getByRole('link', { name: '返回追溯', exact: true }).click()
  await expect(page.getByRole('link', { name: operationTaskId, exact: true })).toBeVisible()
  const restored = new URL(page.url())
  expect(restored.searchParams.get('mode')).toBe('batch')
  expect(restored.searchParams.get('batchOrSerial')).toBe('SER-TUBE-001')
  expect(restored.searchParams.get('materialLotId')).toBe('MAT-LOT-001')
  expect(restored.searchParams.get('filter')).toBe('retained')
  await expect(page.getByLabel('批次或物料批', { exact: true })).toHaveValue('SER-TUBE-001')
  for (const request of traceRequests) {
    expect(request.searchParams.get('organizationId')).toBe(principal.organizationId)
    expect(request.searchParams.get('environmentId')).toBe(principal.environmentId)
  }
  await page.screenshot({ path: testInfo.outputPath('trace-return.png'), fullPage: true })
})
