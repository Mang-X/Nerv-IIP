import type {
  BusinessConsoleMesProductionStatisticsBucket,
  BusinessConsoleMesProductionStatisticsDimension,
  BusinessConsoleMesProductionStatisticsRequest,
  BusinessConsoleMesProductionStatisticsResponse,
} from '@nerv-iip/api-client'
import {
  queryBusinessConsoleMesProductionStatistics,
  queryBusinessConsoleMesProductionStatisticsQueryOptions,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive, watch } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { businessReadState } from './businessReadState'

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
    filters.organizationId.trim().length > 0 &&
    filters.environmentId.trim().length > 0 &&
    filters.windowStartUtc.trim().length > 0 &&
    filters.windowEndUtc.trim().length > 0
  )
}

export async function loadAllMesProductionStatistics(
  request: Omit<BusinessConsoleMesProductionStatisticsRequest, 'skip' | 'take'>,
  signal?: AbortSignal,
): Promise<BusinessConsoleMesProductionStatisticsBucket[]> {
  const rows: BusinessConsoleMesProductionStatisticsBucket[] = []
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
    rows.push(...response.items)
    totalCount = response.totalCount
  } while (rows.length < totalCount)

  return rows
}

export function useMesProductionStatistics(
  initialFilters: Partial<MesProductionStatisticsFilters> = {},
) {
  const context = useBusinessContextStore()
  const filters = reactive<MesProductionStatisticsFilters>({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
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
  })
  watch(
    [() => context.organizationId, () => context.environmentId],
    ([organizationId, environmentId]) => {
      filters.organizationId = organizationId
      filters.environmentId = environmentId
    },
    { immediate: true },
  )
  const enabled = computed(() => hasQueryContext(filters))
  const statisticsQuery = useQuery(() => ({
    ...queryBusinessConsoleMesProductionStatisticsQueryOptions({ query: queryOf(filters) }),
    enabled: enabled.value,
  }))
  const response = computed(() => {
    const envelope = statisticsQuery.data.value as ProductionStatisticsEnvelope | undefined
    return envelope?.success ? (envelope.data ?? undefined) : undefined
  })

  return {
    filters,
    items: computed(() => response.value?.items ?? []),
    total: computed(() => response.value?.totalCount ?? 0),
    error: statisticsQuery.error,
    pending: statisticsQuery.isLoading,
    state: businessReadState(statisticsQuery, () => enabled.value),
    refresh: () => (enabled.value ? statisticsQuery.refetch() : Promise.resolve()),
    loadAll: (signal?: AbortSignal) => {
      const { skip: _skip, take: _take, ...request } = queryOf(filters)
      return loadAllMesProductionStatistics(request, signal)
    },
  }
}
