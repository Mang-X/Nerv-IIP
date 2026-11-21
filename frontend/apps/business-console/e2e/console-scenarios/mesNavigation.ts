import { test, fulfillJson, envelope, expectHeading, expandFoundationIssues } from './fixture'
import { expect } from '@playwright/test'

export function registerMesNavigation() {
  test.describe('桌面制造执行入口', () => {
    test.skip(({ isMobile }) => isMobile, '桌面侧栏和宽表格交互')

    test('工单与派工：从侧栏进入工单，通过页内视图按人看负荷并返回原作业范围', async ({
      page,
    }, testInfo) => {
      await page.route('**/api/business-console/v1/me/work-context?*', (route) => {
        const params = new URL(route.request().url()).searchParams
        const scopes = [
          { kind: 'work-center', id: 'WC-TUBE', displayName: '制管工作中心' },
          { kind: 'work-center', id: 'WC-WELD', displayName: '焊接工作中心' },
        ]
        return fulfillJson(
          route,
          envelope({
            organizationId: 'org-001',
            environmentId: 'env-dev',
            applicablePermissionCode: params.get('permissionCode'),
            resolutionStatus: 'resolved',
            authorizedScopes: scopes,
            selectedScope: scopes.find(({ id }) => id === params.get('scopeId')),
          }),
        )
      })
      await page.route('**/api/business-console/v1/master-data/workers?*', (route) =>
        fulfillJson(
          route,
          envelope({
            items: [{ userId: 'user-zhang', displayName: '张伟', employeeNo: 'EMP-021' }],
            total: 1,
          }),
        ),
      )
      await page.route('**/api/business-console/v1/mes/dispatch-tasks?*', (route) => {
        const params = new URL(route.request().url()).searchParams
        const rows = [
          { operationTaskId: 'OP-WELD', operationTaskNo: 'OP-WELD', assignedUserId: 'user-zhang' },
          { operationTaskId: 'OP-CUT', operationTaskNo: 'OP-CUT', assignedUserId: 'user-li' },
        ].filter(
          ({ assignedUserId }) =>
            !params.has('assignedUserId') || params.get('assignedUserId') === assignedUserId,
        )
        return fulfillJson(
          route,
          envelope({
            items: rows.map((row) => ({ ...row, workOrderId: 'WO-001', status: 'Ready' })),
            total: rows.length,
          }),
        )
      })

      await expectHeading(page, '/mes', '生产驾驶舱')
      const sidebar = page.locator('[data-sidebar="sidebar"]')
      await expect(sidebar.getByRole('link', { name: '派工看板' })).toHaveCount(0)
      await sidebar.getByRole('link', { name: '工单与派工' }).click()

      const scope = page.getByRole('combobox', { name: '作业范围' })
      await expect(scope).toContainText('制管工作中心', { timeout: 30_000 })
      await scope.click()
      const switched = page.waitForRequest((request) => {
        const { pathname, searchParams } = new URL(request.url())
        return (
          pathname === '/api/business-console/v1/mes/work-orders' &&
          searchParams.get('scopeId') === 'WC-WELD'
        )
      })
      await page.getByRole('option', { name: /焊接工作中心/ }).click()
      await switched

      const views = page.getByRole('navigation', { name: '页面视图' })
      await views.getByRole('link', { name: '派工看板' }).click()
      await expect(page).toHaveURL(/\/mes\/dispatch$/, { timeout: 30_000 })
      const sidebarEntry = sidebar.getByRole('link', { name: '工单与派工' })
      await expect(sidebarEntry).toHaveAttribute('data-active', 'true')
      await expect(sidebarEntry).toHaveAttribute('href', '/mes/work-orders')
      await expect(views.getByRole('link', { name: '派工看板' })).toHaveAttribute(
        'aria-current',
        'page',
      )
      await expect(page.getByRole('link', { name: '打开工单' })).toHaveAttribute(
        'href',
        '/mes/work-orders/WO-001',
      )
      await page.getByRole('button', { name: '平铺列表' }).click()
      await expect(page.getByRole('cell', { name: 'OP-CUT', exact: true })).toBeVisible()
      await page.getByRole('button', { name: '受派工人' }).click()
      const filteredRead = page.waitForRequest((request) => {
        const { pathname, searchParams } = new URL(request.url())
        return (
          pathname === '/api/business-console/v1/mes/dispatch-tasks' &&
          searchParams.get('assignedUserId') === 'user-zhang'
        )
      })
      await page.getByRole('option', { name: '张伟 · EMP-021' }).click()
      await filteredRead
      await expect(page.getByRole('cell', { name: 'OP-CUT', exact: true })).toHaveCount(0)
      await expect(page.getByRole('cell', { name: 'OP-WELD', exact: true })).toBeVisible()
      await expect(
        page.getByRole('row').filter({ hasText: 'OP-WELD' }).getByRole('link', { name: 'WO-001' }),
      ).toHaveAttribute('href', '/mes/work-orders/WO-001')
      await page.screenshot({ path: testInfo.outputPath('dispatch-person-load.png') })
      await page.getByRole('button', { name: '工序操作 OP-WELD' }).click()
      await page.getByRole('menuitem', { name: '改派' }).click()
      const assignment = page.getByRole('dialog', { name: '改派工序' })
      await expect(assignment.getByRole('button', { name: '确认改派' })).toBeVisible()
      await assignment.getByRole('button', { name: '取消' }).click()

      await expectHeading(page, '/mes/dispatch/', '派工看板')
      await expect(sidebarEntry).toHaveAttribute('data-active', 'true')
      await views.getByRole('link', { name: '工单列表' }).click()
      await expect(scope).toContainText('焊接工作中心')
    })

    test('领料与齐套：进入准备检查，原深链接和返回历史恢复检查范围', async ({ page }, testInfo) => {
      await expectHeading(page, '/mes', '生产驾驶舱')
      const sidebar = page.locator('[data-sidebar="sidebar"]')
      await expect(sidebar.getByRole('link', { name: '生产准备检查' })).toHaveCount(0)
      await sidebar.getByRole('link', { name: '领料与齐套' }).click()
      const views = page.getByRole('navigation', { name: '页面视图' })
      await views.getByRole('link', { name: '生产准备检查' }).click()
      const sidebarEntry = sidebar.getByRole('link', { name: '领料与齐套' })
      await expect(sidebarEntry).toHaveAttribute('data-active', 'true')
      await expect(sidebarEntry).toHaveAttribute('href', '/mes/materials')
      await expect(views.getByRole('link', { name: '生产准备检查' })).toHaveAttribute(
        'aria-current',
        'page',
      )

      const path =
        '/mes/foundation?siteCode=SITE-001&lineCode=LINE-001&workCenterCode=WC-001&skuId=sku-1&productionVersionId=PV-001'
      await expectHeading(page, path, '生产准备检查')
      await expandFoundationIssues(page)
      const rateLink = page.getByRole('link', { name: '去维护：总装一线', exact: true })
      await expect(rateLink).toHaveAttribute(
        'href',
        '/erp/finance/work-center-cost-rates?fromFoundation=1&foundationOrganizationId=org-001&foundationEnvironmentId=env-dev&foundationSiteCode=SITE-001&foundationLineCode=LINE-001&foundationWorkCenterCode=WC-001&foundationSkuId=sku-1&foundationProductionVersionId=PV-001',
      )
      await expect(page.getByRole('link', { name: '去维护：总装设备', exact: true })).toBeVisible()
      await expect(page.getByText('缺少 11 月会计期间')).toBeVisible()
      await expect(page.getByRole('link', { name: '去维护：11 月', exact: true })).toHaveCount(0)
      await rateLink.click()
      await expect(page).toHaveURL(/\/erp\/finance\/work-center-cost-rates\?/)
      const scopedRead = page.waitForRequest((request) => {
        const { pathname, searchParams } = new URL(request.url())
        return (
          pathname === '/api/business-console/v1/mes/foundation-readiness' &&
          searchParams.get('siteCode') === 'SITE-001' &&
          searchParams.get('lineCode') === 'LINE-001' &&
          searchParams.get('workCenterCode') === 'WC-001' &&
          searchParams.get('skuId') === 'sku-1' &&
          searchParams.get('productionVersionId') === 'PV-001'
        )
      })
      await page.getByRole('link', { name: '返回原准备检查' }).click()
      await scopedRead
      await expect(views.getByRole('link', { name: '生产准备检查' })).toHaveAttribute('href', path)
      await page.screenshot({ path: testInfo.outputPath('foundation-deep-link.png') })

      await views.getByRole('link', { name: '领料' }).click()
      await expect(page).toHaveURL(/\/mes\/materials$/, { timeout: 30_000 })
      await page.goBack()
      await expect(views.getByRole('link', { name: '生产准备检查' })).toHaveAttribute('href', path)

      const slashPath = path.replace('/mes/foundation?', '/mes/foundation/?')
      await expectHeading(page, slashPath, '生产准备检查')
      await expect(views.getByRole('link', { name: '生产准备检查' })).toHaveAttribute(
        'href',
        slashPath,
      )
      await expect(sidebarEntry).toHaveAttribute('data-active', 'true')
      await views.getByRole('link', { name: '领料' }).click()
      await expect(page).toHaveURL(/\/mes\/materials$/)
    })

    test.describe('MES 只有独立视图读权限', () => {
      test.use({ permissionCodes: ['business.mes.dispatch.read', 'business.mes.foundation.read'] })

      test('旧入口可从侧栏进入，直接访问未授权主视图仍受守卫保护', async ({ page }) => {
        await expectHeading(page, '/mes/dispatch', '派工看板')
        const sidebar = page.locator('[data-sidebar="sidebar"]')
        await expect(sidebar.getByRole('link', { name: '工单与派工' })).toHaveCount(0)
        await expect(sidebar.getByRole('link', { name: '领料与齐套' })).toHaveCount(0)
        await expect(page.getByRole('navigation', { name: '页面视图' })).toHaveCount(0)
        await expect(page.getByText('WO-001', { exact: true })).toBeVisible()
        await expect(page.getByRole('link', { name: '打开工单' })).toHaveCount(0)
        await page.getByRole('button', { name: '平铺列表' }).click()
        await expect(page.getByRole('cell', { name: 'WO-001', exact: true })).toBeVisible()
        await expect(page.getByRole('link', { name: 'WO-001', exact: true })).toHaveCount(0)
        await sidebar.getByRole('link', { name: '生产准备检查' }).click()
        await expect(page).toHaveURL(/\/mes\/foundation$/, { timeout: 30_000 })
        await expect(page.getByRole('button', { name: '物料', exact: true })).toBeDisabled()
        await expect(
          page.getByText('当前角色没有物料读取权限，请联系管理员确认岗位授权。'),
        ).toBeVisible()
        await expandFoundationIssues(page)
        await expect(page.getByText('缺少总装一线的人工费率')).toBeVisible()
        await expect(page.getByText('缺少总装设备的制造费用率')).toBeVisible()
        await expect(page.getByText('缺少 11 月会计期间')).toBeVisible()
        await expect(page.getByRole('link', { name: /^去维护/ })).toHaveCount(0)
        await sidebar.getByRole('link', { name: '派工看板' }).click()
        await expect(page).toHaveURL(/\/mes\/dispatch$/, { timeout: 30_000 })

        for (const path of ['/mes/work-orders', '/mes/work-orders/WO-001', '/mes/materials']) {
          await page.goto(path)
          await expect(page).toHaveURL(/\/forbidden\?from=/)
        }
      })
    })

    test.describe('准备检查的会计期间维护', () => {
      test.use({ permissionCodes: ['business.mes.foundation.read', 'business.erp.finance.manage'] })

      test('财务管理权限可从缺项打开会计期间并返回原检查', async ({ page }) => {
        const path = '/mes/foundation?siteCode=SITE-001'
        await expectHeading(page, path, '生产准备检查')
        await expandFoundationIssues(page)
        const link = page.getByRole('link', { name: '去维护：11 月', exact: true })
        await expect(link).toHaveAttribute(
          'href',
          '/erp/finance/accounting-period-open?fromFoundation=1&foundationOrganizationId=org-001&foundationEnvironmentId=env-dev&foundationSiteCode=SITE-001&date=2026-11-20',
        )
        await link.click()
        await expect(page).toHaveURL(/\/erp\/finance\/accounting-period-open\?/)
        await page.getByRole('link', { name: '返回原准备检查' }).click()
        await expect(page).toHaveURL(/\/mes\/foundation\?siteCode=SITE-001$/)
      })
    })

    test.describe('MES 只有主视图读权限', () => {
      test.use({
        permissionCodes: ['business.mes.work-orders.read', 'business.mes.materials.read'],
      })

      test('主入口可用，旧深链接仍要求各自的读权限', async ({ page }) => {
        await expectHeading(page, '/mes/work-orders', '工单与派工')
        const sidebar = page.locator('[data-sidebar="sidebar"]')
        await expect(page.getByRole('navigation', { name: '页面视图' })).toHaveCount(0)
        await expect(sidebar.getByRole('link', { name: '派工看板' })).toHaveCount(0)
        await expect(sidebar.getByRole('link', { name: '生产准备检查' })).toHaveCount(0)
        await sidebar.getByRole('link', { name: '领料与齐套' }).click()
        await expect(page).toHaveURL(/\/mes\/materials$/, { timeout: 30_000 })
        await expect(page.getByRole('navigation', { name: '页面视图' })).toHaveCount(0)

        for (const path of ['/mes/dispatch', '/mes/foundation']) {
          await page.goto(path)
          await expect(page).toHaveURL(/\/forbidden\?from=/)
        }
      })
    })
  })
}
