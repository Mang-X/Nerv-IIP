import type {
  BusinessConsoleTelemetryOeeAggregateBucket,
  BusinessConsoleTelemetryOeeAggregateDimension,
} from '@nerv-iip/api-client'

export type OeeReportDimension = BusinessConsoleTelemetryOeeAggregateDimension

export type OeeBucketIdentity = readonly [
  dimension: BusinessConsoleTelemetryOeeAggregateDimension | null,
  dimensionValue: string | null,
  siteCode: string | null,
  workshopCode: string | null,
  lineCode: string | null,
  businessDate: string | null,
  bucketStartUtc: string | null,
  bucketEndUtc: string | null,
]

export interface OeeTableRow {
  key: string
  identity: OeeBucketIdentity
  dimension: BusinessConsoleTelemetryOeeAggregateDimension | null
  primaryLabel: string
  hierarchyLabel: string
  businessDateLabel: string
  windowLabel: string
  oeeRate: number | null
  availabilityRate: number | null
  performanceRate: number | null
  qualityRate: number | null
  deviceCount: number
  isDegraded: boolean
  degradedReasons: string[]
}

export interface OeeTrendPoint extends Record<string, string | number> {
  time: string
  oee: number
  availability: number
  performance: number
  quality: number
}

export interface OeeTrendSeries {
  key: 'oee' | 'availability' | 'performance' | 'quality'
  label: string
}

export interface OeeTrendGroup {
  key: string
  siteCode: string | null
  siteLabel: string
  bucketCount: number
  pointCount: number
  omittedCount: number
  points: OeeTrendPoint[]
  series: OeeTrendSeries[]
}

export interface OeeReportPresentation {
  trendGroups: OeeTrendGroup[]
  tableRows: OeeTableRow[]
  trendBucketCount: number
  trendPointCount: number
  omittedTrendBucketCount: number
  tablePageCount: number
  tableTotal: number
}

export function presentOeeReport(input: {
  dimension: OeeReportDimension
  trendBuckets: readonly BusinessConsoleTelemetryOeeAggregateBucket[]
  tableBuckets: readonly BusinessConsoleTelemetryOeeAggregateBucket[]
  tableTotal: number
}): OeeReportPresentation {
  const trendGroups = input.dimension === 'day' ? presentDayTrendGroups(input.trendBuckets) : []
  const trendPointCount = trendGroups.reduce((total, group) => total + group.pointCount, 0)
  const omittedTrendBucketCount = trendGroups.reduce(
    (total, group) => total + group.omittedCount,
    0,
  )

  return {
    trendGroups,
    tableRows: input.tableBuckets.map(presentTableRow),
    trendBucketCount: input.trendBuckets.length,
    trendPointCount,
    omittedTrendBucketCount,
    tablePageCount: input.tableBuckets.length,
    tableTotal: input.tableTotal,
  }
}

function presentDayTrendGroups(
  buckets: readonly BusinessConsoleTelemetryOeeAggregateBucket[],
): OeeTrendGroup[] {
  const bucketsBySite = new Map<string, BusinessConsoleTelemetryOeeAggregateBucket[]>()
  for (const bucket of buckets) {
    const groupKey = JSON.stringify(['day-site', nullable(bucket.siteCode)])
    const siteBuckets = bucketsBySite.get(groupKey) ?? []
    siteBuckets.push(bucket)
    bucketsBySite.set(groupKey, siteBuckets)
  }

  return [...bucketsBySite.entries()]
    .map(([key, siteBuckets]) => {
      const orderedBuckets = siteBuckets.slice().sort(compareTrendBuckets)
      const siteCode = nullable(orderedBuckets[0]?.siteCode)
      const siteLabel = siteCode?.trim() || '未解析站点'
      const completeBuckets = orderedBuckets.filter(hasCompleteRates)
      return {
        key,
        siteCode,
        siteLabel,
        bucketCount: orderedBuckets.length,
        pointCount: completeBuckets.length,
        omittedCount: orderedBuckets.length - completeBuckets.length,
        points: completeBuckets.map((bucket) => ({
          time: shortBusinessDate(bucket),
          oee: percentNumber(bucket.oeeRate),
          availability: percentNumber(bucket.availabilityRate),
          performance: percentNumber(bucket.performanceRate),
          quality: percentNumber(bucket.qualityRate),
        })),
        series: trendSeries(siteLabel),
      }
    })
    .sort(
      (left, right) =>
        compareOrdinal(left.siteCode, right.siteCode) || compareOrdinal(left.key, right.key),
    )
}

