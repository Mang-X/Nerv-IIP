import { describe, expect, it } from 'vitest'
import { buildInventoryExpiryAlertsQuery, inventoryExpiryPagingScope } from './useBusinessInventory'

describe('Inventory 近效期查询', () => {
  it('默认查询 30 天内并包含零可用量，保留服务端过滤字段', () => {
    expect(
      buildInventoryExpiryAlertsQuery(
        {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          siteCode: 'SITE-A',
          skuCode: 'SKU-001',
          locationCode: 'A-01',
        },
        2,
        25,
      ),
    ).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      siteCode: 'SITE-A',
      skuCode: 'SKU-001',
      locationCode: 'A-01',
      nearExpiryThresholdDays: 30,
      includeZeroAvailable: true,
      page: 2,
      pageSize: 25,
    })
  })

  it('组织、环境和服务端筛选字段共同定义分页复位范围', () => {
    expect(
      inventoryExpiryPagingScope({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        siteCode: 'SITE-A',
        skuCode: 'SKU-001',
        locationCode: 'A-01',
      }),
    ).toEqual(['org-001', 'env-dev', 'SITE-A', 'SKU-001', 'A-01'])
  })
})
