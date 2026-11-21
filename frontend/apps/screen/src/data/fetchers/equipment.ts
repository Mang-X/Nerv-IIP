// 设备监控取数：mock（显式演示）↔ real（business-console 真实 facade）。
// 模式由 VITE_SCREEN_DATA_MODE 切换（见 data/config.ts）；契约与页面不随模式变化。
// 真实取数适配见 data/real/equipment.ts；分批约束/演示数据流见 data/mock/equipment.ts。
import { getScreenSession } from '@/data/session'
import { IS_REAL_DATA, requireDemoMode } from '@/data/config'
import type { DeviceDetail, DeviceParamsTick, EquipmentOverview } from '@/data/contracts/equipment'
import { buildDeviceDetail, buildEquipmentOverview, buildParamsTick } from '@/data/mock/equipment'
import { DEFAULT_FACTORY_ID } from '@/data/mock/masterdata'
import { fetchRealDeviceDetail } from '@/data/real/equipment'

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

export async function fetchDemoEquipmentOverview(
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
): Promise<EquipmentOverview> {
  requireDemoMode()
  await delay(260)
  return buildEquipmentOverview(factoryId, workshopIds)
}

export async function fetchDemoDeviceParamsTick(
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
  deviceIds?: string[],
): Promise<DeviceParamsTick> {
  requireDemoMode()
  await delay(120)
  return buildParamsTick(factoryId, workshopIds, deviceIds)
}

/** 设备详情按需取（点击设备格触发）。 */
export async function fetchDeviceDetail(
  deviceId: string,
  factoryId = IS_REAL_DATA ? getScreenSession().organizationId : DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
  requestedCenter?: string,
): Promise<DeviceDetail | null> {
  if (IS_REAL_DATA) return fetchRealDeviceDetail(deviceId, requestedCenter)
  await delay(220)
  return buildDeviceDetail(deviceId, factoryId, workshopIds)
}
