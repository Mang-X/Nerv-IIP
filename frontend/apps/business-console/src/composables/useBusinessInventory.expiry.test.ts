import { describe, expect, it } from 'vitest'
import { buildInventoryExpiryAlertsQuery } from './useBusinessInventory'

describe('Inventory 近效期查询', () => {
  it('默认查询 30 天内且排除零可用量，保留服务端过滤字段', () => {
    expect(
      buildInventoryExpiryAlertsQuery({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        siteCode: 'SITE-A',
        skuCode: 'SKU-001',
        locationCode: 'A-01',
      }),
    ).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      siteCode: 'SITE-A',
      skuCode: 'SKU-001',
      locationCode: 'A-01',
      nearExpiryThresholdDays: 30,
      includeZeroAvailable: false,
    })
  })
})
