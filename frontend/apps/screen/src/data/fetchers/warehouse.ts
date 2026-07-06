// 仓储物流取数。#570 就绪后只换本文件：改为 WMS 真实分页端点
// （ASN/SO/putaway/pick/cycle-count/WCS 指令 list 前端聚合），页面与契约均不动。
// 注意真实侧口径：WarehouseTask 仅 Open/Completed 两态、无 operator；WCS 无设备号
// 只有 AdapterType；库存余额/流水/预留无读面（一期不做库存资产半屏）。
import type { WarehouseBoard, WarehouseOpsTick } from '@/data/contracts/warehouse'
import { buildWarehouseBoard, buildWarehouseOpsTick } from '@/data/mock/warehouse'

/** 主数据（KPI + 出入库进度 + 盘点汇总），5s 轮询。 */
export async function fetchWarehouseBoard(factoryId = 'F01'): Promise<WarehouseBoard> {
  await new Promise((r) => setTimeout(r, 240))
  return buildWarehouseBoard(new Date(), factoryId)
}

/** 任务看板 + WCS 高频 tick，3s 轮询（与主数据同源纯函数，口径必然一致）。 */
export async function fetchWarehouseOpsTick(factoryId = 'F01'): Promise<WarehouseOpsTick> {
  await new Promise((r) => setTimeout(r, 180))
  return buildWarehouseOpsTick(new Date(), factoryId)
}
