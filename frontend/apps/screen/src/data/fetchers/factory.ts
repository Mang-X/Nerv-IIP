// 工厂总览 fetcher。当前 mock；#570 就绪后只换本文件实现，契约与页面不变。
import type { FactoryOverview } from '@/data/contracts/factory'
import { buildFactoryOverview } from '@/data/mock/factory'
import { DEFAULT_FACTORY_ID } from '@/data/mock/masterdata'

export async function fetchFactoryOverview(
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
): Promise<FactoryOverview> {
  await new Promise((resolve) => setTimeout(resolve, 260))
  return buildFactoryOverview(factoryId, workshopIds)
}
