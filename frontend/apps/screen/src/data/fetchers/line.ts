// 产线监控取数。#570 就绪后只换本文件：改为真实端点
// （lineCode→workCenter/device 聚合、当班产量、节拍），页面与契约均不动。
import type { LineBoard, LineSummaryCard } from '@/data/contracts/line'
import { buildLineBoard, buildLineCards } from '@/data/mock/line'

export async function fetchLineCards(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<LineSummaryCard[]> {
  await new Promise((r) => setTimeout(r, 240))
  return buildLineCards(factoryId, workshopIds)
}

export async function fetchLineBoard(
  lineId: string,
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): Promise<LineBoard | null> {
  await new Promise((r) => setTimeout(r, 260))
  return buildLineBoard(lineId, factoryId, workshopIds)
}
