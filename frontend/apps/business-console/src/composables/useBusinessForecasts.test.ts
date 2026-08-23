import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { shallowRef } from 'vue'
import {
  createOrUpdateBusinessConsolePlanningForecastMutationOptions,
  listBusinessConsolePlanningForecastsQueryOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessForecasts } from './useBusinessForecasts'

const state = vi.hoisted(() => ({
  data: undefined as unknown,
  refetch: vi.fn(async () => undefined),
  mutation: vi.fn(async (vars) => ({ success: true, data: vars.body })),
  queryFactory: undefined as undefined | (() => unknown),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsolePlanningForecastsQueryOptions: vi.fn((options) => ({
    key: [{ _id: 'listBusinessConsolePlanningForecasts' }],
    query: vi.fn(),
    requestedOptions: options,
  })),
  createOrUpdateBusinessConsolePlanningForecastMutationOptions: vi.fn(() => ({
    mutation: state.mutation,
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((factory) => {
    state.queryFactory = factory
    factory()
    return {
      data: shallowRef(state.data),
      error: shallowRef(null),
      isLoading: shallowRef(false),
      refetch: state.refetch,
    }
  }),
  useMutation: vi.fn((options) => ({
    error: shallowRef(null),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn((vars) => options.mutation(vars)),
  })),
}))

describe('business forecast composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    state.data = {
      success: true,
      data: {
        items: [
          {
            forecastInputId: 'forecast-1',
            forecastReference: 'FC-2026-09-FG-1000',
            skuCode: 'FG-1000',
            siteCode: 'SITE-01',
            quantity: 120,
          },
        ],
      },
    }
    state.refetch.mockClear()
    state.mutation.mockClear()
    vi.clearAllMocks()
  })

  it('按当前业务上下文查询预测，并将全部筛选哨兵转换为空参数', () => {
    const { filters, forecasts } = useBusinessForecasts()

    expect(listBusinessConsolePlanningForecastsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: undefined,
        siteCode: undefined,
        fromDate: undefined,
        toDate: undefined,
      },
    })
    expect(forecasts.value[0]?.forecastReference).toBe('FC-2026-09-FG-1000')

    filters.skuCode = 'all'
    filters.siteCode = 'all'
    state.queryFactory?.()
    expect(listBusinessConsolePlanningForecastsQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: undefined,
        siteCode: undefined,
        fromDate: undefined,
        toDate: undefined,
      },
    })
  })

  it('保存完整 ForecastInput 契约并在成功后刷新列表', async () => {
    const { saveForecast } = useBusinessForecasts()
    const body = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      forecastReference: 'FC-2026-09-FG-1000',
      skuCode: 'FG-1000',
      uomCode: 'pcs',
      siteCode: 'SITE-01',
      periodStartDate: '2026-09-01',
      periodEndDate: '2026-09-30',
      quantity: 120,
      backwardConsumptionDays: 7,
      forwardConsumptionDays: 3,
    }

    await saveForecast(body)

    expect(createOrUpdateBusinessConsolePlanningForecastMutationOptions).toHaveBeenCalled()
    expect(state.mutation).toHaveBeenCalledWith({ body })
    expect(state.refetch).toHaveBeenCalledOnce()
  })

  it('200 软失败不会刷新列表或伪装成功', async () => {
    state.mutation.mockResolvedValueOnce({
      success: false,
      message: '预测期间已被锁定。',
    } as never)
    const { saveForecast } = useBusinessForecasts()

    await expect(
      saveForecast({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        forecastReference: 'FC-LOCKED',
        skuCode: 'FG-1000',
        uomCode: 'pcs',
        siteCode: 'SITE-01',
        periodStartDate: '2026-09-01',
        periodEndDate: '2026-09-30',
        quantity: 120,
        backwardConsumptionDays: 0,
        forwardConsumptionDays: 0,
      }),
    ).rejects.toThrow('预测期间已被锁定。')
    expect(state.refetch).not.toHaveBeenCalled()
  })
})
