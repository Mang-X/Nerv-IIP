// 仓储演示模式的两档轮询。真实模式按入库、出库、盘点和 WCS 分域读取。
import { requireDemoMode } from '@/data/config'
import type { WarehouseBoard, WarehouseOpsTick } from '@/data/contracts/warehouse'
import { DEFAULT_FACTORY_ID } from '@/data/mock/masterdata'
import { buildWarehouseBoard, buildWarehouseOpsTick } from '@/data/mock/warehouse'

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/** 主数据（KPI + 出入库进度 + 盘点汇总），10s 轮询。 */
export async function fetchDemoWarehouseBoard(
  factoryId = DEFAULT_FACTORY_ID,
): Promise<WarehouseBoard> {
  requireDemoMode()
  await delay(240)
  return buildWarehouseBoard(new Date(), factoryId)
}

/** 任务看板 + WCS 高频 tick，15s 轮询（mock 与主数据同源纯函数，口径必然一致）。 */
export async function fetchDemoWarehouseOpsTick(
  factoryId = DEFAULT_FACTORY_ID,
): Promise<WarehouseOpsTick> {
  requireDemoMode()
  await delay(180)
  return buildWarehouseOpsTick(new Date(), factoryId)
}
