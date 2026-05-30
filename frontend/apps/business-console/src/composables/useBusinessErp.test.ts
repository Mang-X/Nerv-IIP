import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  listBusinessConsoleErpPurchaseOrdersQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessErp } from './useBusinessErp'

const coladaState = vi.hoisted(() => ({
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
            supplierName: '密封件供应商',
            receiptReadiness: 'incoming-inspection',
          },
        ],
      },
    })

    const { purchaseOrders } = useBusinessErp()

    expect(listBusinessConsoleErpPurchaseOrdersQueryOptions).toHaveBeenCalledWith({
      query: { organizationId: 'org-002', environmentId: 'prod' },
    })
    expect(purchaseOrders.value).toHaveLength(1)
    expect(purchaseOrders.value[0]?.supplierCode).toBe('SUP-001')
    expect(purchaseOrders.value[0]?.receiptReadiness).toBe('incoming-inspection')
  })
})
