import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  runBusinessConsoleMesScheduleMutationOptions,
} from '@nerv-iip/api-client'
import { useMesSchedules, useMesWorkOrders } from './useBusinessMes'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryDataById: new Map<string, unknown>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  createBusinessConsoleMesRushWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  listBusinessConsoleMesWorkOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesWorkOrders' }],
    query: vi.fn(),
  })),
  recordBusinessConsoleMesProductionReportMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  runBusinessConsoleMesScheduleMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
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
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
  })),
}))

describe('business MES composables', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryDataById.clear()
  })

  it('lists work orders with default context and safe items', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesWorkOrders', {
      success: true,
      data: {
        items: [
          {
            workOrderId: 'wo-1',
            status: 'released',
          },
        ],
      },
    })

    const { workOrders } = useMesWorkOrders()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        take: 100,
      },
    })
    expect(workOrders.value).toEqual([
      {
        workOrderId: 'wo-1',
        status: 'released',
      },
    ])
  })

  it('defaults work orders to an empty array for unsuccessful envelopes', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesWorkOrders', {
      success: false,
    })

    const { workOrders } = useMesWorkOrders()

    expect(workOrders.value).toEqual([])
  })

  it('submits rush work orders and production reports', async () => {
    const { createRushWorkOrder, recordProductionReport } = useMesWorkOrders()

    await createRushWorkOrder({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-rush',
      skuId: 'sku-1',
      quantity: 10,
      dueUtc: '2026-05-24T00:00:00Z',
      workCenterId: 'wc-1',
      durationMinutes: 60,
    })
    await recordProductionReport({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-rush',
      operationTaskId: 'op-1',
      goodQuantity: 8,
      scrapQuantity: 1,
      completesOperation: true,
      reportedAtUtc: '2026-05-24T01:00:00Z',
    })

    expect(createBusinessConsoleMesRushWorkOrderMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(createBusinessConsoleMesRushWorkOrderMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        workOrderId: 'wo-rush',
      }),
    })
    expect(recordBusinessConsoleMesProductionReportMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(recordBusinessConsoleMesProductionReportMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        operationTaskId: 'op-1',
      }),
    })
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(2)
  })

  it('runs schedule mutations through the generated option', async () => {
    const { runSchedule } = useMesSchedules()

    await runSchedule({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      trigger: 'manual',
    })

    expect(runBusinessConsoleMesScheduleMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(runBusinessConsoleMesScheduleMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        trigger: 'manual',
      },
    })
  })
})
