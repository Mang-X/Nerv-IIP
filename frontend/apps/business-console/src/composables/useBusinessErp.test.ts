import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  listBusinessConsoleErpPurchaseOrdersQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessErp } from './useBusinessErp'

const coladaState = vi.hoisted(() => ({
  queryFactoriesById: new Map<string, () => unknown>(),
  queryDataById: new Map<string, unknown>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleErpPurchaseOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleErpPurchaseOrders' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryFactoriesById.set(id, optionsFactory)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('business ERP composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryFactoriesById.clear()
    coladaState.queryDataById.clear()
  })

  it('loads procurement purchase orders through the generated gateway client', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-002', environmentId: 'prod' })
    coladaState.queryDataById.set('listBusinessConsoleErpPurchaseOrders', {
      success: true,
      data: {
        items: [
          {
            purchaseOrderNo: 'PO-001',
            supplierCode: 'SUP-001',
            receiptReadiness: 'partially-received',
          },
        ],
        total: 42,
      },
    })

    const { filters, purchaseOrders, purchaseOrdersTotal } = useBusinessErp()
    filters.status = 'Released'
    filters.keyword = 'SUP-001'
    filters.skip = 20
    filters.take = 10

    coladaState.queryFactoriesById.get('listBusinessConsoleErpPurchaseOrders')?.()

    expect(listBusinessConsoleErpPurchaseOrdersQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-002',
        environmentId: 'prod',
        status: 'Released',
        keyword: 'SUP-001',
        skip: 20,
        take: 10,
      },
    })
    expect(purchaseOrders.value).toHaveLength(1)
    expect(purchaseOrders.value[0]?.supplierCode).toBe('SUP-001')
    expect(purchaseOrders.value[0]?.receiptReadiness).toBe('partially-received')
    expect(purchaseOrdersTotal.value).toBe(42)
  })
})
