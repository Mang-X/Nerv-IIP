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
    expect(
      report.trendGroups.map((group) =>
        group.segments.flatMap((segment) =>
          segment.runs.flatMap((run) => run.points.map((point) => point.time)),
        ),
      ),
    ).toEqual([
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
    expect(report.trendGroups.map((group) => group.segments.length)).toEqual([1, 1])
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
    expect(report.trendGroups[0]?.segments).toHaveLength(1)
    expect(report.trendGroups[0]?.segments[0]).toMatchObject({
      bucketCount: 31,
      pointCount: 30,
      omittedCount: 1,
    })
    expect(report.trendGroups[0]?.segments[0]?.runs.map((run) => run.points.length)).toEqual([
      25, 5,
    ])
    expect(
      report.trendGroups[0]?.segments[0]?.runs.some((run) =>
        run.points.some((point) => point.time === '8/26'),
      ),
    ).toBe(false)
  })

  it('separates equal site and business date buckets with different windows', () => {
    const report = presentOeeReport({
      dimension: 'day',
      trendBuckets: [
        bucket({
          businessDate: '2026-08-01',
          bucketStartUtc: '2026-07-31T15:00:00.000Z',
          bucketEndUtc: '2026-08-01T15:00:00.000Z',
        }),
        bucket({
          businessDate: '2026-08-01',
          bucketStartUtc: '2026-07-31T16:00:00.000Z',
          bucketEndUtc: '2026-08-01T16:00:00.000Z',
        }),
      ],
      tableBuckets: [],
      tableTotal: 2,
    })

    const segments = report.trendGroups[0]?.segments ?? []
    expect(segments).toHaveLength(2)
    expect(segments.map((segment) => segment.runs[0]?.displayMode)).toEqual(['point', 'point'])
    expect(segments.map((segment) => segment.firstWindowLabel)).toEqual([
      '2026-07-31 15:00:00 UTC – 2026-08-01 15:00:00 UTC',
      '2026-07-31 16:00:00 UTC – 2026-08-01 16:00:00 UTC',
    ])
    expect(segments.map((segment) => segment.buckets[0]?.identity)).toEqual([
      [
        'day',
        'SITE-A',
        'SITE-A',
        null,
        null,
        '2026-08-01',
        '2026-07-31T15:00:00.000Z',
        '2026-08-01T15:00:00.000Z',
      ],
      [
        'day',
        'SITE-A',
        'SITE-A',
        null,
        null,
        '2026-08-01',
        '2026-07-31T16:00:00.000Z',
        '2026-08-01T16:00:00.000Z',
      ],
    ])
    expect(
      new Set(segments.flatMap((segment) => segment.runs[0]?.points.map((point) => point.key)))
        .size,
    ).toBe(2)
  })

  it('does not join touching windows that repeat a business date', () => {
    const report = presentOeeReport({
      dimension: 'day',
      trendBuckets: [
        bucket({
          businessDate: '2026-08-01',
          bucketStartUtc: '2026-08-01T00:00:00.000Z',
          bucketEndUtc: '2026-08-01T12:00:00.000Z',
        }),
        bucket({
          businessDate: '2026-08-01',
          bucketStartUtc: '2026-08-01T12:00:00.000Z',
          bucketEndUtc: '2026-08-02T00:00:00.000Z',
        }),
      ],
      tableBuckets: [],
      tableTotal: 2,
    })

    expect(report.trendGroups[0]?.segments).toHaveLength(2)
  })

  it.each([
    [
      'overlapping windows',
      { businessDate: '2026-08-02', bucketStartUtc: '2026-08-01T15:00:00.000Z' },
    ],
    ['window gaps', { businessDate: '2026-08-02', bucketStartUtc: '2026-08-01T17:00:00.000Z' }],
    [
      'calendar date gaps',
      {
        businessDate: '2026-08-03',
        bucketStartUtc: '2026-08-01T16:00:00.000Z',
        bucketEndUtc: '2026-08-02T16:00:00.000Z',
      },
    ],
  ])('starts a new segment for %s', (_case, nextOverrides) => {
    const report = presentOeeReport({
      dimension: 'day',
      trendBuckets: [
        bucket({
          businessDate: '2026-08-01',
          bucketStartUtc: '2026-07-31T16:00:00.000Z',
          bucketEndUtc: '2026-08-01T16:00:00.000Z',
        }),
        bucket({
          bucketEndUtc: '2026-08-02T16:00:00.000Z',
          ...nextOverrides,
        }),
      ],
      tableBuckets: [],
      tableTotal: 2,
    })

    expect(report.trendGroups[0]?.segments).toHaveLength(2)
  })

  it.each([
    [
      'spring DST 23-hour day',
      [
        ['2026-03-07', '2026-03-07T05:00:00.000Z', '2026-03-08T05:00:00.000Z'],
        ['2026-03-08', '2026-03-08T05:00:00.000Z', '2026-03-09T04:00:00.000Z'],
        ['2026-03-09', '2026-03-09T04:00:00.000Z', '2026-03-10T04:00:00.000Z'],
      ],
    ],
    [
      'fall DST 25-hour day',
      [
        ['2026-10-31', '2026-10-31T04:00:00.000Z', '2026-11-01T04:00:00.000Z'],
        ['2026-11-01', '2026-11-01T04:00:00.000Z', '2026-11-02T05:00:00.000Z'],
        ['2026-11-02', '2026-11-02T05:00:00.000Z', '2026-11-03T05:00:00.000Z'],
      ],
    ],
  ])('keeps %s in one continuous segment', (_case, windows) => {
    const report = presentOeeReport({
      dimension: 'day',
      trendBuckets: windows.map(([businessDate, bucketStartUtc, bucketEndUtc]) =>
        bucket({ businessDate, bucketStartUtc, bucketEndUtc }),
      ),
      tableBuckets: [],
      tableTotal: windows.length,
    })

    expect(report.trendGroups[0]?.segments).toHaveLength(1)
    expect(report.trendGroups[0]?.segments[0]?.runs).toHaveLength(1)
    expect(report.trendGroups[0]?.segments[0]?.runs[0]).toMatchObject({
      displayMode: 'line',
      points: expect.any(Array),
    })
    expect(report.trendGroups[0]?.segments[0]?.runs[0]?.points).toHaveLength(3)
    expect(report.trendGroups[0]?.segments[0]?.buckets.map((bucket) => bucket.windowLabel)).toEqual(
      windows.map(
        ([, start, end]) =>
          `${start.replace('T', ' ').replace('.000Z', ' UTC')} – ${end
            .replace('T', ' ')
            .replace('.000Z', ' UTC')}`,
      ),
    )
  })

  it('keeps missing buckets in the segment while breaking drawable runs', () => {
    const buckets = Array.from({ length: 5 }, (_, index) =>
      bucket({
        businessDate: `2026-08-0${index + 1}`,
        bucketStartUtc: `2026-08-0${index + 1}T00:00:00.000Z`,
        bucketEndUtc: `2026-08-0${index + 2}T00:00:00.000Z`,
        ...(index === 2 ? { oeeRate: null, performanceRate: null } : {}),
      }),
    )
    const report = presentOeeReport({
      dimension: 'day',
      trendBuckets: buckets,
      tableBuckets: buckets,
      tableTotal: 5,
    })

    expect(report.trendGroups[0]?.segments).toHaveLength(1)
    expect(report.trendGroups[0]?.segments[0]).toMatchObject({
      bucketCount: 5,
      pointCount: 4,
      omittedCount: 1,
    })
    expect(
      report.trendGroups[0]?.segments[0]?.buckets.map((bucket) => bucket.hasCompleteRates),
    ).toEqual([true, true, false, true, true])
    expect(report.trendGroups[0]?.segments[0]?.runs.map((run) => run.points.length)).toEqual([2, 2])
  })

  it('marks singleton runs as points and all-missing segments without drawable runs', () => {
    const complete = bucket({ businessDate: '2026-08-02' })
    const missing = (businessDate: string) =>
      bucket({ businessDate, oeeRate: null, performanceRate: null })
    const report = presentOeeReport({
      dimension: 'day',
      trendBuckets: [missing('2026-08-01'), complete, missing('2026-08-03')],
      tableBuckets: [],
      tableTotal: 3,
    })
    const allMissing = presentOeeReport({
      dimension: 'day',
      trendBuckets: [missing('2026-08-01'), missing('2026-08-02')],
      tableBuckets: [],
      tableTotal: 2,
    })

    expect(report.trendGroups[0]?.segments[0]?.runs).toEqual([
      expect.objectContaining({ displayMode: 'point', points: [expect.any(Object)] }),
    ])
    expect(allMissing.trendGroups[0]?.segments[0]).toMatchObject({
      bucketCount: 2,
      pointCount: 0,
      omittedCount: 2,
      runs: [],
    })
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
        '2026-08-02T00:00:00.000Z',
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
  const nextDay = new Date(`${day}T00:00:00.000Z`)
  nextDay.setUTCDate(nextDay.getUTCDate() + 1)
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
    bucketEndUtc: nextDay.toISOString(),
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
