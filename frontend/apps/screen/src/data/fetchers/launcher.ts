// 门厅摘要取数。#570 就绪后只换本文件：改为真实聚合端点，页面与契约均不动。
import type { LauncherSummary } from '@/data/contracts/launcher'
import { buildLauncherSummary } from '@/data/mock/launcher'

export async function fetchLauncherSummary(
  factoryId: string,
  workshopIds: string[] | 'all' = 'all',
): Promise<LauncherSummary> {
  await new Promise((r) => setTimeout(r, 240))
  return buildLauncherSummary(factoryId, workshopIds)
}
