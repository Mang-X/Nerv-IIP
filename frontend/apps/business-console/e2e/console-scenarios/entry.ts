import { test, expectHeading } from './fixture'
import { expect } from '@playwright/test'

export function registerEntry() {
  test.describe('桌面顶栏按岗位授权进入业务域', () => {
    test.skip(({ isMobile }) => isMobile, '桌面顶栏交互')

    for (const role of [
      {
        permission: 'business.erp.sales.read',
        domain: '经营管理',
        path: '/erp/sales',
        pageTitle: '销售机会',
        forbiddenPath: '/erp',
      },
      {
        permission: 'business.masterdata.resources.read',
        domain: '基础数据',
        path: '/master-data/partners',
        pageTitle: '业务伙伴',
        forbiddenPath: '/master-data/skus',
      },
      {
        permission: 'business.engineering.changes.read',
        domain: '产品工程',
        path: '/engineering/eco',
        pageTitle: '工程变更',
        forbiddenPath: '/engineering/production-versions',
      },
    ]) {
      test.describe(role.pageTitle, () => {
        test.use({ permissionCodes: [role.permission] })

        test('从顶栏打开首个有权页面，未授权直链被拒绝', async ({ page }) => {
          await page.goto('/', { waitUntil: 'domcontentloaded' })
          const topNav = page.getByRole('navigation', { name: '一级能力区' })
          await topNav.getByRole('link', { name: role.domain, exact: true }).click()
          await expect(page).toHaveURL(role.path, { timeout: 30_000 })
          await expect(page.locator('[data-slot="breadcrumb-page"]')).toHaveText(role.pageTitle, {
            timeout: 15_000,
          })

          await page.goto(role.forbiddenPath, { waitUntil: 'domcontentloaded' })
          await expect(page).toHaveURL(
            (url) =>
              url.pathname === '/forbidden' && url.searchParams.get('from') === role.forbiddenPath,
          )
        })
      })
    }

    test.describe('只有资源管理权限', () => {
      test.use({ permissionCodes: ['business.masterdata.resources.manage'] })

      test('顶栏隐藏没有可访问页面的基础数据域', async ({ page }) => {
        await page.goto('/', { waitUntil: 'domcontentloaded' })
        const topNav = page.getByRole('navigation', { name: '一级能力区' })
        await expect(topNav.getByRole('link', { name: '数字化工作台' })).toBeVisible()
        await expect(topNav.getByRole('link', { name: '基础数据' })).toHaveCount(0)
        await expect(topNav.getByRole('button', { name: '更多' })).toHaveCount(0)
      })
    })
  })

  test('business console smoke pages render', async ({ page }) => {
    await expectHeading(page, '/master-data/skus', '物料与产品')
    await expectHeading(page, '/master-data/partners', '业务伙伴')
    await expectHeading(page, '/inventory/availability', '库存可用量')
    await expectHeading(page, '/quality/ncrs', '不合格品处理')
    await expectHeading(page, '/mes', '生产驾驶舱')
    await expectHeading(page, '/mes/foundation', '生产准备检查')
    await expectHeading(page, '/mes/plans', '生产计划')
    await expectHeading(page, '/mes/work-orders', '工单与派工')
    await expectHeading(page, '/mes/work-orders/WO-001', '工单 WO-001')
    const cancel = page.getByRole('button', { name: '取消工单', exact: true })
    await expect(cancel).toBeDisabled()
    await cancel.locator('..').focus()
    await expect(page.getByText('当前角色没有取消工单权限。', { exact: true })).toBeVisible()
    await expectHeading(page, '/mes/materials', '领料与齐套')
    await expectHeading(page, '/mes/dispatch', '派工看板')
    await expectHeading(page, '/mes/operation-tasks', '工序执行')
    await expectHeading(page, '/mes/production-reports', '报工记录')
    await expectHeading(page, '/mes/quality', '质量与不良')
    await expectHeading(page, '/mes/receipts', '完工入库')
    await expectHeading(page, '/mes/downtime', '设备与停机')
    await expectHeading(page, '/mes/handovers', '班次交接')
    await expectHeading(page, '/mes/traceability', '追溯查询')
    await expectHeading(page, '/mes/capacity', '产能影响')
  })

  // 回归：`pages/erp/sales.vue` 与 `pages/erp/sales/` 同名并存，曾让 sales.vue 变成父路由，
  // 子页没有出口 → /erp/sales/orders 等三个 URL 全部渲染「销售机会」。finance 同型。
  test('经营管理：销售/财务子页各自渲染，侧栏只高亮当前项', async ({ page }) => {
    const sections: Array<[path: string, heading: string, navItem: string]> = [
      ['/erp/sales', '销售机会', '销售机会'],
      ['/erp/sales/quotations', '销售报价', '销售报价'],
      ['/erp/sales/orders', '销售订单', '销售订单'],
      ['/erp/sales/deliveries', '销售发货', '销售发货'],
      ['/erp/finance', '财务摘要', '财务摘要'],
      ['/erp/finance/ar-ap', 'AR/AP', 'AR/AP'],
      ['/erp/finance/vouchers', '会计凭证', '会计凭证'],
      ['/erp/finance/cost-candidates', '成本候选', '成本候选'],
    ]

    for (const [path, heading, navItem] of sections) {
      await expectHeading(page, path, heading)

      // 侧栏双高亮回归：只有目标项 active，节区首页项不得同时点亮。
      const active = page.locator('[data-sidebar="menu-button"][data-active="true"]')
      await expect(active).toHaveCount(1, { timeout: 15_000 })
      await expect(active).toHaveText(navItem)
    }
  })
}
