import {
  queryBusinessConsoleMesProductionStatistics,
  queryBusinessConsoleMesProductionStatisticsQueryOptions,
} from '@nerv-iip/api-client'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, shallowRef } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  loadAllMesProductionStatistics,
  useMesProductionStatistics,
} from './useMesProductionStatistics'

type QueryOptions = { query: Record<string, unknown>; signal?: AbortSignal }

const state = vi.hoisted(() => ({
  data: undefined as unknown,
  calls: [] as QueryOptions[],
  implementation: (_options: QueryOptions): Promise<unknown> => Promise.resolve({}),
  queryOptions: undefined as undefined | { enabled?: boolean },
}))

vi.mock('@nerv-iip/api-client', () => ({
  queryBusinessConsoleMesProductionStatistics: vi.fn((options: QueryOptions) => {
    state.calls.push(options)
    return state.implementation(options)
  }),
  queryBusinessConsoleMesProductionStatisticsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleMesProductionStatistics' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((factory) => {
    const options = factory()
    state.queryOptions = options
    return {
      data: shallowRef(state.data),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('MES production statistics composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    state.data = undefined
    state.calls = []
    state.queryOptions = undefined
    state.implementation = () => Promise.resolve({})
    vi.clearAllMocks()
  })

  it('maps the complete server-side filter and paging contract through the stable client', () => {
    state.data = {
      success: true,
      data: {
        items: [{ dimension: 'shift', dimensionValue: 'SHIFT-DAY', totalOutputQuantity: 120 }],
        totalCount: 21,
      },
    }

    const report = useMesProductionStatistics({
      dimension: 'shift',
      windowStartUtc: '2026-08-01T00:00:00.000Z',
      windowEndUtc: '2026-08-08T00:00:00.000Z',
      businessDate: '2026-08-06',
      shiftCode: ' SHIFT-DAY ',
      workCenterId: ' WC-CNC-01 ',
      skuId: ' SKU-BEARING-6205 ',
      skip: 20,
      take: 20,
    })

    expect(queryBusinessConsoleMesProductionStatisticsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        dimension: 'shift',
        windowStartUtc: '2026-08-01T00:00:00.000Z',
        windowEndUtc: '2026-08-08T00:00:00.000Z',
        businessDate: '2026-08-06',
        shiftCode: 'SHIFT-DAY',
        workCenterId: 'WC-CNC-01',
        skuId: 'SKU-BEARING-6205',
        skip: 20,
        take: 20,
      },
    })
    expect(report.items.value[0]).toMatchObject({
      dimensionValueLabel: 'SHIFT-DAY',
      totalOutputQuantity: 120,
    })
    expect(report.total.value).toBe(21)
    expect(state.queryOptions?.enabled).toBe(true)
  })

  it('follows business context that becomes available after page setup', async () => {
    useBusinessContextStore().resetContext()
    const report = useMesProductionStatistics({
      windowStartUtc: '2026-08-01T00:00:00.000Z',
      windowEndUtc: '2026-08-08T00:00:00.000Z',
    })

    useBusinessContextStore().patchContext({
      organizationId: 'org-late',
      environmentId: 'env-late',
    })
    await nextTick()

    expect(report.filters.organizationId).toBe('org-late')
    expect(report.filters.environmentId).toBe('env-late')
  })

  it('surfaces an unsupported producer identifier as the shared report error', () => {
    state.data = {
      success: true,
      data: {
        items: [
          {
            dimension: 'workCenter',
            dimensionValue: 'work-center-internal-42',
            workCenterId: 'work-center-internal-42',
            totalOutputQuantity: 120,
          },
        ],
        totalCount: 1,
      },
    }

    const report = useMesProductionStatistics({
      windowStartUtc: '2026-08-01T00:00:00.000Z',
      windowEndUtc: '2026-08-08T00:00:00.000Z',
    })

    expect(report.items.value).toEqual([])
    expect(report.error.value).toEqual(new Error('工作中心分组缺少受支持的业务编码。'))
    expect(report.state.value).toBe('error')
  })

  it('reads every server page for export without silently returning a partial file', async () => {
    state.implementation = async ({ query }) => {
      const skip = Number(query.skip)
      const count = skip === 0 ? 500 : 2
      return {
        data: {
          success: true,
          data: {
            items: Array.from({ length: count }, (_, index) => ({
              dimension: 'day',
              dimensionValue: `2026-day-${skip + index + 1}`,
              goodQuantity: skip + index + 1,
            })),
            totalCount: 502,
          },
        },
      }
    }

    const rows = await loadAllMesProductionStatistics({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      dimension: 'day',
      windowStartUtc: '2026-08-01T00:00:00.000Z',
      windowEndUtc: '2026-09-01T00:00:00.000Z',
    })

    expect(rows).toHaveLength(502)
    expect(state.calls.map(({ query }) => [query.skip, query.take])).toEqual([
      [0, 500],
      [500, 500],
    ])
    expect(queryBusinessConsoleMesProductionStatistics).toHaveBeenCalledTimes(2)
  })

  it('rejects an interrupted export instead of generating a partial CSV', async () => {
    state.implementation = async () => ({
      data: { success: true, data: { items: [], totalCount: 2 } },
    })

    await expect(
      loadAllMesProductionStatistics({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        dimension: 'workCenter',
        windowStartUtc: '2026-08-01T00:00:00.000Z',
        windowEndUtc: '2026-09-01T00:00:00.000Z',
      }),
    ).rejects.toThrow('生产统计导出在读取全部数据前意外结束')
  })
})
