import type { BusinessConsoleMesProductionStatisticsBucket } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'
import {
  createProductionStatisticsCsv,
  productionStatisticsCsvFilename,
} from './productionStatisticsCsv'
import { presentProductionStatisticsRow } from './productionStatisticsPresentation'

const bucket: BusinessConsoleMesProductionStatisticsBucket = {
  dimension: 'workCenter',
  dimensionValue: 'WC-CNC-01',
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
    const csv = createProductionStatisticsCsv([presentProductionStatisticsRow(bucket)])

    expect(csv.charCodeAt(0)).toBe(0xfeff)
    expect(csv).toContain(
      '聚合维度,维度值,业务日,班次,总产出,合格量,合格率,报废量,报废率,返修量,返修率,报工数,数据状态,降级原因',
    )
    expect(csv).toContain('工作中心,WC-CNC-01,2026-08-30,SHIFT-DAY')
    expect(csv).toContain(',100,96.5,0.965,2,0.02,1.5,0.015,7,数据不完整,历史站点时区缺失')
    expect(csv).not.toContain('organizationId')
    expect(csv).not.toContain('environmentId')
    expect(csv).not.toContain('workCenterId')
    expect(csv).not.toContain('skuId')
  })

  it('keeps every work center and SKU aggregate distinguishable by its business code', () => {
    const csv = createProductionStatisticsCsv([
      presentProductionStatisticsRow(bucket),
      presentProductionStatisticsRow({
        ...bucket,
        dimension: 'sku',
        dimensionValue: 'SKU-HOUSING-01',
        workCenterId: null,
        skuId: 'SKU-HOUSING-01',
      }),
    ])

    expect(csv).toContain('工作中心,WC-CNC-01,2026-08-30,SHIFT-DAY')
    expect(csv).toContain('物料,SKU-HOUSING-01,2026-08-30,SHIFT-DAY')
  })

  it('refuses to export an aggregate whose only group identity is a system UUID', () => {
    const systemId = '58d80fc0-77d9-4213-8fe6-09cd0f595776'

    expect(() =>
      createProductionStatisticsCsv([
        presentProductionStatisticsRow({
          ...bucket,
          dimensionValue: systemId,
          workCenterId: systemId,
        }),
      ]),
    ).toThrow('工作中心分组缺少可导出的业务标识')
  })

  it('builds a filename from the selected UTC window and dimension', () => {
    expect(
      productionStatisticsCsvFilename({
        dimension: 'sku',
        windowStartUtc: new Date(2026, 7, 1).toISOString(),
        windowEndUtc: new Date(2026, 8, 1).toISOString(),
      }),
    ).toBe('生产日报_物料_2026-08-01_2026-08-31.csv')
  })
})
