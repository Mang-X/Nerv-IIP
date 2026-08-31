import type { BusinessConsoleMesProductionStatisticsBucket } from '@nerv-iip/api-client'
import { readFaceText } from '@/utils/readFace'

const WORK_CENTER_CODE_PATTERN = /^WC-[A-Z0-9][A-Z0-9._-]*$/
const SKU_CODE_PATTERN = /^SKU-[A-Z0-9][A-Z0-9._-]*$/

export interface ProductionStatisticsPresentationRow extends Omit<
  BusinessConsoleMesProductionStatisticsBucket,
  'dimensionValue' | 'workCenterId' | 'skuId'
> {
  dimensionValueLabel: string
  workCenterLabel: string
  skuLabel: string
}

export function presentProductionStatisticsRow(
  row: BusinessConsoleMesProductionStatisticsBucket,
): ProductionStatisticsPresentationRow {
  const group = presentGroup(row)
  return {
    dimension: row.dimension,
    dimensionValueLabel: group.dimensionValueLabel,
    businessDate: row.businessDate,
    shiftCode: row.shiftCode,
    workCenterLabel: group.workCenterLabel,
    skuLabel: group.skuLabel,
    goodQuantity: row.goodQuantity,
    scrapQuantity: row.scrapQuantity,
    reworkQuantity: row.reworkQuantity,
    totalOutputQuantity: row.totalOutputQuantity,
    goodRate: row.goodRate,
    scrapRate: row.scrapRate,
    reworkRate: row.reworkRate,
    productionReportCount: row.productionReportCount,
    resolutionStatus: row.resolutionStatus,
    degradedReasons: row.degradedReasons,
  }
}

function presentGroup(row: BusinessConsoleMesProductionStatisticsBucket) {
  if (row.dimension === 'workCenter') {
    const code = supportedBusinessCode(row.dimensionValue, WORK_CENTER_CODE_PATTERN, '工作中心')
    return { dimensionValueLabel: code, workCenterLabel: code, skuLabel: '—' }
  }
  if (row.dimension === 'sku') {
    const code = supportedBusinessCode(row.dimensionValue, SKU_CODE_PATTERN, '物料')
    return { dimensionValueLabel: code, workCenterLabel: '—', skuLabel: code }
  }
  return {
    dimensionValueLabel: readFaceText(row.dimensionValue),
    workCenterLabel: '—',
    skuLabel: '—',
  }
}

function supportedBusinessCode(value: string | null | undefined, pattern: RegExp, label: string) {
  const code = value?.trim()
  if (!code || !pattern.test(code)) throw new Error(`${label}分组缺少受支持的业务编码。`)
  return code
}
