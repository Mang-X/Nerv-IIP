import {
  queryBusinessConsoleQualityProcessCapabilityQueryOptions,
  queryBusinessConsoleQualitySpcControlChartQueryOptions,
  type BusinessConsoleQualityItem,
  type BusinessConsoleQualityProcessCapabilityEnvelope,
  type BusinessConsoleQualityProcessCapabilityResponse,
  type BusinessConsoleQualitySpcControlChartEnvelope,
  type BusinessConsoleQualitySpcControlChartResponse,
  type BusinessConsoleQualitySpcRuleViolation,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  type BusinessContextFields,
} from './businessContextBinding'

// Keep aligned with SpcCalculation.NoCompleteSubgroupMessage in the Quality service.
const SPC_WARMUP_MESSAGE = 'SPC control chart requires at least one complete subgroup.'

export interface QualityAnalysisBucket {
  label: string
  count: number
  defectQuantity: number
  sharePercent: number
}

export interface QualityAnalysisSummary {
  sampledNcrCount: number
  totalNcrCount: number
  openNcrCount: number
  dispositionedNcrCount: number
  closedNcrCount: number
  totalDefectQuantity: number
  sampleNotice: string
  defectPareto: QualityAnalysisBucket[]
  bySku: QualityAnalysisBucket[]
  bySourceType: QualityAnalysisBucket[]
}

interface QualitySpcBaseChartRow extends Record<string, number | string> {
  subgroup: string
  centerLine: number
  ucl: number
  lcl: number
}

export interface QualitySpcXbarChartRow extends QualitySpcBaseChartRow {
  xbar: number
}

export interface QualitySpcRangeChartRow extends QualitySpcBaseChartRow {
  range: number
}

export interface QualitySpcViolationMarker {
  key: string
  label: string
  message: string
  targetId: string
  startSubgroupIndex: number
  endSubgroupIndex: number
}

export interface QualitySpcChartPresentation {
  xbarRows: QualitySpcXbarChartRow[]
  rangeRows: QualitySpcRangeChartRow[]
  violationMarkers: QualitySpcViolationMarker[]
}

export interface QualitySpcFilters extends BusinessContextFields {
  skuCode: string
  characteristicCode: string
  workCenterId: string
  subgroupSize: number
  take: number
}

export function useQualitySpcAnalysis(initialFilters: Partial<QualitySpcFilters> = {}) {
  const filters = defaultSpcFilters(initialFilters)

  const controlChartQuery = useQuery(() => ({
    ...queryBusinessConsoleQualitySpcControlChartQueryOptions({
      query: toSpcQuery(filters),
    }),
    enabled: hasSpcScope(filters),
  }))
  const capabilityQuery = useQuery(() => ({
    ...queryBusinessConsoleQualityProcessCapabilityQueryOptions({
      query: toCapabilityQuery(filters),
    }),
    enabled: hasSpcScope(filters),
  }))
  const spcWarmup = computed(() => isSpcWarmupEnvelope(controlChartQuery.data.value))

  return {
    capability: computed(() => unwrapCapability(capabilityQuery.data.value)),
    filters,
    refreshSpc: () => refreshSpcQueries(filters, controlChartQuery, capabilityQuery),
    spcChart: computed(() => unwrapControlChart(controlChartQuery.data.value)),
    spcError: computed(() =>
      spcWarmup.value
        ? capabilityQuery.error.value
        : (controlChartQuery.error.value ?? capabilityQuery.error.value),
    ),
    spcPending: computed(
      () => controlChartQuery.isLoading.value || capabilityQuery.isLoading.value,
    ),
    spcReady: computed(() => hasSpcScope(filters)),
    spcViolations: computed(
      () => unwrapControlChart(controlChartQuery.data.value)?.ruleViolations ?? [],
    ),
    spcWarmup,
  }
}

export function buildQualityAnalysisSummary(
  ncrs: ReadonlyArray<BusinessConsoleQualityItem>,
  totalNcrCount: number,
): QualityAnalysisSummary {
  const sampledNcrCount = ncrs.length
  const totalDefectQuantity = ncrs.reduce((total, item) => total + toNumber(item.defectQuantity), 0)

  return {
    sampledNcrCount,
    totalNcrCount,
    openNcrCount: countByStatus(ncrs, 'open'),
    dispositionedNcrCount: countByStatus(ncrs, 'dispositioned'),
    closedNcrCount: countByStatus(ncrs, 'closed'),
    totalDefectQuantity,
    sampleNotice: buildSampleNotice(sampledNcrCount, totalNcrCount),
    defectPareto: summarizeBy(ncrs, (item) => item.defectReason, {
      shareMetric: 'defectQuantity',
      sortMetric: 'defectQuantity',
      unknownLast: false,
    }),
    bySku: summarizeBy(ncrs, (item) => item.skuCode),
    bySourceType: summarizeBy(ncrs, (item) => item.sourceType),
  }
}

