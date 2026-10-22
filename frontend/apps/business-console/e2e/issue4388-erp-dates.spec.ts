import { expect, test } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'
import { requireBrowserEvidenceOutputDir } from '../playwright.config'

// #3905 r1：真实页面及日期控件，HTTP fixture；不声明真实后端验收。
test('ERP 单据日期选择器保留布局、本地化显示及清除入口', async ({ page }) => {
  const out = requireBrowserEvidenceOutputDir()
  await mkdir(out, { recursive: true })
  const principal = {
    principalId: 'finance-01',
    principalType: 'User',
    loginName: 'finance.accountant',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: [
      'business.erp.procurement.read',
      'business.erp.finance.read',
      'business.erp.finance.manage',
      'business.erp.sales.read',
    ],
  }
  const session = {
    principal,
    accessToken: 'ui-contract-access',
    refreshToken: 'ui-contract-refresh',
    sessionId: 'ui-contract-session',
    expiresAtUtc: '2099-01-01T00:00:00Z',
  }
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const url = new URL(route.request().url())
      let data: unknown = { items: [], total: 0 }
      if (url.pathname.endsWith('/auth/me')) data = principal
      else if (url.pathname.endsWith('/auth/refresh')) data = session
      else if (url.pathname.endsWith('/rfqs'))
        data = {
          items: [
            {
              rfqNo: 'RFQ-2026-0618',
              supplierCodes: ['SUP-001'],
              lines: [
                { skuCode: 'SKU-RM-001', uomCode: 'EA', quantity: 10, requiredDate: '2026-06-18' },
              ],
            },
          ],
          total: 1,
        }
      await route.fulfill({ json: { success: true, data, code: 0, message: '' } })
    },
  )
  const errors: string[] = []
  page.on('pageerror', (error) => errors.push(error.message))
  const cases = [
    ['procurement/rfqs', '新建 RFQ', 'erp-rfq-date'],
    ['procurement/supplier-quotations', '录入报价', 'erp-sq-date'],
    ['procurement/purchase-orders', '新建采购单', 'erp-po-date'],
    ['sales/quotations', '新建报价', 'erp-quo-expires'],
    ['finance/work-center-cost-rates?workCenterId=WC-CNC-01', '新增修订', 'erp-wcr-from'],
    ['finance/vouchers', '过账凭证', 'erp-jv-date'],
  ]
  for (const [route, action, id] of cases) {
    await page.goto(`/erp/${route}`)
    await page.getByRole('button', { name: action, exact: true }).click()
    const trigger = page.locator(`#${id}`)
    await trigger.click()
    const month = page.locator('[data-slot="popover-content"] .tabular-nums').first()
    for (let i = 0; i < 120 && (await month.textContent())?.trim() !== '2026 年 6 月'; i++) {
      const [year, m] = (await month.textContent())!.match(/\d+/g)!.map(Number)
      await page
        .getByRole('button', {
          name: year * 12 + m > 2026 * 12 + 6 ? '上个月' : '下个月',
          exact: true,
        })
        .click()
    }
    await expect(month).toHaveText('2026 年 6 月')
    await page.locator('[data-slot="popover-content"]').evaluate(async (element) => {
      await Promise.all(
        element.getAnimations({ subtree: true }).map((animation) => animation.finished),
      )
    })
    await page.screenshot({ path: path.join(out, `${id}-calendar.png`), fullPage: true })
    await page.locator('.nv-dp-cell').filter({ hasText: /^19$/ }).click()
    await expect(trigger).toHaveText('2026/6/19')
    await expect(page.locator('[data-slot="popover-content"]')).toBeHidden()
    await page.screenshot({ path: path.join(out, `${id}-selected.png`), fullPage: true })
    await page.getByRole('button', { name: '清除所选日期', exact: true }).click()
    await expect(trigger).toHaveText('选择日期')
  }
  expect(errors).toEqual([])
})
