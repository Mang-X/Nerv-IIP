// 车间总览取数。#570 就绪后只换本文件：改为真实端点
// （workshop→line/workCenter 聚合、当班产量、齐套、班组花名册），页面与契约均不动。
import type { WorkshopBoard } from '@/data/contracts/workshop'
import { buildWorkshopBoard } from '@/data/mock/workshop'

export async function fetchWorkshopBoard(
  workshopId: string,
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<WorkshopBoard | null> {
  await new Promise((r) => setTimeout(r, 250))
  return buildWorkshopBoard(workshopId, factoryId, workshopIds)
}
