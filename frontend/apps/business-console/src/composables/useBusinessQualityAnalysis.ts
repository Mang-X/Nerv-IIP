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

export interface QualitySpcFilters extends BusinessContextFields {
  skuCode: string
  characteristicCode: string
  workCenterId: string
  subgroupSize: number
  take: number
}

export const QUALITY_ANALYSIS_FACADE_AUDIT = [
  {
    capability: 'SPC Xbar-R / 过程能力',
    qualityService: '已接入',
    businessConsoleFacade: '已接入',
    frontendHandling: '按 SKU、特性和工作中心查询 SPC 控制图、判异和 Cp/Cpk。',
  },
  {
    capability: 'CAPA 列表 / 详情 / 状态追踪',
    qualityService: '写入口已存在，缺少列表与详情读面',
    businessConsoleFacade: '缺口',
    frontendHandling: '不手写 CAPA 状态；在 NCR 处置中保留审批和关闭路径。',
  },
  {
    capability: 'NCR 处置与关闭',
    qualityService: '已接入',
    businessConsoleFacade: '已接入',
    frontendHandling: 'NCR 页面可提交处置审批链并记录返工、报废、退货关闭引用。',
  },
] as const

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

  return {
    capability: computed(() => unwrapCapability(capabilityQuery.data.value)),
    filters,
    refreshSpc: () => refreshSpcQueries(filters, controlChartQuery, capabilityQuery),
    spcChart: computed(() => unwrapControlChart(controlChartQuery.data.value)),
    spcError: computed(() => controlChartQuery.error.value ?? capabilityQuery.error.value),
    spcPending: computed(() => controlChartQuery.isLoading.value || capabilityQuery.isLoading.value),
    spcReady: computed(() => hasSpcScope(filters)),
    spcViolations: computed(() => unwrapControlChart(controlChartQuery.data.value)?.ruleViolations ?? []),
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
  const buckets = new Map<string, { count: number, defectQuantity: number }>()
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

  const shareDenominator = [...buckets.values()].reduce((total, bucket) => total + bucket[shareMetric], 0)

  return [...buckets.entries()]
    .map(([label, bucket]) => ({
      label,
      count: bucket.count,
      defectQuantity: bucket.defectQuantity,
      sharePercent: shareDenominator ? Math.round((bucket[shareMetric] / shareDenominator) * 100) : 0,
    }))
    .sort((left, right) =>
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
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    skuCode: '',
    characteristicCode: '',
    workCenterId: '',
    subgroupSize: 5,
    take: 50,
    ...initial,
  }))
}

function hasSpcScope(filters: QualitySpcFilters) {
  return hasBusinessContext(filters) &&
    filters.skuCode.trim().length > 0 &&
    filters.characteristicCode.trim().length > 0 &&
    filters.workCenterId.trim().length > 0
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
    take: toPositiveInteger(filters.take, 50),
  }
}

function unwrapControlChart(
  envelope: BusinessConsoleQualitySpcControlChartEnvelope | undefined,
): BusinessConsoleQualitySpcControlChartResponse | null {
  return envelope?.success ? envelope.data ?? null : null
}

function unwrapCapability(
  envelope: BusinessConsoleQualityProcessCapabilityEnvelope | undefined,
): BusinessConsoleQualityProcessCapabilityResponse | null {
  return envelope?.success ? envelope.data ?? null : null
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
