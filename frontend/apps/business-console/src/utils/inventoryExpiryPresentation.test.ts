import { describe, expect, it } from 'vitest'
import {
  formatInventoryExpiryDate,
  inventoryExpiryRowKey,
  summarizeInventoryExpiryAlerts,
} from './inventoryExpiryPresentation'

const availabilityLine = {
  locationCode: 'A-01',
  lotNo: 'LOT-240719-A',
  serialNo: null,
  qualityStatus: 'blocked',
  ownerType: 'owned',
  ownerId: null,
  onHandQuantity: 12,
  reservedQuantity: 12,
  availableQuantity: 0,
}

const expiryAlert = {
  ...availabilityLine,
  skuCode: 'SKU-COOLANT-20L',
  uomCode: 'DRUM',
  siteCode: 'PLANT-SH-01',
  productionDate: '2026-05-20',
  expiryDate: '2026-07-18',
  daysUntilExpiry: -1,
  isExpired: true,
  isNearExpiry: false,
}

describe('Inventory 效期展示工具', () => {
  it('用 SKU 区分同库位同批次行，并为普通可用量行使用筛选 SKU', () => {
    expect(inventoryExpiryRowKey(expiryAlert, '')).not.toBe(
      inventoryExpiryRowKey({ ...expiryAlert, skuCode: 'SKU-ADDITIVE-5L' }, ''),
    )
    expect(inventoryExpiryRowKey(availabilityLine, 'SKU-COOLANT-20L')).toContain('SKU-COOLANT-20L')
    expect(inventoryExpiryRowKey(expiryAlert, '')).not.toBe(
      inventoryExpiryRowKey(
        { ...expiryAlert, productionDate: '2026-05-21', expiryDate: '2026-07-19' },
        '',
      ),
    )
  })

  it('未选择工厂时计数显示空值，查询后区分过期与 30 天内到期', () => {
    expect(summarizeInventoryExpiryAlerts([expiryAlert], false, false).alertCount).toBe('—')
    expect(summarizeInventoryExpiryAlerts([], true, false).alertCount).toBe('—')
    expect(
      summarizeInventoryExpiryAlerts(
        [
          expiryAlert,
          {
            ...expiryAlert,
            lotNo: 'LOT-240719-B',
            expiryDate: '2026-08-10',
            daysUntilExpiry: 22,
            isExpired: false,
            isNearExpiry: true,
          },
        ],
        true,
        true,
      ),
    ).toEqual({ alertCount: 2, expiredCount: 1, nearCount: 1, skuCount: 1 })
  })

  it('缺失日期按数据排版规则显示破折号', () => {
    expect(formatInventoryExpiryDate(undefined)).toBe('—')
    expect(formatInventoryExpiryDate('2026-07-19T12:30:00Z')).toBe('2026-07-19')
  })
})