export function buildSpcChartPresentation(
  chart: BusinessConsoleQualitySpcControlChartResponse,
): QualitySpcChartPresentation {
  const limits = chart.controlLimits
  const subgroups = chart.subgroups ?? []

  if (!hasCompleteSpcControlLimits(limits)) {
    return {
      xbarRows: [],
      rangeRows: [],
      violationMarkers: buildViolationMarkers(chart.ruleViolations ?? []),
    }
  }

  return {
    xbarRows: subgroups.flatMap((subgroup) =>
      isFiniteNumber(subgroup.index) && isFiniteNumber(subgroup.xbar)
        ? [
            {
              subgroup: `子组 ${subgroup.index}`,
              xbar: subgroup.xbar,
              centerLine: limits.centerLine,
              ucl: limits.xbarUpperControlLimit,
              lcl: limits.xbarLowerControlLimit,
            },
          ]
        : [],
    ),
    rangeRows: subgroups.flatMap((subgroup) =>
      isFiniteNumber(subgroup.index) && isFiniteNumber(subgroup.range)
        ? [
            {
              subgroup: `子组 ${subgroup.index}`,
              range: subgroup.range,
              centerLine: limits.averageRange,
              ucl: limits.rangeUpperControlLimit,
              lcl: limits.rangeLowerControlLimit,
            },
          ]
        : [],
    ),
    violationMarkers: buildViolationMarkers(chart.ruleViolations ?? []),
  }
}

export function buildParetoChartRows(buckets: ReadonlyArray<QualityAnalysisBucket>) {
  return buckets.map((bucket) => ({
    reason: bucket.label,
    defectQuantity: bucket.defectQuantity,
  }))
}

export function formatQualityQuantity(value: number) {
  return Number.isInteger(value) ? String(value) : value.toFixed(2)
}

export function spcViolationTargetId(violation: QualitySpcViolation, position = 0) {
  const rule = toDomIdSegment(violation.rule)
  const start = violation.startSubgroupIndex ?? 0
  const end = violation.endSubgroupIndex ?? start
  const ordinal = Math.max(0, position) + 1
  return `spc-violation-${rule}-${start}-${end}-${ordinal}`
}

function buildViolationMarkers(
  violations: ReadonlyArray<BusinessConsoleQualitySpcRuleViolation>,
): QualitySpcViolationMarker[] {
  return violations.map((violation, position) => {
    const start = violation.startSubgroupIndex ?? 0
    const end = violation.endSubgroupIndex ?? start
    const rule = violation.rule ?? 'unknown'
    const targetId = spcViolationTargetId(violation, position)
    return {
      key: targetId,
      label: start === end ? `子组 ${start}` : `子组 ${start}–${end}`,
      message: violation.message?.trim() || '检测到 SPC 判异',
      targetId,
      startSubgroupIndex: start,
      endSubgroupIndex: end,
    }
  })
}

export function hasCompleteSpcControlLimits(
  limits: BusinessConsoleQualitySpcControlChartResponse['controlLimits'],
): limits is NonNullable<BusinessConsoleQualitySpcControlChartResponse['controlLimits']> & {
  centerLine: number
  averageRange: number
  xbarUpperControlLimit: number
  xbarLowerControlLimit: number
  rangeUpperControlLimit: number
  rangeLowerControlLimit: number
} {
  return Boolean(
    limits &&
    isFiniteNumber(limits.centerLine) &&
    isFiniteNumber(limits.averageRange) &&
    isFiniteNumber(limits.xbarUpperControlLimit) &&
    isFiniteNumber(limits.xbarLowerControlLimit) &&
    isFiniteNumber(limits.rangeUpperControlLimit) &&
    isFiniteNumber(limits.rangeLowerControlLimit),
  )
}

