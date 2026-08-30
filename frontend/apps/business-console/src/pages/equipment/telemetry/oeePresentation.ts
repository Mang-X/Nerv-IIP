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

export interface OeeTrendPoint {
  key: string
  identity: OeeBucketIdentity
  time: string
  businessDateLabel: string
  windowLabel: string
  oee: number
  availability: number
  performance: number
  quality: number
}

export interface OeeTrendChartRow extends Record<string, string | number> {
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
  segments: OeeTrendSegment[]
  series: OeeTrendSeries[]
}

export interface OeeTrendRun {
  key: string
  displayMode: 'line' | 'point'
  points: OeeTrendPoint[]
  chartData: OeeTrendChartRow[]
}

export interface OeeTrendBucketDetail {
  key: string
  identity: OeeBucketIdentity
  businessDateLabel: string
  windowLabel: string
  hasCompleteRates: boolean
}

export interface OeeTrendSegment {
  key: string
  ordinal: number
  businessDateStartLabel: string
  businessDateEndLabel: string
  firstWindowLabel: string
  lastWindowLabel: string
  bucketCount: number
  pointCount: number
  omittedCount: number
  buckets: OeeTrendBucketDetail[]
  runs: OeeTrendRun[]
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
      const segments = presentTrendSegments(orderedBuckets)
      return {
        key,
        siteCode,
        siteLabel,
        bucketCount: orderedBuckets.length,
        pointCount: segments.reduce((total, segment) => total + segment.pointCount, 0),
        omittedCount: segments.reduce((total, segment) => total + segment.omittedCount, 0),
        segments,
        series: trendSeries(siteLabel),
      }
    })
    .sort(
      (left, right) =>
        compareOrdinal(left.siteCode, right.siteCode) || compareOrdinal(left.key, right.key),
    )
}

function presentTrendSegments(
  orderedBuckets: readonly BusinessConsoleTelemetryOeeAggregateBucket[],
): OeeTrendSegment[] {
  const segmentBuckets: BusinessConsoleTelemetryOeeAggregateBucket[][] = []
  for (const bucket of orderedBuckets) {
    const segment = segmentBuckets.find((candidate) => canContinueSegment(candidate, bucket))
    if (segment) segment.push(bucket)
    else segmentBuckets.push([bucket])
  }

  return segmentBuckets
    .sort((left, right) => compareTrendBuckets(left[0]!, right[0]!))
    .map((buckets, index) => presentTrendSegment(buckets, index + 1))
}

function canContinueSegment(
  segment: readonly BusinessConsoleTelemetryOeeAggregateBucket[],
  bucket: BusinessConsoleTelemetryOeeAggregateBucket,
) {
  const previous = segment.at(-1)
  if (!previous || !isSameOrNextBusinessDate(previous.businessDate, bucket.businessDate)) {
    return false
  }
  if (
    segment.some((existing) => nullable(existing.businessDate) === nullable(bucket.businessDate))
  ) {
    return false
  }
  const previousEnd = Date.parse(previous.bucketEndUtc ?? '')
  const currentStart = Date.parse(bucket.bucketStartUtc ?? '')
  return Number.isFinite(previousEnd) && previousEnd === currentStart
}

function isSameOrNextBusinessDate(previous?: string | null, current?: string | null) {
  const previousDate = parseBusinessDate(previous)
  const currentDate = parseBusinessDate(current)
  return (
    previousDate !== null &&
    currentDate !== null &&
    currentDate >= previousDate &&
    currentDate <= previousDate + 24 * 60 * 60 * 1000
  )
}

function parseBusinessDate(value?: string | null) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value ?? '')) return null
  const parsed = Date.parse(`${value}T00:00:00.000Z`)
  return Number.isFinite(parsed) ? parsed : null
}

function presentTrendSegment(
  buckets: readonly BusinessConsoleTelemetryOeeAggregateBucket[],
  ordinal: number,
): OeeTrendSegment {
  const runs = presentTrendRuns(buckets)
  const pointCount = runs.reduce((total, run) => total + run.points.length, 0)
  const identities = buckets.map(bucketIdentity)
  return {
    key: JSON.stringify(['day-segment', identities]),
    ordinal,
    businessDateStartLabel: displayBusinessDate(buckets[0]?.businessDate),
    businessDateEndLabel: displayBusinessDate(buckets.at(-1)?.businessDate),
    firstWindowLabel: formatExactUtcWindow(buckets[0]?.bucketStartUtc, buckets[0]?.bucketEndUtc),
    lastWindowLabel: formatExactUtcWindow(
      buckets.at(-1)?.bucketStartUtc,
      buckets.at(-1)?.bucketEndUtc,
    ),
    bucketCount: buckets.length,
    pointCount,
    omittedCount: buckets.length - pointCount,
    buckets: buckets.map(presentTrendBucketDetail),
    runs,
  }
}

function presentTrendBucketDetail(
  bucket: BusinessConsoleTelemetryOeeAggregateBucket,
): OeeTrendBucketDetail {
  const identity = bucketIdentity(bucket)
  return {
    key: JSON.stringify(identity),
    identity,
    businessDateLabel: displayBusinessDate(bucket.businessDate),
    windowLabel: formatExactUtcWindow(bucket.bucketStartUtc, bucket.bucketEndUtc),
    hasCompleteRates: hasCompleteRates(bucket),
  }
}

function presentTrendRuns(
  buckets: readonly BusinessConsoleTelemetryOeeAggregateBucket[],
): OeeTrendRun[] {
  const bucketRuns: BusinessConsoleTelemetryOeeAggregateBucket[][] = []
  let currentRun: BusinessConsoleTelemetryOeeAggregateBucket[] = []
  for (const bucket of buckets) {
    if (!hasCompleteRates(bucket)) {
      if (currentRun.length > 0) bucketRuns.push(currentRun)
      currentRun = []
      continue
    }
    currentRun.push(bucket)
  }
  if (currentRun.length > 0) bucketRuns.push(currentRun)

  return bucketRuns.map((run) => {
    const points = run.map(presentTrendPoint)
    return {
      key: JSON.stringify(['day-run', run.map(bucketIdentity)]),
      displayMode: run.length >= 2 ? 'line' : 'point',
      points,
      chartData: points.map(({ time, oee, availability, performance, quality }) => ({
        time,
        oee,
        availability,
        performance,
        quality,
      })),
    }
  })
}

function presentTrendPoint(bucket: BusinessConsoleTelemetryOeeAggregateBucket): OeeTrendPoint {
  const identity = bucketIdentity(bucket)
  return {
    key: JSON.stringify(identity),
    identity,
    time: shortBusinessDate(bucket),
    businessDateLabel: displayBusinessDate(bucket.businessDate),
    windowLabel: formatExactUtcWindow(bucket.bucketStartUtc, bucket.bucketEndUtc),
    oee: percentNumber(bucket.oeeRate),
    availability: percentNumber(bucket.availabilityRate),
    performance: percentNumber(bucket.performanceRate),
    quality: percentNumber(bucket.qualityRate),
  }
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

function displayBusinessDate(value?: string | null) {
  return value?.trim() || '未解析业务日'
}

function formatExactUtcWindow(start?: string | null, end?: string | null) {
  return `${formatExactUtc(start)} – ${formatExactUtc(end)}`
}

function formatExactUtc(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return `${date.toISOString().slice(0, 19).replace('T', ' ')} UTC`
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
