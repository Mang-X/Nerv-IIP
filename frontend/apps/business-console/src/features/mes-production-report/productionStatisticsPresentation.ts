import type { BusinessConsoleMesProductionStatisticsBucket } from '@nerv-iip/api-client'
import { readFaceText } from '@/utils/readFace'

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
  return {
    dimension: row.dimension,
    dimensionValueLabel: readFaceText(row.dimensionValue),
    businessDate: row.businessDate,
    shiftCode: row.shiftCode,
    workCenterLabel: readFaceText(row.workCenterId),
    skuLabel: readFaceText(row.skuId),
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
