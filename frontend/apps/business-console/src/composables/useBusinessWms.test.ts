import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  completeBusinessConsoleWmsCountExecution,
  completeBusinessConsoleWmsInboundOrder,
  completeBusinessConsoleWmsOutboundOrder,
  listBusinessConsoleWmsCountExecutions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsReceivingQualityGates,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrders,
  listBusinessConsoleWmsSupplierReturnRequests,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
} from '@nerv-iip/api-client'
import { acquirePendingBusinessIntent } from '@nerv-iip/business-core'
import {
  useWmsCountExecutions,
  useWmsInboundOrders,
  useWmsOutboundOrders,
  useWmsWcsTasks,
} from './useBusinessWms'
import { useBusinessContextStore } from '@/stores/businessContext'

const coladaState = vi.hoisted(() => ({
  confirmOperation: vi.fn(),
  queryDataById: new Map<string, unknown>(),
  queryFactoriesById: new Map<string, () => { enabled?: boolean } & Record<string, unknown>>(),
  queryOptionsById: new Map<string, { enabled?: boolean } & Record<string, unknown>>(),
  queryRefetchById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  confirmBusinessConsoleOperation: (...args: unknown[]) => coladaState.confirmOperation(...args),
  completeBusinessConsoleWmsCountExecution: vi.fn(),
  completeBusinessConsoleWmsInboundOrder: vi.fn(),
  completeBusinessConsoleWmsOutboundOrder: vi.fn(),
  listBusinessConsoleWmsCountExecutions: vi.fn(),
  listBusinessConsoleWmsInboundOrders: vi.fn(),
  listBusinessConsoleWmsCountExecutionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsCountExecutions' }],
    query: vi.fn(),
  })),
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
  listBusinessConsoleWmsReceivingQualityGates: vi.fn(),
  listBusinessConsoleWmsSupplierReturnRequests: vi.fn(),
  listBusinessConsoleWmsOutboundOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsOutboundOrders' }],
    query: vi.fn(),
  })),
  listBusinessConsoleWmsOutboundOrders: vi.fn(),
  listBusinessConsoleWmsWcsTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleWmsWcsTasks' }],
    query: vi.fn(),
  })),
  completeBusinessConsoleWmsInboundOrderMutationOptions: vi.fn(() => ({})),
  completeBusinessConsoleWmsOutboundOrderMutationOptions: vi.fn(() => ({})),
  completeBusinessConsoleWmsWcsTaskMutationOptions: vi.fn(() => ({})),
  createBusinessConsoleWmsCountExecutionMutationOptions: vi.fn(() => ({})),
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
    coladaState.confirmOperation.mockImplementation(async (value) => value)
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
    const inboundQuery = coladaState.queryOptionsById.get('listBusinessConsoleWmsInboundOrders') as
      | { autoRefetch?: () => number }
      | undefined
    expect(inboundQuery?.autoRefetch?.()).toBe(10_000)
  })

  it('passes the trusted work scope to list queries and gates pages that require it', () => {
    useBusinessContextStore().patchContext({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })

    useWmsInboundOrders({
      workScopeRequired: true,
      scopeKind: 'self',
      scopeId: 'emp049',
    })

    expect(listBusinessConsoleWmsInboundOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        scopeKind: 'self',
        scopeId: 'emp049',
      }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsInboundOrders')?.enabled).toBe(
      true,
    )

    useWmsOutboundOrders({ workScopeRequired: true })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleWmsOutboundOrders')?.enabled).toBe(
      false,
    )
  })

  it('exposes unsuccessful inbound and outbound envelopes as business-response failures', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.set('listBusinessConsoleWmsInboundOrders', { success: false })
    coladaState.queryDataById.set('listBusinessConsoleWmsOutboundOrders', { success: false })

    const inbound = useWmsInboundOrders()
    const outbound = useWmsOutboundOrders()

    expect(inbound.inboundOrdersHasSuccessfulResponse.value).toBe(false)
    expect(inbound.inboundOrdersHasFailedResponse.value).toBe(true)
    expect(outbound.outboundOrdersHasSuccessfulResponse.value).toBe(false)
    expect(outbound.outboundOrdersHasFailedResponse.value).toBe(true)
  })

  it('reads receiving quality and supplier returns through all server pages', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    const gateSkips: number[] = []
    const returnSkips: number[] = []
    vi.mocked(listBusinessConsoleWmsReceivingQualityGates).mockImplementation((({
      query,
    }: {
      query?: { skip?: number }
    }) => {
      gateSkips.push(query?.skip ?? 0)
      return Promise.resolve({
        data: {
          success: true,
          data: {
            total: 2,
            items: [{ inboundOrderNo: gateSkips.length === 1 ? 'IN-001' : 'IN-002' }],
          },
        },
        request: new Request('http://test.local'),
        response: new Response(),
      } as Awaited<ReturnType<typeof listBusinessConsoleWmsReceivingQualityGates>>)
    }) as never)
    vi.mocked(listBusinessConsoleWmsSupplierReturnRequests).mockImplementation((({
      query,
    }: {
      query?: { skip?: number }
    }) => {
      returnSkips.push(query?.skip ?? 0)
      return Promise.resolve({
        data: {
          success: true,
          data: {
            total: 2,
            items: [{ supplierReturnNo: returnSkips.length === 1 ? 'RTS-001' : 'RTS-002' }],
          },
        },
        request: new Request('http://test.local'),
        response: new Response(),
      } as Awaited<ReturnType<typeof listBusinessConsoleWmsSupplierReturnRequests>>)
    }) as never)

    useWmsInboundOrders()
    type QualityQueryEnvelope = { data?: { items?: unknown[] } }
    type QualityQueryOption = {
      query?: () => Promise<QualityQueryEnvelope>
      autoRefetch?: () => number
    }
    const gateQuery = coladaState.queryOptionsById.get(
      'listBusinessConsoleWmsReceivingQualityGates',
    ) as QualityQueryOption | undefined
    const returnQuery = coladaState.queryOptionsById.get(
      'listBusinessConsoleWmsSupplierReturnRequests',
    ) as QualityQueryOption | undefined

    const gateEnvelope = await gateQuery?.query?.()
    const returnEnvelope = await returnQuery?.query?.()

    expect(gateSkips).toEqual([0, 1])
    expect(returnSkips).toEqual([0, 1])
    expect(gateEnvelope?.data?.items).toHaveLength(2)
    expect(returnEnvelope?.data?.items).toHaveLength(2)
    expect(gateQuery?.autoRefetch?.()).toBe(10_000)
  })

  it('surfaces a later quality page failure instead of returning an empty gate list', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.mocked(listBusinessConsoleWmsReceivingQualityGates).mockImplementation((({
      query,
    }: {
      query?: { skip?: number }
    }) => {
      if ((query?.skip ?? 0) > 0) {
        return Promise.resolve({
          data: { success: false },
          request: new Request('http://test.local'),
          response: new Response(),
        } as Awaited<ReturnType<typeof listBusinessConsoleWmsReceivingQualityGates>>)
      }
      return Promise.resolve({
        data: { success: true, data: { total: 2, items: [{ inboundOrderNo: 'IN-001' }] } },
        request: new Request('http://test.local'),
        response: new Response(),
      } as Awaited<ReturnType<typeof listBusinessConsoleWmsReceivingQualityGates>>)
    }) as never)

    useWmsInboundOrders()
    const gateQuery = coladaState.queryOptionsById.get(
      'listBusinessConsoleWmsReceivingQualityGates',
    ) as { query?: () => Promise<unknown> } | undefined

    await expect(gateQuery?.query?.()).rejects.toThrow('收货质检门禁读取失败')
  })

  it('fails closed when a successful quality page is empty before the server total is reached', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.mocked(listBusinessConsoleWmsReceivingQualityGates).mockImplementation((({
      query,
    }: {
      query?: { skip?: number }
    }) => {
      if ((query?.skip ?? 0) > 0) {
        return Promise.resolve({
          data: { success: true, data: { total: 2, items: [] } },
          request: new Request('http://test.local'),
          response: new Response(),
        } as Awaited<ReturnType<typeof listBusinessConsoleWmsReceivingQualityGates>>)
      }
      return Promise.resolve({
        data: { success: true, data: { total: 2, items: [{ inboundOrderNo: 'IN-001' }] } },
        request: new Request('http://test.local'),
        response: new Response(),
      } as Awaited<ReturnType<typeof listBusinessConsoleWmsReceivingQualityGates>>)
    }) as never)

    useWmsInboundOrders()
    const gateQuery = coladaState.queryOptionsById.get(
      'listBusinessConsoleWmsReceivingQualityGates',
    ) as { query?: () => Promise<unknown> } | undefined

    await expect(gateQuery?.query?.()).rejects.toThrow('收货质检门禁读取不完整')
  })

  it('surfaces a later supplier return page failure instead of accepting a partial return list', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.mocked(listBusinessConsoleWmsSupplierReturnRequests).mockImplementation((({
      query,
    }: {
      query?: { skip?: number }
    }) => {
      if ((query?.skip ?? 0) > 0) {
        return Promise.resolve({
          data: { success: false },
          request: new Request('http://test.local'),
          response: new Response(),
        } as Awaited<ReturnType<typeof listBusinessConsoleWmsSupplierReturnRequests>>)
      }
      return Promise.resolve({
        data: { success: true, data: { total: 2, items: [{ supplierReturnNo: 'RTS-001' }] } },
        request: new Request('http://test.local'),
        response: new Response(),
      } as Awaited<ReturnType<typeof listBusinessConsoleWmsSupplierReturnRequests>>)
    }) as never)

    useWmsInboundOrders()
    const returnQuery = coladaState.queryOptionsById.get(
      'listBusinessConsoleWmsSupplierReturnRequests',
    ) as { query?: () => Promise<unknown> } | undefined

    await expect(returnQuery?.query?.()).rejects.toThrow('供应商退供读取失败')
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

  it('fails closed when WCS dispatch has no authoritative warehouse-task version', async () => {
    useBusinessContextStore().patchContext({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    const { dispatchWcs } = useWmsWcsTasks()

    await expect(
      dispatchWcs('warehouse-task-1', {
        adapterType: 'mock',
        externalTaskId: 'EXT-001',
        payloadJson: '{}',
      }),
    ).rejects.toMatchObject({ source: 'preflight' })
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

  it('allows only a persisted completed-count retry with the frozen key', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.mocked(listBusinessConsoleWmsCountExecutions)
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [{ countExecutionId: 'count-1', status: 'Open', version: 7 }],
            total: 1,
          },
        },
      } as never)
      .mockResolvedValue({
        data: {
          success: true,
          data: {
            items: [{ countExecutionId: 'count-1', status: 'Completed', version: 8 }],
            total: 1,
          },
        },
      } as never)
    const receipt = {
      success: true,
      data: { countExecutionId: 'count-1', status: 'Completed', countedQuantity: 5 },
    }
    vi.mocked(completeBusinessConsoleWmsCountExecution)
      .mockRejectedValueOnce(new TypeError('network interrupted'))
      .mockResolvedValue({
        data: receipt,
        response: new Response(null, { status: 200 }),
      } as never)
    const { completeCountExecution, filters } = useWmsCountExecutions({
      workScopeRequired: true,
      scopeKind: 'self',
      scopeId: 'emp049',
    })

    await expect(completeCountExecution('count-1', 5, 'KEY-CNT-FROZEN')).rejects.toThrow(
      'network interrupted',
    )
    filters.scopeKind = 'site'
    filters.scopeId = 'SITE-001'
    await expect(
      completeCountExecution('count-1', 5, 'KEY-CNT-FROZEN', { attempt: 'retry' }),
    ).resolves.toBe(receipt)

    expect(completeBusinessConsoleWmsCountExecution).toHaveBeenCalledTimes(2)
    expect(completeBusinessConsoleWmsCountExecution).toHaveBeenLastCalledWith({
      path: { countExecutionId: 'count-1' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      body: {
        countedQuantity: 5,
        expectedVersion: 7,
        scopeKind: 'self',
        scopeId: 'emp049',
        idempotencyKey: 'KEY-CNT-FROZEN',
      },
      throwOnError: false,
    })
    expect(listBusinessConsoleWmsCountExecutions).toHaveBeenLastCalledWith({
      query: expect.objectContaining({
        scopeKind: 'self',
        scopeId: 'emp049',
      }),
      throwOnError: false,
    })
  })

  it.each([
    {
      kind: 'inbound',
      useList: () =>
        useWmsInboundOrders({
          workScopeRequired: true,
          scopeKind: 'work-pool',
          scopeId: 'WMS-SITE-001-RECEIVING',
        }),
      list: listBusinessConsoleWmsInboundOrders,
      complete: completeBusinessConsoleWmsInboundOrder,
      idField: 'inboundOrderId',
      id: 'inbound-1',
      operation: (result: ReturnType<typeof useWmsInboundOrders>, id: string, key: string) =>
        result.completeInbound(id, key),
      expectedBusinessBody: {},
    },
    {
      kind: 'outbound',
      useList: () =>
        useWmsOutboundOrders({
          workScopeRequired: true,
          scopeKind: 'work-pool',
          scopeId: 'WMS-SITE-001-SHIPPING',
        }),
      list: listBusinessConsoleWmsOutboundOrders,
      complete: completeBusinessConsoleWmsOutboundOrder,
      idField: 'outboundOrderId',
      id: 'outbound-1',
      operation: (result: ReturnType<typeof useWmsOutboundOrders>, id: string, key: string) =>
        result.completeOutbound(id, { packReviewNo: 'PR-001', passed: true }, key),
      expectedBusinessBody: { packReviewNo: 'PR-001', passed: true },
    },
  ])('$kind completion freezes the catalog scope and authoritative version', async (testCase) => {
    useBusinessContextStore().patchContext({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    vi.mocked(testCase.list).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ [testCase.idField]: testCase.id, status: 'Open', version: 11 }],
          total: 1,
        },
      },
    } as never)
    vi.mocked(testCase.complete).mockResolvedValue({
      data: { success: true, data: { [testCase.idField]: testCase.id } },
      response: new Response(null, { status: 200 }),
    } as never)

    const result = testCase.useList()
    await testCase.operation(result as never, testCase.id, `KEY-${testCase.kind}`)

    expect(testCase.complete).toHaveBeenCalledWith({
      path: { [testCase.idField]: testCase.id },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      body: {
        ...testCase.expectedBusinessBody,
        expectedVersion: 11,
        scopeKind: 'work-pool',
        scopeId: testCase.kind === 'inbound' ? 'WMS-SITE-001-RECEIVING' : 'WMS-SITE-001-SHIPPING',
        idempotencyKey: `KEY-${testCase.kind}`,
      },
      throwOnError: false,
    })
  })

  it('fails closed before completion when the authoritative aggregate version is not positive', async () => {
    useBusinessContextStore().patchContext({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    vi.mocked(listBusinessConsoleWmsInboundOrders).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ inboundOrderId: 'inbound-zero', status: 'Open', version: 0 }],
          total: 1,
        },
      },
    } as never)
    const { completeInbound } = useWmsInboundOrders({
      scopeKind: 'self',
      scopeId: 'emp049',
    })

    await expect(completeInbound('inbound-zero', 'KEY-ZERO')).rejects.toMatchObject({
      source: 'preflight',
    })
    expect(completeBusinessConsoleWmsInboundOrder).not.toHaveBeenCalled()
  })

  it('does not combine a malformed restored scope with the currently selected scope', async () => {
    useBusinessContextStore().patchContext({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    acquirePendingBusinessIntent(
      {
        principalId: 'unrestored-session',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        operationType: 'wms.inbound-order.complete',
        payloadFingerprint: 'inbound-malformed',
      },
      () => 'KEY-MALFORMED',
      { scopeKind: 'self', expectedVersion: 2 },
    )
    const { completeInbound } = useWmsInboundOrders({
      scopeKind: 'site',
      scopeId: 'SITE-001',
    })

    await expect(completeInbound('inbound-malformed', 'KEY-NEW')).rejects.toMatchObject({
      source: 'preflight',
    })
    expect(listBusinessConsoleWmsInboundOrders).not.toHaveBeenCalled()
  })

  it('rotates the count intent key after an explicit 422 rejection', async () => {
    useBusinessContextStore().patchContext({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    vi.mocked(listBusinessConsoleWmsCountExecutions).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [{ countExecutionId: 'count-422', status: 'Open', version: 3 }],
          total: 1,
        },
      },
    } as never)
    vi.mocked(completeBusinessConsoleWmsCountExecution).mockResolvedValue({
      data: { success: true, data: { countExecutionId: 'count-422' } },
      response: new Response(null, { status: 200 }),
    } as never)
    coladaState.confirmOperation
      .mockRejectedValueOnce(Object.assign(new Error('validation failed'), { statusCode: 422 }))
      .mockImplementation(async (value) => value)
    const { completeCountExecution } = useWmsCountExecutions({
      scopeKind: 'self',
      scopeId: 'emp049',
    })

    await expect(completeCountExecution('count-422', 5, 'count-key-1')).rejects.toThrow(
      'validation failed',
    )
    await completeCountExecution('count-422', 5, 'count-key-2')

    expect(
      vi
        .mocked(completeBusinessConsoleWmsCountExecution)
        .mock.calls.map(([request]) => request.body.idempotencyKey),
    ).toEqual(['count-key-1', 'count-key-2'])
  })
})
