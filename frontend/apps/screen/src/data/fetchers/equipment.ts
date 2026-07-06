// 设备监控取数。#570 就绪后只换本文件：改为真实端点（设备状态按
// deviceAssetIds ≤ 50/批 分批请求后合并，chunkIds 见 mock/equipment.ts），
// 页面与契约均不动。
import type { DeviceDetail, EquipmentOverview } from '@/data/contracts/equipment'
import { buildDeviceDetail, buildEquipmentOverview } from '@/data/mock/equipment'

export async function fetchEquipmentOverview(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<EquipmentOverview> {
  await new Promise((r) => setTimeout(r, 260))
  return buildEquipmentOverview(factoryId, workshopIds)
}

/** 设备详情按需取（点击设备格触发）；未来对应单设备端点。 */
export async function fetchDeviceDetail(
  deviceId: string,
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<DeviceDetail | null> {
  await new Promise((r) => setTimeout(r, 220))
  return buildDeviceDetail(deviceId, factoryId, workshopIds)
}
