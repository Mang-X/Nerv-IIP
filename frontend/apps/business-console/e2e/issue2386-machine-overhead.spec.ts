import { expect, test } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'
import { requireBrowserEvidenceOutputDir } from '../playwright.config'

// 浏览器 + HTTP 桩只证明 UI 契约，不代表真实后端验收。
test('财务人员核对机器预定分配与月度未多分配差异', async ({ page }) => {
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
        actualMachineHours: 120,
        machineCostStatus: 'available',
        machineCostUnavailableReason: null,
        machineCurrencyCode: 'CNY',
        appliedFixedMachineOverhead: 12000,
        appliedVariableMachineOverhead: 3600,
        appliedMachineOverheadTotal: 15600,
        machineOverheadPageNumber: 1,
        machineOverheadPageSize: 10,
        totalMachineOverheadOperations: 1,
        machineOverheadOperations: [
          {
            operationTaskId: 'OP-0186-10',
            workCenterId: 'WC-CNC-01',
            settlementId: 'SET-0186-2',
            settlementRevision: 2,
            status: 'available',
            unavailableReason: null,
            actualMachineHours: 120,
            appliedFixedMachineOverhead: 12000,
            appliedVariableMachineOverhead: 3600,
            appliedMachineOverheadTotal: 15600,
            accountingPeriodCode: '2026-09',
            currencyCode: 'CNY',
            workCenterMachineOverheadRateId: 'RATE-CNC-09',
            rateRevision: 3,
            deviceAssetId: 'CNC-01',
            completedAtUtc: '2026-09-12T10:00:00Z',
            sourceEventId: 'EVT-0186-2',
          },
        ],
      }
    } else if (url.pathname.endsWith('/work-center-machine-overhead-reconciliations')) {
      expect(url.searchParams.get('accountingPeriodCode')).toBe('2026-09')
      data = {
        accountingPeriodCode: '2026-09',
        pageNumber: 1,
        pageSize: 10,
        totalCount: 2,
        accountingPeriodStatus: 'open',
        reconciliationStatus: 'available',
        reconciliationUnavailableReason: null,
        items: [12000, 18000].map((applied, index) => ({
          id: `REC-CNC-09-${index}`,
          workCenterId: `WC-CNC-0${index + 1}`,
          accountingPeriodCode: '2026-09',
          revision: 2,
          rateRevision: 3,
          currencyCode: 'CNY',
          actualFixedOverheadAmount: 12000,
          actualVariableOverheadAmount: 3600,
          actualTotalOverheadAmount: 15600,
          appliedMachineTicks: 4320000000000,
          appliedMachineHours: 120,
          appliedFixedAmount: applied - 3600,
          appliedVariableAmount: 3600,
          appliedTotalAmount: applied,
          appliedRoundingDifferenceAmount: 0,
          underOverAppliedFixedAmount: 15600 - applied,
          underOverAppliedVariableAmount: 0,
          underOverAppliedTotalAmount: 15600 - applied,
          unallocatedFixedOverheadAmount: Math.max(15600 - applied, 0),
          overAppliedFixedOverheadAmount: Math.max(applied - 15600, 0),
          abnormalDowntimeTicks: 0,
          abnormalDowntimeHours: 0,
          abnormalDowntimeDisposition: 'None',
          isReadyForClose: true,
          reconciliationStatus: 'available',
          unavailableReason: null,
          recordedBy: '财务核算员',
          sourceReference: 'FIN-202609-CNC',
          reason: '月末核对',
          recordedAtUtc: '2026-09-30T08:00:00Z',
        })),
      }
    }
    await route.fulfill({ json: { success: true, data, code: 0, message: '' } })
  })
  await page.goto('/erp/finance/machine-overhead')
  await page.getByLabel('工单编号', { exact: true }).fill('WO-202609-0186')
  await page.getByRole('button', { name: '查询工单', exact: true }).click()
  await expect(page.getByText('合计 CNY 15,600.00', { exact: true })).toBeVisible()
  await page.getByLabel('会计期间', { exact: true }).fill('2026-09')
  await page.getByRole('button', { name: '查询月度差异', exact: true }).click()
  await expect(page.getByText('合计 未分配 +CNY 3,600.00', { exact: true })).toBeVisible()
  await expect(page.getByText('合计 多分配 CNY -2,400.00', { exact: true })).toBeVisible()
  const output = requireBrowserEvidenceOutputDir()
  await mkdir(output, { recursive: true })
  await page.screenshot({ path: path.join(output, 'machine-overhead-finance.png'), fullPage: true })
  await page.getByText('结算追溯', { exact: true }).click()
  await expect(page.getByText('SET-0186-2 / 2', { exact: true })).toBeVisible()
  await page.screenshot({ path: path.join(output, 'machine-overhead-lineage.png'), fullPage: true })
})
