// 演示摘要按工厂生成；真实指标各自读取授权范围并独立刷新。
import { requireDemoMode } from '@/data/config'
import type { LauncherSummary } from '@/data/contracts/launcher'

export async function fetchLauncherSummary(
  factoryId: string,
  workshopIds: string[] | 'all' = 'all',
): Promise<LauncherSummary> {
  requireDemoMode()
  const { buildLauncherSummary } = await import('@/data/mock/launcher')
  await new Promise((r) => setTimeout(r, 240))
  return buildLauncherSummary(factoryId, workshopIds)
}

export { fetchHallMetric, HallMetricUnavailable, type HallMetric } from '@/data/real/launcher'
