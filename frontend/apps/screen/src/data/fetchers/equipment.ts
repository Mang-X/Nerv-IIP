// 设备监控取数。#570 就绪后只换本文件：改为真实端点（设备状态按
// deviceAssetIds ≤ 50/批 分批请求后合并，chunkIds 见 mock/equipment.ts），
// 页面与契约均不动。
import type { EquipmentOverview } from '@/data/contracts/equipment'
import { buildEquipmentOverview } from '@/data/mock/equipment'

export async function fetchEquipmentOverview(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<EquipmentOverview> {
  await new Promise((r) => setTimeout(r, 260))
  return buildEquipmentOverview(factoryId, workshopIds)
}
