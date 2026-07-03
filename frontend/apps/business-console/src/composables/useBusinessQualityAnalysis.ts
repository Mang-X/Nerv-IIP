import type { BusinessConsoleQualityItem } from '@nerv-iip/api-client'

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
  byWorkCenter: QualityAnalysisBucket[]
  byDevice: QualityAnalysisBucket[]
  bySourceType: QualityAnalysisBucket[]
}

export const QUALITY_ANALYSIS_FACADE_AUDIT = [
  {
    capability: '质量趋势 / 缺陷统计聚合',
    qualityService: '缺口',
    businessConsoleFacade: '缺口',
    frontendHandling: '仅展示当前 NCR 返回窗口的真实派生摘要，不宣称全量趋势。',
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
    defectPareto: summarizeBy(ncrs, (item) => item.defectReason),
    bySku: summarizeBy(ncrs, (item) => item.skuCode),
    byWorkCenter: summarizeBy(ncrs, (item) => item.workCenterId),
    byDevice: summarizeBy(ncrs, (item) => item.deviceAssetId),
    bySourceType: summarizeBy(ncrs, (item) => item.sourceType),
  }
}

function summarizeBy(
  ncrs: ReadonlyArray<BusinessConsoleQualityItem>,
  getLabel: (item: BusinessConsoleQualityItem) => string | null | undefined,
): QualityAnalysisBucket[] {
  const buckets = new Map<string, { count: number, defectQuantity: number }>()

  for (const item of ncrs) {
    const label = displayLabel(getLabel(item))
    const bucket = buckets.get(label) ?? { count: 0, defectQuantity: 0 }
    bucket.count += 1
    bucket.defectQuantity += toNumber(item.defectQuantity)
    buckets.set(label, bucket)
  }

  return [...buckets.entries()]
    .map(([label, bucket]) => ({
      label,
      count: bucket.count,
      defectQuantity: bucket.defectQuantity,
      sharePercent: ncrs.length ? Math.round((bucket.count / ncrs.length) * 100) : 0,
    }))
    .sort((left, right) =>
      unknownRank(left.label) - unknownRank(right.label) ||
      right.count - left.count ||
      right.defectQuantity - left.defectQuantity ||
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
