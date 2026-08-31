import type { BusinessConsoleMesProductionStatisticsBucket } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import {
  createProductionStatisticsCsv,
  productionStatisticsCsvFilename,
} from './productionStatisticsCsv'

const bucket: BusinessConsoleMesProductionStatisticsBucket = {
  dimension: 'workCenter',
  dimensionValue: 'WC-CNC-01,"精加工"',
  businessDate: '2026-08-30',
  shiftCode: 'SHIFT-DAY',
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
  resolutionStatus: 'degraded',
  degradedReasons: ['historicalTimezoneMissing'],
}

describe('production statistics CSV', () => {
  it('exports producer quantities and rates with a UTF-8 BOM and stable Chinese columns', () => {
    const csv = createProductionStatisticsCsv([bucket])

    expect(csv.charCodeAt(0)).toBe(0xfeff)
    expect(csv).toContain(
      '聚合维度,维度值,业务日,班次,工作中心,物料,总产出,合格量,合格率,报废量,报废率,返修量,返修率,报工数,数据状态,降级原因',
    )
    expect(csv).toContain('"WC-CNC-01,""精加工"""')
    expect(csv).toContain(',100,96.5,0.965,2,0.02,1.5,0.015,7,数据不完整,历史站点时区缺失')
    expect(csv).not.toContain('organizationId')
    expect(csv).not.toContain('environmentId')
  })

  it('builds a filename from the selected UTC window and dimension', () => {
    expect(
      productionStatisticsCsvFilename({
        dimension: 'sku',
        windowStartUtc: '2026-08-01T00:00:00.000Z',
        windowEndUtc: '2026-08-31T23:59:59.000Z',
      }),
    ).toBe('生产日报_物料_2026-08-01_2026-08-31.csv')
  })
})
