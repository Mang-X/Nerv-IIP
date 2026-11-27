import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { effectScope, nextTick, ref } from 'vue'
import { useInventorySiteStockOverview } from './useInventorySiteStock'

const { readStock } = vi.hoisted(() => ({ readStock: vi.fn() }))
vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleInventoryAvailability: readStock,
}))
vi.mock('../shared/businessContextBinding', () => ({
  bindBusinessContext: () => ({ organizationId: 'org', environmentId: 'env' }),
  hasBusinessContext: () => true,
}))
vi.mock('./useInventoryScope', () => ({
  useInventoryScopeCatalog: () => ({
    skuOptions: ref(
      Array.from({ length: 25 }, (_, id) => ({
        value: `SKU-${id}`,
        label: `物料 ${id}`,
      })),
    ),
    skusPending: ref(false),
    resolveUomCode: () => 'pcs',
  }),
}))

const emptyStock = { data: { success: true, data: { items: [] } } }
let scope: ReturnType<typeof effectScope>
beforeEach(() => {
  scope = effectScope()
  readStock.mockReset()
})
afterEach(() => scope.stop())

it('自动扫描第 25 个物料，保持并发上限并标记失败覆盖', async () => {
  let active = 0
  let peak = 0
  readStock.mockImplementation(async ({ query }) => {
    peak = Math.max(peak, ++active)
    await Promise.resolve()
    active--
    if (query.skuCode === 'SKU-24') return { error: new Error('读取失败') }
    return emptyStock
  })
  const scan = scope.run(() =>
    useInventorySiteStockOverview(() => 'ST001', { autoContinue: true }),
  )!
  await flushPromises()

  expect(peak).toBe(6)
  expect(scan.siteStockScannedCount.value).toBe(25)
  expect(scan.siteStockFailedCount.value).toBe(1)
  expect(scan.siteStockAllRows.value).toHaveLength(24)
  expect(scan.siteStockScanning.value).toBe(false)
  readStock.mockResolvedValue(emptyStock)
  await scan.refreshSiteStock()
  expect(scan.siteStockFailedCount.value).toBe(0)
  expect(scan.siteStockAllRows.value).toHaveLength(25)
})

it('切厂取消在途请求并丢弃迟到的旧批次', async () => {
  const site = ref('SITE-001')
  const oldRequests: Array<{ signal: AbortSignal; finish: () => void }> = []
  readStock.mockImplementation(({ query, signal }) => {
    if (query.siteCode === 'ST001') return Promise.resolve(emptyStock)
    return new Promise((resolve) => {
      oldRequests.push({
        signal,
        finish: () =>
          resolve({
            data: { success: true, data: { items: [{ locationCode: 'OLD' }] } },
          }),
      })
    })
  })
  const scan = scope.run(() =>
    useInventorySiteStockOverview(() => site.value, { autoContinue: true }),
  )!
  expect(oldRequests).toHaveLength(6)
  site.value = 'ST001'
  await nextTick()
  expect(oldRequests.every(({ signal }) => signal.aborted)).toBe(true)
  for (const request of oldRequests) request.finish()
  await flushPromises()

  expect(oldRequests).toHaveLength(6)
  expect(scan.siteStockLines.value).toEqual([])
  expect(scan.siteStockScannedCount.value).toBe(25)
})
