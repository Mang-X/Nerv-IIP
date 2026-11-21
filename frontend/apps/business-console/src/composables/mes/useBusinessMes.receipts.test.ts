import { coladaState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import {
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions,
} from '@nerv-iip/api-client'
import { useMesFinishedGoodsReceipts } from '.'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('creates finished goods receipt requests and invalidates dependent lists', async () => {
    const { createReceiptRequest } = useMesFinishedGoodsReceipts()

    await createReceiptRequest({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-1',
      skuId: 'sku-1',
      quantity: 10,
      unitCost: 12.34,
      uomCode: 'EA',
      requestedAtUtc: '2026-05-26T00:00:00.000Z',
      idempotencyKey: 'receipt-1',
    } as never)

    expect(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).toHaveBeenCalled()
    const mutationOptions = vi.mocked(
      createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
    ).mock.results[
      vi.mocked(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).mock.results
        .length - 1
    ]?.value as { mutation: ReturnType<typeof vi.fn> } | undefined
    expect(mutationOptions).toBeDefined()
    const sentBody = mutationOptions!.mutation.mock.calls[0]![0].body
    expect(sentBody).not.toHaveProperty('unitCost')
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(3)
    expect(
      coladaState.invalidateQueries.mock.calls.some(([options]) =>
        options.predicate({ key: [{ _id: 'listBusinessConsoleMesReceivableProducedLots' }] }),
      ),
    ).toBe(true)
  })

  it('tracks concurrent inventory-posting retries independently by requestNo', async () => {
    // 用 deferred promise 控制两次重试的在途状态，验证 A 完成不会误清仍在途的 B。
    const resolvers = new Map<string, () => void>()
    const mocked = vi.mocked(
      retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions,
    )
    mocked.mockReturnValue({
      mutation: (vars: { path: { requestNo: string } }) =>
        new Promise((resolve) =>
          resolvers.set(vars.path.requestNo, () => resolve({ success: true })),
        ),
    } as unknown as ReturnType<
      typeof retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions
    >)
    try {
      const { retryInventoryPosting, isRetrying } = useMesFinishedGoodsReceipts()

      const a = retryInventoryPosting('FGR-A')
      const b = retryInventoryPosting('FGR-B')
      await Promise.resolve()
      expect(isRetrying('FGR-A')).toBe(true)
      expect(isRetrying('FGR-B')).toBe(true)

      resolvers.get('FGR-A')!()
      await a
      // A 完成后仅 A 恢复；仍在途的 B 的 spinner/禁用不被误清。
      expect(isRetrying('FGR-A')).toBe(false)
      expect(isRetrying('FGR-B')).toBe(true)

      resolvers.get('FGR-B')!()
      await b
      expect(isRetrying('FGR-B')).toBe(false)
    } finally {
      mocked.mockReturnValue({
        mutation: vi.fn(async (vars: { body?: unknown }) => ({ success: true, data: vars.body })),
      } as unknown as ReturnType<
        typeof retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions
      >)
    }
  })
})
