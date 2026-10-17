import { expect, test } from '@playwright/test'

// Regression：#4458。真实页面、客户端与 HTTP fixture；不证明真实后端授权或文件下载。
test('切换作业范围关闭旧 SOP，晚到响应不覆盖新任务，切回和关闭重开正常（HTTP fixture）', async ({
  page,
}, testInfo) => {
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
  const scopes = [
    { kind: 'work-center', id: 'WC-TUB-01', displayName: '管材切割一线' },
    { kind: 'work-center', id: 'WC-ROD-01', displayName: '活塞杆加工一线' },
    { kind: 'work-center', id: 'WC-ASM-01', displayName: '总成装配一线' },
  ]
  const tasks = scopes.map((scope, index) => ({
    operationTaskId: `WO-QJ-1001-OP-${(index + 1) * 10}`,
    operationTaskNo: `WO-QJ-1001-OP-${(index + 1) * 10}`,
    operationSequence: (index + 1) * 10,
    workOrderId: 'WO-QJ-1001',
    operationCode: ['OP-WB-CUT', 'OP-ROD-TURN', 'OP-ASM'][index],
    workCenterId: scope.id,
    workCenterCode: scope.id,
    workCenterName: scope.displayName,
    routingCode: 'ROUTING-FG-QJ-P1-L',
    routingRevision: '1',
    status: 'Queued',
    allowedActions: [],
  }))
  const sopRequests: URL[] = []
  let releaseOld!: () => void
  const oldResponse = new Promise<void>((resolve) => {
    releaseOld = resolve
  })
  let oldStarted!: () => void
  const oldRequest = new Promise<void>((resolve) => {
    oldStarted = resolve
  })
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
      return json({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolutionStatus: 'resolved',
        authorizedScopes: scopes,
        selectedScope: scopes.find((scope) => scope.id === url.searchParams.get('scopeId')) ?? null,
        availableScopeKinds: ['work-center'],
        issues: [],
      })
    }
    if (path.endsWith('/mes/operation-tasks')) {
      const task = tasks.find((task) => task.workCenterId === url.searchParams.get('scopeId'))
      return json({ items: task ? [task] : [], total: task ? 1 : 0 })
    }
    if (path.endsWith('/mes/operation-sops/current')) {
      sopRequests.push(url)
      const task = tasks.find(
        (task) => task.operationCode === url.searchParams.get('operationCode'),
      )!
      if (sopRequests.length === 1) {
        oldStarted()
        await oldResponse
      }
      return json({
        items: [
          {
            documentNumber: `SOP-${task.operationCode}`,
            revision: 'A',
            fileId: task.operationCode,
            fileName: `${task.workCenterName}作业指导书`,
            effectiveDate: '2026-01-01',
          },
        ],
      })
    }
    return json({ items: [] })
  })
  await page.goto('/mes/operation-tasks')
  const panel = page
    .locator('section[aria-live="polite"]')
    .filter({ has: page.getByRole('heading', { name: /WO-QJ-1001-OP-/ }) })
  const open = async (index: number) => {
    await page.getByRole('button', { name: '查看当前SOP', exact: true }).click()
    await expect(panel.getByRole('heading')).toHaveText(
      `${tasks[index]!.operationTaskNo} · ${tasks[index]!.operationCode}`,
    )
  }
  const selectScope = async (index: number) => {
    await page.getByRole('combobox', { name: '作业范围', exact: true }).click()
    await page
      .getByRole('option', { name: `${scopes[index]!.displayName}（工作中心）`, exact: true })
      .click()
    await expect(panel).toHaveCount(0)
    await expect(
      page.getByText(tasks[index]!.operationTaskNo, { exact: true }).first(),
    ).toBeVisible()
  }
  await open(0)
  await oldRequest
  await selectScope(1)
  await open(1)
  await expect(panel).toContainText('活塞杆加工一线作业指导书')
  const oldFinished = page.waitForResponse(
    (response) =>
      response.url().includes('/operation-sops/current') &&
      new URL(response.url()).searchParams.get('operationCode') === 'OP-WB-CUT',
  )
  releaseOld()
  await oldFinished
  await expect(panel).toContainText('活塞杆加工一线作业指导书')
  await expect(panel).not.toContainText('管材切割一线作业指导书')
  await page.screenshot({
    path: testInfo.outputPath('late-response-new-context.png'),
    fullPage: true,
  })
  await selectScope(2)
  await open(2)
  await expect(panel).toContainText('总成装配一线作业指导书')
  await selectScope(0)
  await open(0)
  await expect(panel).toContainText('管材切割一线作业指导书')
  await panel.getByRole('button', { name: '关闭SOP', exact: true }).click()
  await expect(panel).toHaveCount(0)
  await open(0)
  await expect(panel).toContainText('管材切割一线作业指导书')
  await page.getByRole('textbox').first().fill('WO-QJ-1001')
  await expect(panel).toHaveCount(0)
  await page.screenshot({ path: testInfo.outputPath('task-filter-closed.png'), fullPage: true })
  expect(sopRequests.length).toBeGreaterThanOrEqual(3)
  for (const url of sopRequests) {
    const task = tasks.find((task) => task.operationCode === url.searchParams.get('operationCode'))!
    expect(Object.fromEntries(url.searchParams)).toMatchObject({
      organizationId: principal.organizationId,
      environmentId: principal.environmentId,
      operationCode: task.operationCode,
      workCenterCode: task.workCenterCode,
      routingCode: task.routingCode,
      routingRevision: task.routingRevision,
    })
  }
})
