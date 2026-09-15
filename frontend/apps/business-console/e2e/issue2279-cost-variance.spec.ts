import { expect, test } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'
import { requireBrowserEvidenceOutputDir } from '../playwright.config'

// 浏览器与 HTTP 桩只证明 UI 契约，不代表真实后端或 FullChain。
test('财务核对人工差异并展开覆盖报工', async ({ page }, testInfo) => {
  const principal = {
    principalId: 'finance-01',
    principalType: 'User',
    loginName: 'finance.accountant',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: ['business.erp.finance.read'],
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
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    let data: unknown = { items: [], total: 0 }
    if (url.pathname.endsWith('/auth/refresh')) data = session
    else if (url.pathname.endsWith('/auth/me')) data = principal
    else if (url.pathname.includes('/work-order-costs/')) {
      expect(url.searchParams.get('organizationId')).toBe('org-001')
      expect(url.searchParams.get('environmentId')).toBe('env-dev')
      data = {
        workOrderId: 'WO-202609-0186',
        currencyCode: 'CNY',
        laborCostBasis: 'actualOperation',
        laborVarianceStatus: 'available',
        unavailableReason: null,
        actualLaborHours: 12,
        actualLaborCost: 600,
        standardLaborHours: 10,
        standardLaborCost: 500,
        laborEfficiencyVarianceHours: 2,
        laborEfficiencyVarianceAmount: 100,
        laborEfficiencyVarianceDirection: 'unfavorable',
        laborRateVarianceStatus: 'notApplicable',
        laborRateVarianceReason: 'actual_payroll_rate_not_modeled',
        materialCost: 2000,
        totalAccumulatedCost: 2600,
        capitalizedCost: 2400,
        capitalizationVarianceAmount: 200,
        actualMachineHours: null,
        machineCostStatus: 'notApplicable',
        machineCostUnavailableReason: 'machine_overhead_not_applicable',
        machineCurrencyCode: null,
        appliedFixedMachineOverhead: null,
        appliedVariableMachineOverhead: null,
        appliedMachineOverheadTotal: null,
        machineOverheadPageNumber: 1,
        machineOverheadPageSize: 10,
        totalMachineOverheadOperations: 0,
        machineOverheadOperations: [],
        pageNumber: 1,
        pageSize: 10,
        totalOperations: 1,
        operations: [
          {
            operationTaskId: 'OP-0186-10',
            workCenterId: 'WC-CNC-01',
            settlementRevision: 2,
            status: 'available',
            unavailableReason: null,
            actualLaborHours: 12,
            actualLaborCost: 600,
            standardLaborHours: 10,
            standardLaborCost: 500,
            laborEfficiencyVarianceHours: 2,
            laborEfficiencyVarianceAmount: 100,
            laborEfficiencyVarianceDirection: 'unfavorable',
            currencyCode: 'CNY',
            workCenterCostRateId: 'RATE-CNC-09',
            rateRevision: 3,
            hourlyRate: 50,
            rateBasisAtUtc: '2026-09-12T08:00:00Z',
            coveredReports: [
              {
                reportNo: 'RPT-0186-11',
                goodQuantity: 100,
                scrapQuantity: 2,
                reworkQuantity: 1,
                uomCode: '件',
                theoreticalRatePerHour: 10,
                reportedAtUtc: '2026-09-12T09:00:00Z',
                isReversal: false,
              },
            ],
          },
        ],
      }
    }
    await route.fulfill({ json: { success: true, data, code: 0, message: '' } })
  })
  await page.goto('/erp/finance/cost-variance')
  await page.getByLabel('工单编号', { exact: true }).fill('WO-202609-0186')
  await page.getByRole('button', { name: '查询工单', exact: true }).click()
  await expect(page.getByText('效率方向：不利', { exact: true })).toBeVisible()
  await expect(page.getByText('未启用机器成本', { exact: true })).toBeVisible()
  const output = requireBrowserEvidenceOutputDir()
  await mkdir(output, { recursive: true })
  await page.screenshot({
    path: path.join(output, `cost-variance-${testInfo.project.name}.png`),
    fullPage: true,
  })
  await page.getByText('费率与覆盖报工', { exact: true }).click()
  await expect(page.getByText('RPT-0186-11 · 报工', { exact: true })).toBeVisible()
  await page.screenshot({
    path: path.join(output, `cost-variance-lineage-${testInfo.project.name}.png`),
    fullPage: true,
  })
})
