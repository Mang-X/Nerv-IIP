import { describe, expect, it } from 'vitest'
import type { BusinessConsoleTelemetryOeeAggregateBucket } from '@nerv-iip/api-client'
import { presentOeeReport, type OeeReportDimension } from './oeePresentation'

describe('OEE aggregate presentation', () => {
  it('separates equal business dates into site-owned trend groups without cross-site lines', () => {
    const report = presentOeeReport({
      dimension: 'day',
      trendBuckets: [
        bucket({ siteCode: 'SITE-B', dimensionValue: 'SITE-B', businessDate: '2026-08-01' }),
        bucket({ siteCode: 'SITE-A', dimensionValue: 'SITE-A', businessDate: '2026-08-02' }),
        bucket({ siteCode: 'SITE-A', dimensionValue: 'SITE-A', businessDate: '2026-08-01' }),
        bucket({ siteCode: 'SITE-B', dimensionValue: 'SITE-B', businessDate: '2026-08-02' }),
      ],
      tableBuckets: [],
      tableTotal: 4,
    })

    expect(report.trendGroups.map((group) => group.siteCode)).toEqual(['SITE-A', 'SITE-B'])
    expect(report.trendGroups.map((group) => group.points.map((point) => point.time))).toEqual([
      ['8/1', '8/2'],
      ['8/1', '8/2'],
    ])
    expect(report.trendGroups[0]?.series.map((series) => series.label)).toEqual([
      'SITE-A · OEE',
      'SITE-A · 可用率',
      'SITE-A · 性能率',
      'SITE-A · 质量率',
    ])
    expect(report.trendGroups[1]?.series[0]?.label).toBe('SITE-B · OEE')
    expect(report.trendPointCount).toBe(4)
  })

  it('keeps a 31-bucket site report while omitting only the incomplete rate point', () => {
    const trendBuckets = Array.from({ length: 31 }, (_, index) =>
      bucket({
        businessDate: `2026-08-${String(index + 1).padStart(2, '0')}`,
        ...(index === 25
          ? {
              oeeRate: null,
              performanceRate: null,
              isDegraded: true,
              degradedReasons: ['theoreticalRateMissingOrAmbiguous'],
            }
          : {}),
      }),
    )

    const report = presentOeeReport({
      dimension: 'day',
      trendBuckets,
      tableBuckets: trendBuckets.slice(0, 20),
      tableTotal: 31,
    })

    expect(report).toMatchObject({
      trendBucketCount: 31,
      trendPointCount: 30,
      omittedTrendBucketCount: 1,
      tablePageCount: 20,
      tableTotal: 31,
    })
    expect(report.trendGroups[0]).toMatchObject({
      bucketCount: 31,
      pointCount: 30,
      omittedCount: 1,
    })
    expect(report.trendGroups[0]?.points.some((point) => point.time === '8/26')).toBe(false)
  })

  it('keeps equal shift codes in different sites and hierarchy as distinct readable rows', () => {
    const rows = [
      bucket({
        dimension: 'shift',
        dimensionValue: 'SHIFT-DAY',
        siteCode: 'SITE-A',
        workshopCode: 'WS-A',
        lineCode: 'LINE-A',
        businessDate: '2026-08-01',
      }),
      bucket({
        dimension: 'shift',
        dimensionValue: 'SHIFT-DAY',
        siteCode: 'SITE-B',
        workshopCode: 'WS-B',
        lineCode: 'LINE-B',
        businessDate: '2026-08-01',
      }),
    ]
    const report = presentOeeReport({
      dimension: 'shift',
      trendBuckets: [],
      tableBuckets: rows,
      tableTotal: 2,
    })

    expect(new Set(report.tableRows.map((row) => row.key)).size).toBe(2)
    expect(report.tableRows.map((row) => row.primaryLabel)).toEqual(['SHIFT-DAY', 'SHIFT-DAY'])
    expect(report.tableRows.map((row) => row.hierarchyLabel)).toEqual([
      '站点 SITE-A › 车间 WS-A › 产线 LINE-A',
      '站点 SITE-B › 车间 WS-B › 产线 LINE-B',
    ])
    expect(report.tableRows.every((row) => row.businessDateLabel === '2026-08-01')).toBe(true)
  })

  it.each<[OeeReportDimension, Partial<BusinessConsoleTelemetryOeeAggregateBucket>, string]>([
    [
      'workCenter',
      { dimensionValue: 'WC-01', siteCode: 'SITE-A', workshopCode: 'WS-A', lineCode: 'LINE-A' },
      '站点 SITE-A › 车间 WS-A › 产线 LINE-A',
    ],
    [
      'line',
      { dimensionValue: 'LINE-01', siteCode: 'SITE-A', workshopCode: 'WS-A', lineCode: 'LINE-01' },
      '站点 SITE-A › 车间 WS-A',
    ],
    [
      'workshop',
      { dimensionValue: 'WS-01', siteCode: 'SITE-A', workshopCode: 'WS-01' },
      '站点 SITE-A',
    ],
    [
      'shift',
      { dimensionValue: 'SHIFT-DAY', siteCode: 'SITE-A', workshopCode: 'WS-A', lineCode: 'LINE-A' },
      '站点 SITE-A › 车间 WS-A › 产线 LINE-A',
    ],
  ])(
    'uses the full %s composite identity and relevant hierarchy',
    (dimension, overrides, hierarchy) => {
      const businessDate = dimension === 'shift' ? '2026-08-01' : null
      const left = bucket({ dimension, businessDate, ...overrides })
      const right = bucket({ dimension, businessDate, ...overrides, siteCode: 'SITE-B' })
      const report = presentOeeReport({
        dimension,
        trendBuckets: [],
        tableBuckets: [left, right],
        tableTotal: 9,
      })

      expect(report.tableRows[0]?.identity).toEqual([
        dimension,
        overrides.dimensionValue ?? 'SITE-A',
        'SITE-A',
        overrides.workshopCode ?? null,
        overrides.lineCode ?? null,
        businessDate,
        '2026-08-01T00:00:00.000Z',
        '2026-08-01T23:59:59.000Z',
      ])
      expect(report.tableRows[0]?.key).not.toBe(report.tableRows[1]?.key)
      expect(report.tableRows[0]?.hierarchyLabel).toBe(hierarchy)
      expect(report.tableTotal).toBe(9)
    },
  )

  it('preserves server page order and degraded missing facts without inventing rate values', () => {
    const second = bucket({
      dimension: 'workCenter',
      dimensionValue: 'WC-02',
      oeeRate: null,
      performanceRate: null,
      isDegraded: true,
      degradedReasons: ['theoreticalRateMissingOrAmbiguous'],
    })
    const first = bucket({ dimension: 'workCenter', dimensionValue: 'WC-01' })
    const report = presentOeeReport({
      dimension: 'workCenter',
      trendBuckets: [],
      tableBuckets: [second, first],
      tableTotal: 27,
    })

    expect(report.tableRows.map((row) => row.primaryLabel)).toEqual(['WC-02', 'WC-01'])
    expect(report.tableRows[0]).toMatchObject({
      oeeRate: null,
      performanceRate: null,
      isDegraded: true,
      degradedReasons: ['theoreticalRateMissingOrAmbiguous'],
    })
    expect(report).toMatchObject({ tablePageCount: 2, tableTotal: 27 })
  })
})

function bucket(
  overrides: Partial<BusinessConsoleTelemetryOeeAggregateBucket> = {},
): BusinessConsoleTelemetryOeeAggregateBucket {
  const businessDate = overrides.businessDate === undefined ? '2026-08-01' : overrides.businessDate
  const day = businessDate ?? '2026-08-01'
  return {
    dimension: 'day',
    dimensionValue: 'SITE-A',
    siteCode: 'SITE-A',
    workshopCode: null,
    lineCode: null,
    workCenterId: null,
    deviceAssetId: null,
    shiftCode: null,
    businessDate,
    bucketStartUtc: `${day}T00:00:00.000Z`,
    bucketEndUtc: `${day}T23:59:59.000Z`,
    deviceCount: 4,
    stateSampleCount: 96,
    productionFactCount: 12,
    availabilityRate: 0.81,
    performanceRate: 0.9,
    qualityRate: 0.97,
    oeeRate: 0.707,
    isDegraded: false,
    degradedReasons: [],
    ...overrides,
  }
}
