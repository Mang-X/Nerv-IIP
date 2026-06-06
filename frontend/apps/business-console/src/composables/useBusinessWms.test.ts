import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
} from '@nerv-iip/api-client'
import { useWmsInboundOrders, useWmsOutboundOrders, useWmsWcsTasks } from './useBusinessWms'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleWmsInboundOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsInboundOrders' }],
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

describe('business WMS composables', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
  })

  it('lists inbound orders with paging, filters, items, and total', () => {
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
    expect(result.inboundOrders.value).toEqual([{ inboundOrderId: 'in-1', inboundOrderNo: 'IN-001' }])
    expect(result.inboundOrdersTotal.value).toBe(23)
  })

  it('lists outbound orders with status and keyword filters', () => {
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
})