function toDomIdSegment(value: string | null | undefined) {
  return (
    value
      ?.trim()
      .toLowerCase()
      .replace(/[^\w-]+/g, '-')
      .replace(/^-+|-+$/g, '') || 'rule'
  )
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

type BucketMetric = 'count' | 'defectQuantity'

interface SummarizeByOptions {
  shareMetric?: BucketMetric
  sortMetric?: BucketMetric
  unknownLast?: boolean
}

function summarizeBy(
  ncrs: ReadonlyArray<BusinessConsoleQualityItem>,
  getLabel: (item: BusinessConsoleQualityItem) => string | null | undefined,
  options: SummarizeByOptions = {},
): QualityAnalysisBucket[] {
  const buckets = new Map<string, { count: number; defectQuantity: number }>()
  const shareMetric = options.shareMetric ?? 'count'
  const sortMetric = options.sortMetric ?? 'count'
  const fallbackSortMetric: BucketMetric = sortMetric === 'count' ? 'defectQuantity' : 'count'
  const unknownOrder = options.unknownLast ?? true

  for (const item of ncrs) {
    const label = displayLabel(getLabel(item))
    const bucket = buckets.get(label) ?? { count: 0, defectQuantity: 0 }
    bucket.count += 1
    bucket.defectQuantity += toNumber(item.defectQuantity)
    buckets.set(label, bucket)
  }

  const shareDenominator = [...buckets.values()].reduce(
    (total, bucket) => total + bucket[shareMetric],
    0,
  )

  return [...buckets.entries()]
    .map(([label, bucket]) => ({
      label,
      count: bucket.count,
      defectQuantity: bucket.defectQuantity,
      sharePercent: shareDenominator
        ? Math.round((bucket[shareMetric] / shareDenominator) * 100)
        : 0,
    }))
    .sort(
      (left, right) =>
        (unknownOrder ? unknownRank(left.label) - unknownRank(right.label) : 0) ||
        right[sortMetric] - left[sortMetric] ||
        right[fallbackSortMetric] - left[fallbackSortMetric] ||
        left.label.localeCompare(right.label, 'zh-Hans-CN'),
    )
}

function countByStatus(ncrs: ReadonlyArray<BusinessConsoleQualityItem>, status: string) {
  return ncrs.filter((item) => item.status?.toLowerCase() === status).length
}

function displayLabel(value: string | null | undefined) {
  const trimmed = value?.trim()
  return trimmed || '未填'
}

function unknownRank(label: string) {
  return label === '未填' ? 1 : 0
}

function toNumber(value: number | null | undefined) {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0
}

function buildSampleNotice(sampledNcrCount: number, totalNcrCount: number) {
  if (totalNcrCount > sampledNcrCount) {
    return `当前后端返回窗口覆盖 ${sampledNcrCount} / ${totalNcrCount} 条 NCR；这是当前窗口分析，不是全量趋势。`
  }

  return `当前后端返回窗口覆盖 ${sampledNcrCount} / ${totalNcrCount} 条 NCR。`
}

function defaultSpcFilters(initial: Partial<QualitySpcFilters> = {}): QualitySpcFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skuCode: '',
      characteristicCode: '',
      workCenterId: '',
      subgroupSize: 5,
      take: 50,
      ...initial,
    }),
  )
}

function hasSpcScope(filters: QualitySpcFilters) {
  return (
    hasBusinessContext(filters) &&
    filters.skuCode.trim().length > 0 &&
    filters.characteristicCode.trim().length > 0 &&
    filters.workCenterId.trim().length > 0
  )
}

function toSpcQuery(filters: QualitySpcFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    skuCode: filters.skuCode.trim(),
    characteristicCode: filters.characteristicCode.trim(),
    workCenterId: filters.workCenterId.trim(),
    subgroupSize: toPositiveInteger(filters.subgroupSize, 5),
    take: toPositiveInteger(filters.take, 50),
  }
}

function toCapabilityQuery(filters: QualitySpcFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    skuCode: filters.skuCode.trim(),
    characteristicCode: filters.characteristicCode.trim(),
    workCenterId: filters.workCenterId.trim(),
    subgroupSize: toPositiveInteger(filters.subgroupSize, 5),
    take: toPositiveInteger(filters.take, 50),
  }
}

function unwrapControlChart(
  envelope: BusinessConsoleQualitySpcControlChartEnvelope | undefined,
): BusinessConsoleQualitySpcControlChartResponse | null {
  return envelope?.success ? (envelope.data ?? null) : null
}

function unwrapCapability(
  envelope: BusinessConsoleQualityProcessCapabilityEnvelope | undefined,
): BusinessConsoleQualityProcessCapabilityResponse | null {
  return envelope?.success ? (envelope.data ?? null) : null
}

function refreshSpcQueries(
  filters: QualitySpcFilters,
  controlChartQuery: { refetch: () => Promise<unknown> },
  capabilityQuery: { refetch: () => Promise<unknown> },
) {
  return hasSpcScope(filters)
    ? Promise.all([controlChartQuery.refetch(), capabilityQuery.refetch()])
    : Promise.resolve(undefined)
}

export type QualitySpcViolation = BusinessConsoleQualitySpcRuleViolation

function toPositiveInteger(value: number | string, fallback: number) {
  const parsed = typeof value === 'number' ? value : Number.parseInt(value, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function isSpcWarmupEnvelope(envelope: BusinessConsoleQualitySpcControlChartEnvelope | undefined) {
  return envelope?.success === false && (envelope.message ?? '').includes(SPC_WARMUP_MESSAGE)
}
