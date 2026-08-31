import type {
  BusinessConsoleMesProductionStatisticsBucket,
  BusinessConsoleMesProductionStatisticsDimension,
  BusinessConsoleMesProductionStatisticsDegradedReason,
} from '@nerv-iip/api-client'

const HEADERS = [
  '聚合维度',
  '维度值',
  '业务日',
  '班次',
  '工作中心',
  '物料',
  '总产出',
  '合格量',
  '合格率',
  '报废量',
  '报废率',
  '返修量',
  '返修率',
  '报工数',
  '数据状态',
  '降级原因',
] as const

export const PRODUCTION_STATISTICS_DIMENSION_LABELS: Record<
  BusinessConsoleMesProductionStatisticsDimension,
  string
> = {
  day: '业务日',
  shift: '班次',
  workCenter: '工作中心',
  sku: '物料',
}

const DEGRADATION_LABELS: Record<BusinessConsoleMesProductionStatisticsDegradedReason, string> = {
  historicalDimensionLegacyUnresolved: '历史维度无法解析',
  historicalTimezoneMissing: '历史站点时区缺失',
  historicalTimezoneInvalid: '历史站点时区无效',
  historicalShiftDefinitionMissing: '历史班次定义缺失',
  historicalShiftDefinitionInvalid: '历史班次定义无效',
  historicalReportOutsideShiftWindow: '历史报工不在班次窗口内',
  historicalLocalTimeInvalid: '历史本地时间无效',
  historicalLocalTimeAmbiguous: '历史本地时间存在歧义',
  historicalDimensionSnapshotDegraded: '历史维度快照不完整',
  workCenterMissing: '工作中心缺失',
  nonPositiveTotalOutput: '总产出非正，质量比率缺失',
}

export function describeProductionStatisticsDegradation(
  reason: BusinessConsoleMesProductionStatisticsDegradedReason,
) {
  return DEGRADATION_LABELS[reason]
}

function csvCell(value: string | number | null | undefined) {
  const text = value == null ? '' : String(value)
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text
}

export function createProductionStatisticsCsv(
  rows: BusinessConsoleMesProductionStatisticsBucket[],
): string {
  const body = rows.map((row) =>
    [
      PRODUCTION_STATISTICS_DIMENSION_LABELS[row.dimension],
      row.dimensionValue,
      row.businessDate,
      row.shiftCode,
      row.workCenterId,
      row.skuId,
      row.totalOutputQuantity,
      row.goodQuantity,
      row.goodRate,
      row.scrapQuantity,
      row.scrapRate,
      row.reworkQuantity,
      row.reworkRate,
      row.productionReportCount,
      row.resolutionStatus === 'resolved' ? '完整' : '数据不完整',
      row.degradedReasons.map(describeProductionStatisticsDegradation).join('；'),
    ]
      .map(csvCell)
      .join(','),
  )
  return `\ufeff${[HEADERS.join(','), ...body].join('\r\n')}\r\n`
}

export function productionStatisticsCsvFilename(filters: {
  dimension: BusinessConsoleMesProductionStatisticsDimension
  windowStartUtc: string
  windowEndUtc: string
}): string {
  return `生产日报_${PRODUCTION_STATISTICS_DIMENSION_LABELS[filters.dimension]}_${filters.windowStartUtc.slice(0, 10)}_${filters.windowEndUtc.slice(0, 10)}.csv`
}
