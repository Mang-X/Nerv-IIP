import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const scenarioSource = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), '../e2e/issue1912-real-machine-walkthrough.spec.ts'),
  'utf8',
)

describe('NERV-1127 / GitHub #1912 real-machine walkthrough contract', () => {
  it('starts only from the reserved walkthrough facts and keeps downstream numbers stable', () => {
    expect(scenarioSource).toContain("const RFQ_NO = 'RFQ-WALK-001'")
    expect(scenarioSource).toContain("const SUPPLIER_QUOTATION_NO = 'SQ-WALK-001'")
    expect(scenarioSource).toContain("const SALES_QUOTATION_NO = 'QUO-WALK-001'")
    expect(scenarioSource).toContain("const PURCHASE_ORDER_NO = 'PO-WALK-001'")
    expect(scenarioSource).toContain("const PURCHASE_RECEIPT_NO = 'PR-WALK-001'")
    expect(scenarioSource).toContain("const SALES_ORDER_NO = 'SO-WALK-001'")
    expect(scenarioSource).toContain("const DELIVERY_ORDER_NO = 'DO-WALK-001'")
    expect(scenarioSource).not.toContain('/api/business-console/v1/approval/templates')
  })

  it('uses public business writes for the two chains and records every cross-boundary identifier', () => {
    expect(scenarioSource).toContain('/api/business-console/v1/erp/procurement/supplier-quotations')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/procurement/purchase-orders')
    expect(scenarioSource).toContain('/api/business-console/v1/approval/chains')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/procurement/purchase-receipts')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/sales/sales-orders')
    expect(scenarioSource).toContain('/api/business-console/v1/planning/demands')
    expect(scenarioSource).toContain('/api/business-console/v1/planning/mrp-runs')
    expect(scenarioSource).toContain('/api/business-console/v1/planning/suggestions')
    expect(scenarioSource).toContain('/api/business-console/v1/mes/work-orders')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/sales/delivery-orders')
    expect(scenarioSource).toContain('/api/business-console/v1/erp/finance/receivables')
    expect(scenarioSource).toContain('stableKey')
    expect(scenarioSource).toContain('sourceObject')
    expect(scenarioSource).toContain('downstreamObject')
  })

  it('proves real page responses and rendered rows instead of treating API success as UI evidence', () => {
    expect(scenarioSource).toContain('page.waitForResponse')
    expect(scenarioSource).toContain('response.status() === 200')
    expect(scenarioSource).toContain('await page.goto')
    expect(scenarioSource).toContain('await expect(row).toContainText')
    expect(scenarioSource).toContain('emptyText')
    expect(scenarioSource).toContain('await page.screenshot')
    expect(scenarioSource).toContain('failedRequests')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_EVIDENCE_PATH')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_WORLD_ENABLED')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_HISTORY_ENABLED')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_SCALE_ORDER_COUNT')
  })

  it('fails closed when the real stack or evidence destination is not supplied', () => {
    expect(scenarioSource).toContain('NERV_IIP_PLAYWRIGHT_BASE_URL')
    expect(scenarioSource).toContain('NERV_IIP_FULLSTACK_ADMIN_PASSWORD')
    expect(scenarioSource).toContain('NERV_IIP_ISSUE_1912_EVIDENCE_PATH')
    expect(scenarioSource).toContain('requires a managed full-stack session and an evidence destination')
    expect(scenarioSource).toContain("conclusion: 'not-verified'")
  })
})