function presentTableRow(bucket: BusinessConsoleTelemetryOeeAggregateBucket): OeeTableRow {
  const identity = bucketIdentity(bucket)
  return {
    key: JSON.stringify(identity),
    identity,
    dimension: bucket.dimension ?? null,
    primaryLabel: primaryLabel(bucket),
    hierarchyLabel: hierarchyLabel(bucket),
    businessDateLabel:
      bucket.dimension === 'day' || bucket.dimension === 'shift'
        ? bucket.businessDate?.trim() || '未解析业务日'
        : '—',
    windowLabel: formatWindow(bucket.bucketStartUtc, bucket.bucketEndUtc),
    oeeRate: bucket.oeeRate ?? null,
    availabilityRate: bucket.availabilityRate ?? null,
    performanceRate: bucket.performanceRate ?? null,
    qualityRate: bucket.qualityRate ?? null,
    deviceCount: bucket.deviceCount ?? 0,
    isDegraded: bucket.isDegraded ?? false,
    degradedReasons: [...(bucket.degradedReasons ?? [])],
  }
}

function bucketIdentity(bucket: BusinessConsoleTelemetryOeeAggregateBucket): OeeBucketIdentity {
  return [
    bucket.dimension ?? null,
    nullable(bucket.dimensionValue),
    nullable(bucket.siteCode),
    nullable(bucket.workshopCode),
    nullable(bucket.lineCode),
    nullable(bucket.businessDate),
    nullable(bucket.bucketStartUtc),
    nullable(bucket.bucketEndUtc),
  ]
}

function primaryLabel(bucket: BusinessConsoleTelemetryOeeAggregateBucket) {
  if (bucket.dimension === 'day') return bucket.businessDate?.trim() || '未解析业务日'
  const fallback =
    bucket.dimension === 'shift'
      ? '未解析班次'
      : bucket.dimension === 'workCenter'
        ? '未解析工作中心'
        : bucket.dimension === 'line'
          ? '未解析产线'
          : bucket.dimension === 'workshop'
            ? '未解析车间'
            : '未解析维度'
  return bucket.dimensionValue?.trim() || fallback
}

function hierarchyLabel(bucket: BusinessConsoleTelemetryOeeAggregateBucket) {
  const parts = [`站点 ${displayCode(bucket.siteCode, '未解析')}`]
  if (bucket.dimension === 'workshop') return parts.join(' › ')

  if (
    bucket.dimension === 'line' ||
    bucket.dimension === 'workCenter' ||
    bucket.dimension === 'shift'
  ) {
    parts.push(`车间 ${displayCode(bucket.workshopCode, '未解析')}`)
  }
  if (bucket.dimension === 'workCenter' || bucket.dimension === 'shift') {
    parts.push(`产线 ${displayCode(bucket.lineCode, '未解析')}`)
  }
  return parts.join(' › ')
}

function trendSeries(siteLabel: string): OeeTrendSeries[] {
  return [
    { key: 'oee', label: `${siteLabel} · OEE` },
    { key: 'availability', label: `${siteLabel} · 可用率` },
    { key: 'performance', label: `${siteLabel} · 性能率` },
    { key: 'quality', label: `${siteLabel} · 质量率` },
  ]
}

function hasCompleteRates(bucket: BusinessConsoleTelemetryOeeAggregateBucket) {
  return (
    bucket.oeeRate != null &&
    bucket.availabilityRate != null &&
    bucket.performanceRate != null &&
    bucket.qualityRate != null
  )
}

function compareTrendBuckets(
  left: BusinessConsoleTelemetryOeeAggregateBucket,
  right: BusinessConsoleTelemetryOeeAggregateBucket,
) {
  return (
    compareOrdinal(left.bucketStartUtc, right.bucketStartUtc) ||
    compareOrdinal(left.businessDate, right.businessDate) ||
    compareOrdinal(JSON.stringify(bucketIdentity(left)), JSON.stringify(bucketIdentity(right)))
  )
}

function compareOrdinal(left: string | null | undefined, right: string | null | undefined) {
  const normalizedLeft = left ?? ''
  const normalizedRight = right ?? ''
  return normalizedLeft < normalizedRight ? -1 : normalizedLeft > normalizedRight ? 1 : 0
}

function shortBusinessDate(bucket: BusinessConsoleTelemetryOeeAggregateBucket) {
  const businessDate = bucket.businessDate?.match(/^(\d{4})-(\d{2})-(\d{2})$/)
  if (businessDate) return `${Number(businessDate[2])}/${Number(businessDate[3])}`
  if (!bucket.bucketStartUtc) return '—'
  const date = new Date(bucket.bucketStartUtc)
  return Number.isNaN(date.getTime()) ? '—' : `${date.getUTCMonth() + 1}/${date.getUTCDate()}`
}

function formatWindow(start?: string | null, end?: string | null) {
  return `${formatDateTime(start)} – ${formatDateTime(end)}`
}

function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleString('zh-CN', { timeZone: 'UTC', hour12: false })
}

function percentNumber(value: number | null | undefined) {
  return Number((value! * 100).toFixed(1))
}

function displayCode(value: string | null | undefined, fallback: string) {
  return value?.trim() || fallback
}

function nullable(value: string | null | undefined) {
  return value ?? null
}
