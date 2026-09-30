import { expect, test } from '@playwright/test'

// #4072：生产构建 + HTTP fixture，证明下一轮轮询更新页面，不证明 MES 服务端失效链。
const principal = {
  principalId: 'planner-1',
  principalType: 'User',
  loginName: 'planner',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: ['business.scheduling.plans.read', 'business.scheduling.plans.release'],
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  principal,
}

test('后台失效在下一轮轮询更新提示和发布按钮，退出后停止', async ({ page }) => {
  let invalidated = false
  let historyRequests = 0
  await page.clock.install()
  await page.addInitScript((storedSession) => {
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(storedSession))
  }, session)
  await page.route('**/api/console/v1/**', async (route) => {
    const pathname = new URL(route.request().url()).pathname
    await route.fulfill({
      json: { success: true, data: pathname.endsWith('/me') ? principal : session },
    })
  })
  await page.route('**/api/business-console/v1/**', async (route) => {
    const pathname = new URL(route.request().url()).pathname
    if (pathname.endsWith('/scheduling/plans/history')) {
      historyRequests++
      return route.fulfill({
        json: {
          success: true,
          data: {
            total: 1,
            items: [
              {
                planId: 'APS-260930-001',
                status: 'generated',
                assignmentCount: 12,
                isInvalidated: invalidated,
                latestInvalidationReasonCode: invalidated ? 'equipmentUnavailable' : undefined,
              },
            ],
          },
        },
      })
    }
    return route.fulfill({ json: { success: true, data: { items: [], total: 0 } } })
  })
  await page.goto('/scheduling')
  await page.getByRole('tab', { name: '表格' }).click()
  const row = page.getByRole('row').filter({ hasText: 'APS-260930-001' })
  const release = row.getByRole('button', { name: '发布', exact: true })
  await expect(release).toBeEnabled()
  invalidated = true
  await page.clock.fastForward(5000)
  await expect(row).toContainText('已失效')
  await expect(row).toContainText('设备不可用')
  await expect(release).toBeDisabled()
  await page.screenshot({
    path: test.info().outputPath('scheduling-invalidated.png'),
    fullPage: true,
  })
  await page.goto('/')
  const requestsAfterLeaving = historyRequests
  await page.clock.fastForward(10_000)
  expect(historyRequests).toBe(requestsAfterLeaving)
})

for (const scenario of ['查阅另一方案', '筛选未失效方案']) {
  test(`草案失效状态不受${scenario}影响`, async ({ page }) => {
    let invalidated = false
    const planner = {
      ...principal,
      permissionCodes: [
        ...principal.permissionCodes,
        'business.scheduling.plans.manage',
        'business.mes.work-orders.read',
      ],
    }
    const draft = { planId: 'APS-260930-001', status: 'generated', assignments: [] }
    await page.clock.install()
    await page.addInitScript(
      (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
      { ...session, principal: planner },
    )
    await page.route('**/api/console/v1/**', (route) =>
      route.fulfill({
        json: {
          success: true,
          data: new URL(route.request().url()).pathname.endsWith('/me')
            ? planner
            : { ...session, principal: planner },
        },
      }),
    )
    await page.route('**/api/business-console/v1/**', async (route) => {
      const url = new URL(route.request().url())
      let data: unknown = { items: [], total: 0 }
      if (url.pathname.endsWith('/scheduling/plans/history')) {
        const items = [
          {
            ...draft,
            isInvalidated: invalidated,
            latestInvalidationReasonCode: 'equipmentUnavailable',
          },
          { planId: 'APS-260930-002', status: 'generated', isInvalidated: false },
        ].filter((item) => url.searchParams.get('isInvalidated') !== 'false' || !item.isInvalidated)
        data = { items, total: items.length }
      } else if (url.pathname.endsWith('/me/work-context')) {
        data = {
          authorizedScopes: [{ kind: 'work-center', id: 'WC-TURN', displayName: '车削中心' }],
          selectedScope: url.searchParams.has('scopeId')
            ? { kind: 'work-center', id: 'WC-TURN' }
            : null,
        }
      } else if (url.pathname.endsWith('/mes/work-orders')) {
        data = {
          items: [
            {
              workOrderId: 'WO-260930-001',
              productionVersionId: 'PV-1',
              status: 'released',
              productName: '主轴',
              priority: 100,
            },
          ],
          total: 1,
        }
      } else if (
        url.pathname.endsWith('/scheduling/workbench/plans') ||
        url.pathname.endsWith('/scheduling/plans/APS-260930-001')
      ) {
        data = draft
      } else if (url.pathname.endsWith('/scheduling/plans/APS-260930-002')) {
        data = { ...draft, planId: 'APS-260930-002' }
      }
      await route.fulfill({ json: { success: true, data } })
    })
    await page.goto('/scheduling')
    await page.getByRole('button', { name: '全部加入', exact: true }).click()
    await page.getByRole('button', { name: '生成首版', exact: true }).click()
    const publish = page.getByRole('button', { name: '发布新版', exact: true })
    await expect(publish).toBeEnabled()
    await page.getByRole('tab', { name: '表格', exact: true }).click()
    if (scenario === '筛选未失效方案') {
      await page.getByRole('combobox', { name: '按方案失效筛选' }).click()
      await page.getByRole('option', { name: '未失效', exact: true }).click()
    }
    invalidated = true
    await page.clock.fastForward(5000)
    if (scenario === '查阅另一方案') {
      await page
        .getByRole('row')
        .filter({ hasText: 'APS-260930-002' })
        .getByRole('button', { name: '明细', exact: true })
        .click()
      await page.keyboard.press('Escape')
    }
    await page.getByRole('tab', { name: '排程总览', exact: true }).click()
    await expect(publish).toBeDisabled()
    await expect(
      page.getByText('方案已失效（设备不可用），请重排后再发布', { exact: true }),
    ).toBeVisible()
    await page.screenshot({ path: test.info().outputPath(`draft-${scenario}.png`), fullPage: true })
  })
}
