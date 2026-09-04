import type {
  BusinessConsoleMesProductionStatisticsDimension,
  BusinessConsoleMesProductionStatisticsRequest,
  BusinessConsoleMesProductionStatisticsResponse,
} from '@nerv-iip/api-client'
import {
  queryBusinessConsoleMesProductionStatistics,
  queryBusinessConsoleMesProductionStatisticsQueryOptions,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '@/composables/businessContextBinding'
import { businessReadState } from './businessReadState'
import {
  presentProductionStatisticsRow,
  type ProductionStatisticsPresentationRow,
} from '@/features/mes-production-report/productionStatisticsPresentation'

export interface MesProductionStatisticsFilters {
  organizationId: string
  environmentId: string
  dimension: BusinessConsoleMesProductionStatisticsDimension
  windowStartUtc: string
  windowEndUtc: string
  businessDate: string
  shiftCode: string
  workCenterId: string
  skuId: string
  skip: number
  take: number
}

type ProductionStatisticsEnvelope = {
  success?: boolean
  data?: BusinessConsoleMesProductionStatisticsResponse | null
}

function trimOptional(value: string) {
  const normalized = value.trim()
  return normalized || undefined
}

function queryOf(
  filters: MesProductionStatisticsFilters,
): BusinessConsoleMesProductionStatisticsRequest {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    dimension: filters.dimension,
    windowStartUtc: filters.windowStartUtc,
    windowEndUtc: filters.windowEndUtc,
    businessDate: trimOptional(filters.businessDate),
    shiftCode: trimOptional(filters.shiftCode),
    workCenterId: trimOptional(filters.workCenterId),
    skuId: trimOptional(filters.skuId),
    skip: filters.skip,
    take: filters.take,
  }
}

function hasQueryContext(filters: MesProductionStatisticsFilters) {
  return (
    hasBusinessContext(filters) &&
    filters.windowStartUtc.trim().length > 0 &&
    filters.windowEndUtc.trim().length > 0
  )
}

export async function loadAllMesProductionStatistics(
  request: Omit<BusinessConsoleMesProductionStatisticsRequest, 'skip' | 'take'>,
  signal?: AbortSignal,
): Promise<ProductionStatisticsPresentationRow[]> {
  const rows: ProductionStatisticsPresentationRow[] = []
  const take = 500
  let totalCount = 0

  do {
    const { data } = await queryBusinessConsoleMesProductionStatistics({
      query: { ...request, skip: rows.length, take },
      signal,
      throwOnError: true,
    })
    const response = data?.success ? data.data : undefined
    if (!response) throw new Error('生产统计导出未返回有效数据。')
    if (response.items.length === 0 && rows.length < response.totalCount) {
      throw new Error('生产统计导出在读取全部数据前意外结束。')
    }
    rows.push(...response.items.map(presentProductionStatisticsRow))
    totalCount = response.totalCount
  } while (rows.length < totalCount)

  return rows
}

export function useMesProductionStatistics(
  initialFilters: Partial<MesProductionStatisticsFilters> = {},
) {
  const filters = bindBusinessContext(
    reactive<MesProductionStatisticsFilters>({
      organizationId: '',
      environmentId: '',
      dimension: 'day',
      windowStartUtc: '',
      windowEndUtc: '',
      businessDate: '',
      shiftCode: '',
      workCenterId: '',
      skuId: '',
      skip: 0,
      take: 20,
      ...initialFilters,
    }),
  )
  const enabled = computed(() => hasQueryContext(filters))
  const statisticsQuery = useQuery(() => {
    const options = withBusinessContextEnabled(
      queryBusinessConsoleMesProductionStatisticsQueryOptions({ query: queryOf(filters) }),
      filters,
    )
    return { ...options, enabled: options.enabled && enabled.value }
  })
  const envelope = computed(
    () => statisticsQuery.data.value as ProductionStatisticsEnvelope | undefined,
  )
  const response = computed(() =>
    envelope.value?.success ? (envelope.value.data ?? undefined) : undefined,
  )
  const presentation = computed(() => {
    try {
      return {
        items: response.value?.items.map(presentProductionStatisticsRow) ?? [],
        error: undefined,
      }
    } catch (error) {
      return { items: [], error }
    }
  })
  const error = computed(
    () =>
      statisticsQuery.error.value ??
      (envelope.value?.success === false
        ? new Error('生产统计读取失败，请稍后重试。')
        : presentation.value.error),
  )
  const state = businessReadState(
    { data: statisticsQuery.data, error, isLoading: statisticsQuery.isLoading },
    () => enabled.value,
  )

  return {
    filters,
    items: computed(() => presentation.value.items),
    total: computed(() => response.value?.totalCount ?? 0),
    error,
    pending: statisticsQuery.isLoading,
    state,
    refresh: () =>
      enabled.value
        ? refetchWithBusinessContext(filters, statisticsQuery)
        : Promise.resolve(undefined),
    loadAll: (signal?: AbortSignal) => {
      const { skip: _skip, take: _take, ...request } = queryOf(filters)
      return loadAllMesProductionStatistics(request, signal)
    },
  }
}
