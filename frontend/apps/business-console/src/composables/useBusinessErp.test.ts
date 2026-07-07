import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrderMutationOptions,
  listBusinessConsoleErpPurchaseOrdersQueryOptions,
  listBusinessConsoleErpPurchaseRequisitionsQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessErp, useErpPurchaseRequisitions } from './useBusinessErp'

const coladaState = vi.hoisted(() => ({
  queryFactoriesById: new Map<string, () => unknown>(),
  queryDataById: new Map<string, unknown>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
  mutateAsync: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(),
  })),
  listBusinessConsoleErpPurchaseOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleErpPurchaseOrders' }],
    query: vi.fn(),
  })),
  listBusinessConsoleErpPurchaseRequisitionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleErpPurchaseRequisitions' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryFactoriesById.set(id, optionsFactory)

    const refetch = vi.fn()
    coladaState.refetchById.set(id, refetch)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
  useMutation: vi.fn((options) => ({
    ...options,
    mutateAsync: coladaState.mutateAsync,
    error: shallowRef(),
    isLoading: shallowRef(false),
  })),
}))

describe('business ERP composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryFactoriesById.clear()
    coladaState.queryDataById.clear()
    coladaState.refetchById.clear()
    coladaState.mutateAsync.mockReset()
    coladaState.mutateAsync.mockResolvedValue({ success: true, data: { status: 'PurchaseOrderCreated', purchaseOrderNo: 'PO-001' } })
  })

  it('converts open purchase requisitions through the generated gateway mutation', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-002', environmentId: 'prod' })

    const erp = useErpPurchaseRequisitions()
    await erp.convertToPurchaseOrder(['PR-001', 'PR-002'])

    expect(convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrderMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutateAsync).toHaveBeenCalledWith({
      body: expect.objectContaining({
        organizationId: 'org-002',
        environmentId: 'prod',
        purchaseRequisitionNos: ['PR-001', 'PR-002'],
      }),
    })
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
    coladaState.queryDataById.set('listBusinessConsoleErpPurchaseRequisitions', {
      success: true,
      data: {
        items: [
          {
            requisitionNo: 'PR-001',
            suggestionId: 'suggestion-001',
            skuCode: 'SKU-RM-001',
            status: 'Open',
          },
        ],
        total: 7,
      },
    })

    const { filters, purchaseOrders, purchaseOrdersTotal, purchaseRequisitions, purchaseRequisitionsTotal } = useBusinessErp()
    filters.purchaseOrderStatus = 'Released'
    filters.purchaseRequisitionStatus = 'Open'
    filters.keyword = 'SUP-001'
    filters.skip = 20
    filters.take = 10

    coladaState.queryFactoriesById.get('listBusinessConsoleErpPurchaseOrders')?.()
    coladaState.queryFactoriesById.get('listBusinessConsoleErpPurchaseRequisitions')?.()

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
    expect(listBusinessConsoleErpPurchaseRequisitionsQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-002',
        environmentId: 'prod',
        status: 'Open',
        keyword: 'SUP-001',
        skip: 20,
        take: 10,
      },
    })
    expect(purchaseOrders.value).toHaveLength(1)
    expect(purchaseOrders.value[0]?.supplierCode).toBe('SUP-001')
    expect(purchaseOrders.value[0]?.receiptReadiness).toBe('partially-received')
    expect(purchaseOrdersTotal.value).toBe(42)
    expect(purchaseRequisitions.value).toHaveLength(1)
    expect(purchaseRequisitions.value[0]?.requisitionNo).toBe('PR-001')
    expect(purchaseRequisitionsTotal.value).toBe(7)
  })

  it('does not refresh procurement documents when business context is empty', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: '', environmentId: '' })
    const erp = useBusinessErp()

    await erp.refreshProcurementDocuments()

    expect(coladaState.refetchById.get('listBusinessConsoleErpPurchaseOrders')).not.toHaveBeenCalled()
    expect(coladaState.refetchById.get('listBusinessConsoleErpPurchaseRequisitions')).not.toHaveBeenCalled()
  })
})
