import { expect, test, type Page } from '@playwright/test'

const demand = 'business.planning.demands.read'
const mps = 'business.planning.mps.read'
const mrp = 'business.planning.mrp.read'
const manage = 'business.planning.suggestions.manage'
const procurement = 'business.erp.procurement.read'
const runs = [
  { runId: 'november', status: 'Completed', horizonStart: '2026-11-01', horizonEnd: '2026-11-30' },
  { runId: 'october', status: 'Completed', horizonStart: '2026-10-01', horizonEnd: '2026-10-31' },
]
async function session(page: Page, permissionCodes: string[]) {
  const principal = {
    principalId: 'planner',
    principalType: 'User',
    loginName: 'planner',
    organizationId: 'nb',
    environmentId: 'plant',
    permissionVersion: 1,
    permissionCodes,
  }
  const credentials = {
    principal,
    accessToken: 'fixture-access',
    refreshToken: 'fixture-refresh',
    sessionId: 'planner',
    expiresAtUtc: '2099-01-01T00:00:00Z',
  }
  await page.addInitScript(
    (value) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(value)),
    credentials,
  )
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const { pathname, searchParams } = new URL(route.request().url())
      const path = pathname.replace('/api/business-console/v1/', '')
      let data: unknown
      if (pathname === '/api/console/v1/auth/me') data = principal
      else if (pathname === '/api/console/v1/auth/refresh') data = credentials
      else {
        expect(searchParams.get('organizationId'), pathname).toBe('nb')
        expect(searchParams.get('environmentId')).toBe('plant')
        expect(route.request().method()).toBe('GET')
        if (permissionCodes.includes(mrp) && path === 'planning/mrp-runs') data = { items: runs }
        if (permissionCodes.includes(mrp) && path === 'planning/suggestions')
          data = {
            items: runs.map((r) => ({
              suggestionId: 's-' + r.runId,
              runId: r.runId,
              skuCode: 'RM-' + r.runId,
              status: 'Open',
              suggestionType: 'purchase-requisition',
            })),
          }
        if (
          permissionCodes.includes(mrp) &&
          /^planning\/mrp-runs\/(november|october)\/pegging$/.test(path)
        )
          data = {
            items: [
              {
                suggestionId: 's-' + path.split('/')[2],
                parentSkuCode: 'RM-PEG',
                peggingType: 'component',
              },
            ],
          }
        if (
          permissionCodes.includes(demand) &&
          ['planning/demands', 'planning/forecasts'].includes(path)
        )
          data = { items: [] }
        if (permissionCodes.includes(mps) && path === 'planning/mps') data = { items: [] }
        if (
          [mrp, demand, procurement].every((p) => permissionCodes.includes(p)) &&
          path === 'planning/mrp-runs/november/material-deliveries'
        ) {
          expect(searchParams.has('planId')).toBe(false)
          data = { items: [], unknownRequirementSuggestions: [] }
        }
      }
      expect(data, '未授权或未声明的请求 ' + pathname).toBeDefined()
      await route.fulfill({ json: { success: true, data } })
    },
  )
}

for (const [permission, label] of [
  [demand, '需求池'],
  [mps, 'MPS 主计划'],
  [mrp, 'MRP 运行'],
] as const) {
  test(label + '岗位从缺权来源返回本岗位', async ({ page }) => {
    await session(page, [permission])
    await page.goto(
      permission === mrp ? '/planning?demandSource=SO-310' : '/planning?runId=october',
    )
    await expect(page.getByRole('status').filter({ hasText: '读取权限' })).toBeVisible()
    await page.getByRole('link', { name: /返回需求池|返回岗位入口/ }).click()
    await expect(page.getByRole('tab', { name: new RegExp(label) })).toHaveAttribute(
      'aria-selected',
      'true',
    )
    await expect(page.getByRole('tab', { name: '物料交付' })).toHaveCount(0)
    if (permission === demand) {
      await Promise.all([
        page.waitForResponse((r) => r.url().includes('/planning/forecasts?')),
        page.getByRole('tab', { name: '预测管理' }).click(),
      ])
    }
  })
}
test('仅建议管理无法枚举计划事实', async ({ page }) => {
  await session(page, [manage])
  await page.goto('/planning')
  await expect(page.getByText('当前岗位没有计划数据读取权限。')).toBeVisible()
  await expect(page.getByRole('tab')).toHaveCount(0)
})
test('基础物料交付可刷新且不探测 APS', async ({ page }) => {
  await session(page, [mrp, demand, procurement])
  await page.goto('/planning')
  await page.getByRole('tab', { name: '物料交付' }).click()
  await Promise.all([
    page.waitForResponse((r) => r.url().includes('/material-deliveries?')),
    page.getByRole('button', { name: '刷新交付事实' }).click(),
  ])
  await expect(page.getByLabel('物料交付 APS 方案')).toHaveCount(0)
})
test('来源建议切换运行后可操作，返回恢复来源', async ({ page }) => {
  await session(page, [mrp, manage])
  await page.goto('/planning?suggestionId=s-october')
  await expect(page.getByRole('row').filter({ hasText: 'RM-october' })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
    page.viewportSize()!.width + 1,
  )
  await page.getByLabel('建议运行').click()
  await page.getByRole('option', { name: /第 2 次/ }).click()
  await expect(page).toHaveURL(/runId=november$/)
  await expect(page.getByRole('row').filter({ hasText: 'RM-november' })).toBeVisible()
  await page.goBack()
  await expect(page.getByRole('row').filter({ hasText: 'RM-october' })).toBeVisible()
})
test('运行来源可以通过另一次运行的追溯行定位建议', async ({ page }) => {
  await session(page, [mrp])
  await page.goto('/planning?runId=october')
  await expect(page.getByRole('row').filter({ hasText: 'RM-october' })).toBeVisible()
  await page.getByRole('tab', { name: /MRP 运行/ }).click()
  await page
    .getByRole('row')
    .filter({ hasText: '2026-11-01' })
    .getByRole('button', { name: '查看追溯' })
    .click()
  await page.getByRole('button', { name: '定位建议', exact: true }).click()
  await expect(page.getByRole('row').filter({ hasText: 'RM-november' })).toBeVisible()
  await page.goBack()
  await expect(page.getByRole('row').filter({ hasText: 'RM-october' })).toBeVisible()
})

for (const status of [200, 403, 503]) {
  test('来源建议读取状态 ' + status, async ({ page }) => {
    await session(page, [mrp])
    let finish!: () => void
    const pending = new Promise<void>((resolve) => {
      finish = resolve
    })
    await page.route('**/planning/suggestions?**', async (route) => {
      await pending
      await route.fulfill({
        status,
        json:
          status === 200
            ? { success: true, data: { items: [] } }
            : { status, title: status === 403 ? 'Forbidden' : 'Unavailable' },
      })
    })
    await page.goto('/planning?suggestionId=absent')
    try {
      await expect(page.getByText('正在定位来源建议…', { exact: false })).toBeVisible()
    } finally {
      finish()
    }
    await expect(
      page.getByText(
        status === 403
          ? '当前岗位没有来源建议或运行的读取权限。'
          : status === 503
            ? '来源读取失败，请刷新后重试。'
            : '当前可读范围内未定位到来源建议或运行。',
        { exact: false },
      ),
    ).toBeVisible()
    await page.getByRole('link', { name: '返回岗位入口' }).click()
    await expect(page.getByRole('tab', { name: /MRP 运行/ })).toHaveAttribute(
      'aria-selected',
      'true',
    )
  })
}
