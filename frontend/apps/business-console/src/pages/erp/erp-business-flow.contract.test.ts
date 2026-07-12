import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const erpPagesRoot = resolve(__dirname)

const BUSINESS_OBJECT_PAGES = [
  'index.vue',
  'procurement/rfqs.vue',
  'procurement/supplier-quotations.vue',
  'procurement/purchase-orders.vue',
  'procurement/receipts.vue',
  'sales.vue',
  'sales/quotations.vue',
  'sales/orders.vue',
  'sales/deliveries.vue',
  'finance.vue',
  'finance/ar-ap.vue',
  'finance/vouchers.vue',
  'finance/cost-candidates.vue',
] as const

describe('ERP business flow route split', () => {
  for (const page of BUSINESS_OBJECT_PAGES) {
    it(`${page} is a real route page`, () => {
      const path = resolve(erpPagesRoot, page)
      expect(existsSync(path), `${page} should exist`).toBe(true)

      const source = readFileSync(path, 'utf8')
      expect(source).toContain('definePage')
      expect(source).toContain('NvDataTable')
      expect(source).not.toContain('NvTabs')
      expect(source).not.toContain('NvTabsTrigger')
    })
  }
})
