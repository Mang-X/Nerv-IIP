import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
} from '@nerv-iip/api-client'
import { useWmsInboundOrders, useWmsOutboundOrders, useWmsWcsTasks } from './useBusinessWms'
import { useBusinessContextStore } from '@/stores/businessContext'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  queryFactoriesById: new Map<string, () => { enabled?: boolean } & Record<string, unknown>>(),
  queryOptionsById: new Map<string, { enabled?: boolean } & Record<string, unknown>>(),
  queryRefetchById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleWmsInboundOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsInboundOrders' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsReceivingQualityGatesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsReceivingQualityGates' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsSupplierReturnRequestsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsSupplierReturnRequests' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsOutboundOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsOutboundOrders' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsWcsTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsWcsTasks' }],
    query: vi.fn(),
  })),
  completeBusinessConsoleWmsInboundOrderMutationOptions: vi.fn(() => ({})),
  completeBusinessConsoleWmsOutboundOrderMutationOptions: vi.fn(() => ({})),
  completeBusinessConsoleWmsWcsTaskMutationOptions: vi.fn(() => ({})),
  createBusinessConsoleWmsInboundOrderMutationOptions: vi.fn(() => ({})),
  createBusinessConsoleWmsOutboundOrderMutationOptions: vi.fn(() => ({})),
  dispatchBusinessConsoleWmsWcsTaskMutationOptions: vi.fn(() => ({})),
  failBusinessConsoleWmsWcsTaskMutationOptions: vi.fn(() => ({})),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryFactoriesById.set(id, optionsFactory)
    coladaState.queryOptionsById.set(id, options)

    const refetch = vi.fn()
    coladaState.queryRefetchById.set(id, refetch)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
  useMutation: vi.fn(() => ({
    mutateAsync: vi.fn().mockResolvedValue(undefined),
    isLoading: shallowRef(false),
    error: shallowRef(),
  })),
}))

describe('business WMS composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.queryFactoriesById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.queryRefetchById.clear()
  })

  it('lists inbound orders with paging, filters, items, and total', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.set('listBusinessConsoleWmsInboundOrders', {
      success: true,
      data: {
        total: 23,
        items: [{ inboundOrderId: 'in-1', inboundOrderNo: 'IN-001' }],
      },
    })

    const result = useWmsInboundOrders({ skip: 10, take: 20, status: 'Open', keyword: 'IN' })

    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 10,
        take: 20,
        status: 'Open',
        keyword: 'IN',
      },
    })
    expect(result.inboundOrders.value).toEqual([
      { inboundOrderId: 'in-1', inboundOrderNo: 'IN-001' },
    ])
    expect(result.inboundOrdersTotal.value).toBe(23)
  })

  it('lists outbound orders with status and keyword filters', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.set('listBusinessConsoleWmsOutboundOrders', {
      success: true,
      data: {
        total: 17,
        items: [{ outboundOrderId: 'out-1', outboundOrderNo: 'OUT-001' }],
      },
    })

    const result = useWmsOutboundOrders({ status: 'Completed', keyword: 'OUT' })

    expect(listBusinessConsoleWmsOutboundOrdersQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
        status: 'Completed',
        keyword: 'OUT',
      },
    })
    expect(result.outboundOrdersTotal.value).toBe(17)
  })

  it('lists WCS tasks with status, failed, and keyword filters', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.set('listBusinessConsoleWmsWcsTasks', {
      success: true,
      data: {
        total: 9,
        items: [{ wcsTaskId: 'wcs-1', externalTaskId: 'EXT-001' }],
      },
    })

    const result = useWmsWcsTasks({ status: 'Failed', failed: true, keyword: 'EXT' })

    expect(listBusinessConsoleWmsWcsTasksQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
        status: 'Failed',
        failed: true,
        keyword: 'EXT',
      },
    })
    expect(result.wcsTasksTotal.value).toBe(9)
  })

  it('disables inbound order queries until business context is selected', () => {
    useWmsInboundOrders()

    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: '', environmentId: '' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsInboundOrders')?.enabled).toBe(
      false,
    )
  })

  it('does not refetch WMS lists when business context is empty', async () => {
    const inbound = useWmsInboundOrders()
    const refetch = coladaState.queryRefetchById.get('listBusinessConsoleWmsInboundOrders')

    await inbound.refreshInboundOrders()

    expect(refetch).not.toHaveBeenCalled()

    useBusinessContextStore().patchContext({ organizationId: 'org-wms', environmentId: 'env-wms' })
    await inbound.refreshInboundOrders()

    expect(refetch).toHaveBeenCalledOnce()
  })

  it('updates WMS query scope when business context changes', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-a', environmentId: 'env-a' })
    useWmsOutboundOrders()

    context.patchContext({ organizationId: 'org-b', environmentId: 'env-b' })
    coladaState.queryFactoriesById.get('listBusinessConsoleWmsOutboundOrders')?.()

    expect(listBusinessConsoleWmsOutboundOrdersQueryOptions).toHaveBeenLastCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-b',
        environmentId: 'env-b',
      }),
    })
  })
})
