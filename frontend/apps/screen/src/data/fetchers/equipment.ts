// 设备监控取数：mock（默认 dev fallback）↔ real（business-console 真实 facade）。
// 模式由 VITE_SCREEN_DATA_MODE 切换（见 data/config.ts）；契约与页面不随模式变化。
// 真实取数适配见 data/real/equipment.ts；分批约束/演示数据流见 data/mock/equipment.ts。
import { IS_REAL_DATA } from '@/data/config'
import type { DeviceDetail, DeviceParamsTick, EquipmentOverview } from '@/data/contracts/equipment'
import { buildDeviceDetail, buildEquipmentOverview, buildParamsTick } from '@/data/mock/equipment'
import {
  fetchRealDeviceDetail,
  fetchRealDeviceParamsTick,
  fetchRealEquipmentOverview,
} from '@/data/real/equipment'

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

export async function fetchEquipmentOverview(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<EquipmentOverview> {
  if (IS_REAL_DATA) return fetchRealEquipmentOverview(factoryId)
  await delay(260)
  return buildEquipmentOverview(factoryId, workshopIds)
}

/** 参数快刷（高频轮询，只刷格上参数）；deviceIds = 当前视野内设备集。
 *  真实模式无 historian（#570/#689），返回空集（不产出演示参数）。 */
export async function fetchDeviceParamsTick(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
  deviceIds?: string[],
): Promise<DeviceParamsTick> {
  if (IS_REAL_DATA) return fetchRealDeviceParamsTick()
  await delay(120)
  return buildParamsTick(factoryId, workshopIds, deviceIds)
}

/** 设备详情按需取（点击设备格触发）。 */
export async function fetchDeviceDetail(
  deviceId: string,
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<DeviceDetail | null> {
  if (IS_REAL_DATA) return fetchRealDeviceDetail(deviceId)
  await delay(220)
  return buildDeviceDetail(deviceId, factoryId, workshopIds)
}
