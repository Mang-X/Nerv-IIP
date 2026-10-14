import { expect, test } from '@playwright/test'

// 页面点击 + Gateway 契约 fixture；不证明真实耗料或最小权限主体。
for (const [nodeType, nodeId] of [
  ['Material', 'MAT-STEEL-01'],
  ['MaterialLot', 'MLOT-20261014-01'],
  ['MaterialIssueRequest', 'MIR-20261014-01'],
]) {
  test(`${nodeType} 选择耗料来源核对明细并返回原查询（fixture）`, async ({ page }, testInfo) => {
    const principal = {
      principalId: 'material-trace-reader',
      principalType: 'User',
      loginName: 'reader',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionVersion: 1,
      permissionCodes: ['business.mes.traceability.read', 'business.mes.reporting.read'],
    }
    const session = {
      principal,
      accessToken: 'fixture-access',
      refreshToken: 'fixture-refresh',
      sessionId: 'material-trace-reader',
      expiresAtUtc: '2099-01-01T00:00:00.000Z',
    }
    const facts = [
      {
        materialId: 'MAT-STEEL-01',
        materialLotId: 'MLOT-20261014-01',
        consumedQuantity: 7.5,
        uomCode: 'kg',
        materialIssueRequestNo: 'MIR-20261014-01',
      },
      {
        materialId: 'MAT-LABEL-02',
        materialLotId: 'MLOT-20261014-02',
        consumedQuantity: 2,
        uomCode: 'ea',
        materialIssueRequestNo: 'MIR-20261014-02',
      },
    ]
    const requests: URL[] = []
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
      if (path.endsWith('/me/work-context')) {
        const authorized = principal.permissionCodes.includes(
          url.searchParams.get('permissionCode') ?? '',
        )
        return json({
          organizationId: principal.organizationId,
          environmentId: principal.environmentId,
          applicablePermissionCode: url.searchParams.get('permissionCode'),
          resolutionStatus: authorized ? 'resolved' : 'no-authorized-scope',
          authorizedScopes: authorized
            ? [{ kind: 'workCenter', id: 'WC-TUBE-01', displayName: '制管一线' }]
            : [],
          selectedScope: authorized
            ? { kind: 'workCenter', id: 'WC-TUBE-01', displayName: '制管一线' }
            : undefined,
        })
      }
      if (path.includes('/mes/traceability/'))
        return json({
          nodes: [
            {
              nodeType: 'Material',
              nodeId: facts[0]!.materialId,
              displayName: facts[0]!.materialId,
              status: 'Consumed',
            },
            {
              nodeType: 'MaterialLot',
              nodeId: facts[0]!.materialLotId,
              displayName: facts[0]!.materialLotId,
              status: 'Consumed',
            },
            {
              nodeType: 'MaterialIssueRequest',
              nodeId: facts[0]!.materialIssueRequestNo,
              displayName: facts[0]!.materialIssueRequestNo,
              status: 'Received',
            },
            {
              nodeType: 'ProductionReport',
              nodeId: 'PRPT-20261014-01',
              displayName: 'PRPT-20261014-01',
              status: 'Reported',
            },
            {
              nodeType: 'ProductionReport',
              nodeId: 'PRPT-20261014-02',
              displayName: 'PRPT-20261014-02',
              status: 'Reported',
            },
          ],
          edges: [
            {
              fromNodeId: facts[0]!.materialId,
              toNodeId: facts[0]!.materialLotId,
              relationType: 'has-lot',
            },
            {
              fromNodeId: facts[0]!.materialIssueRequestNo,
              toNodeId: facts[0]!.materialLotId,
              relationType: 'received-lot',
            },
            {
              fromNodeId: facts[0]!.materialLotId,
              toNodeId: 'PRPT-20261014-01',
              relationType: 'consumed-by-report',
            },
            {
              fromNodeId: facts[0]!.materialLotId,
              toNodeId: 'PRPT-20261014-02',
              relationType: 'consumed-by-report',
            },
          ],
        })
      if (path.endsWith('/mes/production-reports/PRPT-20261014-02')) {
        requests.push(url)
        return json({
          report: {
            reportNo: 'PRPT-20261014-02',
            workOrderNo: 'WO-20261014-01',
            operationTaskNo: 'WO-20261014-01-OP-20',
            reportedByDisplayName: '李明',
            reportedAtUtc: '2026-10-14T01:00:00Z',
            goodQuantity: 120,
            scrapQuantity: 0,
            reworkQuantity: 0,
          },
          consumedMaterialLots: facts,
        })
      }
      return json({ items: [], total: 0, resources: [] })
    })
    const query =
      '/mes/traceability?mode=material-lot&workOrderId=WO-20261014-01&batchOrSerial=SN-TUBE-01&materialLotId=MLOT-20261014-01&filter=retained'
    await page.goto(query)
    const row = page.getByRole('row').filter({ has: page.getByText(nodeId!, { exact: true }) })
    await expect(row).toContainText('选择来源报工')
    await expect(
      row.getByRole('link', { name: '耗料 · PRPT-20261014-01', exact: true }),
    ).toBeVisible()
    await row.getByRole('link', { name: '耗料 · PRPT-20261014-02', exact: true }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
    const route = new URL(page.url())
    expect(route.searchParams.get('reportNo')).toBe('PRPT-20261014-02')
    expect(route.searchParams.get('sourceNodeType')).toBe(nodeType)
    expect(route.searchParams.get('sourceNodeId')).toBe(nodeId)
    const section = page.getByRole('region', { name: '来源耗料事实' })
    await expect(section).toContainText(facts[0]!.materialId)
    await expect(section).toContainText(facts[0]!.materialLotId)
    await expect(section).toContainText('7.5')
    await expect(section).toContainText('kg')
    await expect(section).toContainText(facts[0]!.materialIssueRequestNo)
    await expect(section).not.toContainText(facts[1]!.materialId)
    await expect(section).not.toContainText(facts[1]!.materialLotId)
    await page.screenshot({
      path: testInfo.outputPath('material-source-facts.png'),
      fullPage: true,
    })
    expect(requests.length).toBeGreaterThan(0)
    for (const request of requests) {
      expect(request.searchParams.get('organizationId')).toBe(principal.organizationId)
      expect(request.searchParams.get('environmentId')).toBe(principal.environmentId)
    }
    await page.getByRole('link', { name: '返回追溯', exact: true }).click()
    await expect(row).toContainText('选择来源报工')
    expect(new URL(page.url()).searchParams.toString()).toBe(
      new URL(query, 'http://local').searchParams.toString(),
    )
    await page.screenshot({
      path: testInfo.outputPath('material-trace-return.png'),
      fullPage: true,
    })
  })
}
