// 质量看板取数。#570 就绪后只换本文件：检验单/NCR 明细 GET → 前端聚合改真实
// 聚合端点，并在此做缺陷码映射（Quality reason_code ↔ MES defect_code 口径归一）；
// 页面与契约均不动。
import type { QualityBoard } from '@/data/contracts/quality'
import { buildQualityBoard } from '@/data/mock/quality'

export async function fetchQualityBoard(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<QualityBoard> {
  await new Promise((r) => setTimeout(r, 230))
  return buildQualityBoard(factoryId, workshopIds)
}
