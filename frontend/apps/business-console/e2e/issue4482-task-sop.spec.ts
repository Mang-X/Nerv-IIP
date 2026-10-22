import { expect, test } from '@playwright/test'

// Regression / DomainInvariant：#4482、父 #4457 已批准 r2。
// 真实页面与生成客户端 + Gateway HTTP fixture；不证明服务端授权或真实内容下载。
for (const outcome of ['documents', 'forbidden', 'identity-error'] as const) {
  test(`任务 SOP 接力与 ${outcome} 可见结果（HTTP fixture）`, async ({ page }, testInfo) => {
    const principal = {
      principalId: 'operator-tube-01',
      principalType: 'User',
      loginName: 'tube.operator',
      organizationId: 'org-tube',
      environmentId: 'env-production',
      permissionVersion: 1,
      permissionCodes: ['business.mes.operations.read'],
    }
    const session = {
      principal,
      accessToken: 'fixture-access',
      refreshToken: 'fixture-refresh',
      sessionId: 'tube-operator',
      expiresAtUtc: '2099-01-01T00:00:00.000Z',
    }
    const tasks = [
      ['ROUTING-FG-QJ-P1-L', '1'],
      ['ROUTING-FG-QJ-P1-L', '2'],
      ['ROUTING-FG-QJ-P2-L', '1'],
      [null, null],
      ['ROUTING-FG-QJ-P1-L', null],
      [null, '1'],
      ['ROUTING-FG-QJ-P1-L', '1'],
    ].map(([routingCode, routingRevision], index) => ({
      operationTaskId: `OP-TUBE-${index + 1}`,
      operationTaskNo: `OP-TUBE-${index + 1}`,
      workOrderId: `WO-TUBE-${index + 1}`,
      operationSequence: 10,
      operationCode: 'OP-WB-CUT',
      workCenterId: 'wc-tube-id',
      workCenterCode: index === 6 ? null : 'WC-TUB-01',
      workCenterName: '管材切割一线',
      status: 'Queued',
      routingCode,
      routingRevision,
      allowedActions: [],
    }))
    const sopRequests: URL[] = []
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
        const scope = { kind: 'work-center', id: 'wc-tube-id', displayName: '管材切割一线' }
        return json({
          organizationId: principal.organizationId,
          environmentId: principal.environmentId,
          applicablePermissionCode: url.searchParams.get('permissionCode'),
          resolutionStatus: 'resolved',
          authorizedScopes: [scope],
          selectedScope: scope,
          availableScopeKinds: ['work-center'],
          issues: [],
        })
      }
      if (path.endsWith('/mes/operation-tasks')) {
        if (outcome === 'identity-error')
          return route.fulfill({ status: 503, json: { message: '任务身份读取失败' } })
        return json({ items: tasks, total: tasks.length })
      }
      if (path.endsWith('/mes/operation-sops/current')) {
        sopRequests.push(url)
        if (outcome === 'forbidden')
          return route.fulfill({ status: 403, json: { message: '无权读取当前 SOP' } })
        const index = tasks
          .slice(0, 3)
          .findIndex(
            (task) =>
              task.routingCode === url.searchParams.get('routingCode') &&
              task.routingRevision === url.searchParams.get('routingRevision'),
          )
        return json({
          items:
            index < 0
              ? []
              : [
                  {
                    documentNumber: `SOP-TUBE-${index + 1}`,
                    revision: 'A',
                    fileId: `file-tube-${index + 1}`,
                    fileName: `管材切割作业指导书 ${index + 1}`,
                    effectiveDate: '2026-01-01',
                  },
                ],
        })
      }
      return json({ items: [] })
    })
    await page.goto('/mes/operation-tasks')
    const sops = page
      .locator('section[aria-live="polite"]')
      .filter({ has: page.getByRole('heading', { name: /OP-TUBE-/ }) })
    if (outcome === 'identity-error') {
      await expect(page.getByText('任务读取失败', { exact: true })).toBeVisible()
      await expect(page.getByRole('button', { name: '查看当前SOP', exact: true })).toHaveCount(0)
      expect(sopRequests).toHaveLength(0)
      return
    }
    await expect(page.getByRole('button', { name: '查看当前SOP', exact: true })).toHaveCount(7)
    const openTask = async (index: number) => {
      await page.getByRole('button', { name: '查看当前SOP', exact: true }).nth(index).click()
      await expect(sops).toContainText(tasks[index]!.operationTaskNo)
    }
    await openTask(0)
    if (outcome === 'forbidden') {
      await expect(sops.getByRole('alert')).toContainText(/无权|权限|403/)
      await expect(sops).not.toContainText('当前工序没有已发布')
      await expect(sops.getByRole('button', { name: '查看SOP', exact: true })).toHaveCount(0)
      return
    }
    for (const index of [0, 1, 2, 0]) {
      await openTask(index)
      await expect(sops).toContainText(`管材切割作业指导书 ${index + 1}`)
      await expect(sops.getByRole('button', { name: '查看SOP', exact: true })).toHaveCount(1)
      for (const other of [0, 1, 2].filter((value) => value !== index))
        await expect(sops).not.toContainText(`管材切割作业指导书 ${other + 1}`)
    }
    await page.screenshot({ path: testInfo.outputPath('routing-sop.png'), fullPage: true })
    for (const request of sopRequests) {
      expect(Object.fromEntries(request.searchParams)).toMatchObject({
        organizationId: 'org-tube',
        environmentId: 'env-production',
        operationCode: 'OP-WB-CUT',
        workCenterCode: 'WC-TUB-01',
      })
      expect(request.searchParams.get('routingCode')).toBeTruthy()
      expect(request.searchParams.get('routingRevision')).toBeTruthy()
    }
    const count = sopRequests.length
    for (const index of [3, 4, 5]) {
      await openTask(index)
      await expect(sops).toContainText('当前任务缺少工艺路线号或版本，无法确定适用的SOP。')
      await expect(sops).not.toContainText('当前工序没有已发布')
      await expect(sops.getByRole('button', { name: '刷新SOP' })).toBeDisabled()
      await expect(sops.getByRole('button', { name: '查看SOP', exact: true })).toHaveCount(0)
    }
    expect(sopRequests).toHaveLength(count)
    await page.screenshot({ path: testInfo.outputPath('missing-routing.png'), fullPage: true })
    await openTask(6)
    await expect(sops).toContainText('当前任务缺少工作中心编码，无法确定适用的SOP。')
    await expect(sops.getByRole('button', { name: '刷新SOP' })).toBeDisabled()
    await expect(sops.getByRole('button', { name: '查看SOP', exact: true })).toHaveCount(0)
    expect(sopRequests).toHaveLength(count)
  })
}
