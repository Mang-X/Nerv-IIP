// 仓储物流取数：mock（默认 dev fallback）↔ real（business-console WMS 作业域 facade）。
// 模式由 VITE_SCREEN_DATA_MODE 切换（见 data/config.ts）；契约与页面不随模式变化。
// 真实取数适配见 data/real/warehouse.ts（入库/出库/上架/拣货/盘点/WCS 六个分页 list 前端聚合，
// 龄期/超时/失败龄期按真实时间戳计算）；库存余额/流水/预留无读面（库存半屏待 #570）。
import { IS_REAL_DATA } from '@/data/config'
import type { WarehouseBoard, WarehouseOpsTick } from '@/data/contracts/warehouse'
import { buildWarehouseBoard, buildWarehouseOpsTick } from '@/data/mock/warehouse'
import { fetchRealWarehouseBoard, fetchRealWarehouseOpsTick } from '@/data/real/warehouse'

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/** 主数据（KPI + 出入库进度 + 盘点汇总），10s 轮询。 */
export async function fetchWarehouseBoard(factoryId = 'F01'): Promise<WarehouseBoard> {
  if (IS_REAL_DATA) return fetchRealWarehouseBoard(factoryId)
  await delay(240)
  return buildWarehouseBoard(new Date(), factoryId)
}

/** 任务看板 + WCS 高频 tick，15s 轮询（mock 与主数据同源纯函数，口径必然一致）。 */
export async function fetchWarehouseOpsTick(factoryId = 'F01'): Promise<WarehouseOpsTick> {
  if (IS_REAL_DATA) return fetchRealWarehouseOpsTick(factoryId)
  await delay(180)
  return buildWarehouseOpsTick(new Date(), factoryId)
}
