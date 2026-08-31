import type { BusinessConsoleMesProductionStatisticsBucket } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import { presentProductionStatisticsRow } from './productionStatisticsPresentation'

const bucket: BusinessConsoleMesProductionStatisticsBucket = {
  dimension: 'workCenter',
  dimensionValue: 'WC-CNC-01',
  businessDate: null,
  shiftCode: null,
  workCenterId: 'WC-CNC-01',
  skuId: null,
  goodQuantity: 96.5,
  scrapQuantity: 2,
  reworkQuantity: 1.5,
  totalOutputQuantity: 100,
  goodRate: 0.965,
  scrapRate: 0.02,
  reworkRate: 0.015,
  productionReportCount: 7,
  resolutionStatus: 'resolved',
  degradedReasons: [],
}

describe('production statistics presentation', () => {
  it('preserves distinct human-readable producer codes for table and export consumers', () => {
    expect(presentProductionStatisticsRow(bucket)).toMatchObject({
      dimensionValueLabel: 'WC-CNC-01',
      workCenterLabel: 'WC-CNC-01',
      skuLabel: '—',
    })
    expect(
      presentProductionStatisticsRow({
        ...bucket,
        dimensionValue: 'WC-CNC-02',
        workCenterId: 'WC-CNC-02',
      }).dimensionValueLabel,
    ).toBe('WC-CNC-02')
  })

  it('does not expose system UUIDs through any presentation field', () => {
    const systemId = '58d80fc0-77d9-4213-8fe6-09cd0f595776'
    const presented = presentProductionStatisticsRow({
      ...bucket,
      dimensionValue: systemId,
      workCenterId: systemId,
      skuId: systemId,
    })

    expect(presented).toMatchObject({
      dimensionValueLabel: '—',
      workCenterLabel: '—',
      skuLabel: '—',
    })
    expect(JSON.stringify(presented)).not.toContain(systemId)
  })
})
