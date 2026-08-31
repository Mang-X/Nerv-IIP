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
  it('keeps two work centers and two SKUs distinct through the shared presentation boundary', () => {
    const rows = [
      presentProductionStatisticsRow(bucket),
      presentProductionStatisticsRow({
        ...bucket,
        dimensionValue: 'WC-CNC-02',
        workCenterId: 'WC-CNC-02',
      }),
      presentProductionStatisticsRow({
        ...bucket,
        dimension: 'sku',
        dimensionValue: 'SKU-HOUSING-01',
        workCenterId: null,
        skuId: 'SKU-HOUSING-01',
      }),
      presentProductionStatisticsRow({
        ...bucket,
        dimension: 'sku',
        dimensionValue: 'SKU-HOUSING-02',
        workCenterId: null,
        skuId: 'SKU-HOUSING-02',
      }),
    ]

    expect(rows.map((row) => row.dimensionValueLabel)).toEqual([
      'WC-CNC-01',
      'WC-CNC-02',
      'SKU-HOUSING-01',
      'SKU-HOUSING-02',
    ])
    expect(rows.map((row) => [row.workCenterLabel, row.skuLabel])).toEqual([
      ['WC-CNC-01', '—'],
      ['WC-CNC-02', '—'],
      ['—', 'SKU-HOUSING-01'],
      ['—', 'SKU-HOUSING-02'],
    ])
  })

  it.each([
    ['workCenter', 'work-center-internal-42'],
    ['sku', 'material-internal-42'],
    ['workCenter', '58d80fc0-77d9-4213-8fe6-09cd0f595776'],
  ] as const)(
    'rejects unsupported %s producer identifiers at the shared boundary',
    (dimension, id) => {
      expect(() =>
        presentProductionStatisticsRow({
          ...bucket,
          dimension,
          dimensionValue: id,
          workCenterId: dimension === 'workCenter' ? id : null,
          skuId: dimension === 'sku' ? id : null,
        }),
      ).toThrow(`${dimension === 'workCenter' ? '工作中心' : '物料'}分组缺少受支持的业务编码。`)
    },
  )
})
