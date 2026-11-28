import {
  queryBusinessConsoleTelemetryOeeAggregates,
  queryBusinessConsoleTelemetryOeeAggregatesQueryOptions,
  queryBusinessConsoleTelemetryOeeQueryOptions,
  queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions,
  type BusinessConsoleTelemetryOeeAggregateBucket,
  type BusinessConsoleTelemetryOeeAggregateEnvelope,
  type BusinessConsoleTelemetryOeeAggregateResponse,
  type BusinessConsoleTelemetryOeeEnvelope,
  type BusinessConsoleTelemetryOeeResponse,
  type EquipmentRuntimeAvailabilityEnvelope,
  type EquipmentRuntimeAvailabilityWindow,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext } from '../shared/businessContextBinding'
import {
  defaultWindowFilters,
  listItems,
  toContextQuery,
  trimOptional,
  type TelemetryOeeAggregateFilters,
  type TelemetryWindowFilters,
  unwrapData,
} from './telemetryShared'

export function formatOeeRate(value: number | null | undefined) {
  if (value === null || value === undefined) return '无数据'
  return `${(value * 100).toFixed(1)}%`
}

export function formatOeeQuantity(value: number | null | undefined, uomCode?: string | null) {
  if (value === null || value === undefined) return '无数据'
  return `${value.toLocaleString(undefined, { maximumFractionDigits: 3 })}${uomCode ? ` ${uomCode}` : ''}`
}

export function describeTelemetryOeeLimitations() {
  return 'OEE = 可用率 × 性能率 × 质量率。性能率按报工总产出与工序标准速率计算，质量率按良品 ÷（良品 + 报废 + 返工）计算；任一项缺数据时标记为数据不完整，不按 100% 计。'
}

export function describeTelemetryOeeDegradation(reason: string) {
  const labels: Record<string, string> = {
    runtimeStateFactsMissing: '缺少设备运行状态记录',
    runtimeStateCoverageIncomplete: '设备运行状态没有覆盖该时段',
    productionUomAmbiguous: '报工单位不一致，无法合并',
    productionOutputMissing: '缺少可用于计算的报工产出',
    theoreticalRateMissingOrAmbiguous: '缺少或存在冲突的工序标准速率',
    productiveRuntimeMissing: '该时段内没有有效的生产运行时长',
    loadingRuntimeMissing: '该时段内没有有效的负荷运行时长',
    historicalDimensionLegacyUnresolved: '部分早期报工缺少归属信息，无法按所选视角统计',
    historicalHierarchyMissing: '报工记录缺少工厂、车间或产线归属',
    historicalTimezoneMissing: '报工记录缺少工厂时区',
    historicalTimezoneInvalid: '报工记录的工厂时区无效',
    historicalShiftDefinitionMissing: '报工记录缺少班次定义',
    historicalShiftDefinitionInvalid: '报工记录的班次定义无效',
    historicalReportOutsideShiftWindow: '报工时间不在任何班次的起止时间内',
    historicalLocalTimeInvalid: '报工的当地时间无效',
    historicalLocalTimeAmbiguous: '报工的当地时间有歧义',
    siteDimensionMissing: '报工记录缺少所属工厂',
    workshopDimensionMissing: '报工记录缺少所属车间',
    lineDimensionMissing: '报工记录缺少所属产线',
    siteDimensionAmbiguous: '报工记录对应多个工厂',
    workshopDimensionAmbiguous: '报工记录对应多个车间',
    lineDimensionAmbiguous: '报工记录对应多条产线',
    siteTimezoneOrDayBoundaryMissing: '工厂时区或换日时刻未维护',
    shiftDefinitionOrBoundaryMissing: '班次定义或班次起止时间未维护',
    'runtime-state-facts-missing': '缺少设备运行状态记录',
    'production-facts-missing': '缺少报工记录',
    'production-uom-ambiguous': '报工单位不一致，无法合并',
    'production-output-missing': '报工总产出不为正，无法计算质量率',
    'theoretical-rate-missing-or-ambiguous': '缺少或存在冲突的工序标准速率',
    'productive-runtime-missing': '该时段内没有有效的生产运行时长',
  }
  // 对照表以外的原因码不上屏：原因码是系统枚举，解析不出来显示「—」。
  return labels[reason] ?? '—'
}

export function useBusinessTelemetryOeeAggregates(
  initialFilters: Partial<TelemetryOeeAggregateFilters> = {},
  shouldLoad: () => boolean = () => true,
) {
  const businessContext = useBusinessContextStore()
  const defaultWindow = defaultWindowFilters()
  const filters = reactive<TelemetryOeeAggregateFilters>({
    dimension: 'day',
    windowStartUtc: defaultWindow.windowStartUtc,
    windowEndUtc: defaultWindow.windowEndUtc,
    deviceAssetId: '',
    workCenterId: '',
    shiftCode: '',
    lineCode: '',
    workshopCode: '',
    businessDate: '',
    skip: 0,
    take: 20,
    ...initialFilters,
  })
  const queryEnabled = computed(
    () =>
      hasBusinessContext(businessContext) &&
      shouldLoad() &&
      filters.windowStartUtc.trim().length > 0 &&
      filters.windowEndUtc.trim().length > 0,
  )
  const aggregatesQuery = useQuery(() => ({
    ...queryBusinessConsoleTelemetryOeeAggregatesQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        dimension: filters.dimension,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
        deviceAssetId: trimOptional(filters.deviceAssetId),
        workCenterId: trimOptional(filters.workCenterId),
        shiftCode: trimOptional(filters.shiftCode),
        lineCode: trimOptional(filters.lineCode),
        workshopCode: trimOptional(filters.workshopCode),
        businessDate: trimOptional(filters.businessDate),
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: queryEnabled.value,
  }))
  const response = computed<BusinessConsoleTelemetryOeeAggregateResponse | undefined>(() =>
    unwrapData<BusinessConsoleTelemetryOeeAggregateResponse>(
      aggregatesQuery.data.value as BusinessConsoleTelemetryOeeAggregateEnvelope | undefined,
    ),
  )

  return {
    aggregateBuckets: computed<BusinessConsoleTelemetryOeeAggregateBucket[]>(
      () => response.value?.buckets ?? [],
    ),
    aggregateError: aggregatesQuery.error,
    aggregatePending: aggregatesQuery.isLoading,
    aggregateResponse: response,
    aggregateTotal: computed(() => response.value?.totalCount ?? 0),
    filters,
    refreshAggregates: () => (queryEnabled.value ? aggregatesQuery.refetch() : Promise.resolve()),
  }
}

export function useBusinessTelemetryOeeTrend(filters: TelemetryOeeAggregateFilters) {
  const businessContext = useBusinessContextStore()
  const queryEnabled = computed(
    () =>
      hasBusinessContext(businessContext) &&
      filters.windowStartUtc.trim().length > 0 &&
      filters.windowEndUtc.trim().length > 0,
  )
  const trendQuery = useQuery(() => ({
    key: [
      'business-telemetry-oee-complete-buckets',
      filters.dimension,
      businessContext.organizationId,
      businessContext.environmentId,
      filters.windowStartUtc,
      filters.windowEndUtc,
      filters.deviceAssetId,
      filters.workCenterId,
      filters.shiftCode,
      filters.lineCode,
      filters.workshopCode,
      filters.businessDate,
    ],
    query: async (context): Promise<BusinessConsoleTelemetryOeeAggregateBucket[]> => {
      const scope = { ...filters }
      const contextQuery = toContextQuery(businessContext)
      const buckets: BusinessConsoleTelemetryOeeAggregateBucket[] = []
      const take = 100
      let totalCount = 0

      do {
        const { data } = await queryBusinessConsoleTelemetryOeeAggregates({
          query: {
            ...contextQuery,
            dimension: scope.dimension,
            windowStartUtc: scope.windowStartUtc,
            windowEndUtc: scope.windowEndUtc,
            deviceAssetId: trimOptional(scope.deviceAssetId),
            workCenterId: trimOptional(scope.workCenterId),
            shiftCode: trimOptional(scope.shiftCode),
            lineCode: trimOptional(scope.lineCode),
            workshopCode: trimOptional(scope.workshopCode),
            businessDate: trimOptional(scope.businessDate),
            skip: buckets.length,
            take,
          },
          signal: context.signal,
          throwOnError: true,
        })
        const page = unwrapData<BusinessConsoleTelemetryOeeAggregateResponse>(data)
        if (!page) throw new Error('OEE 趋势查询未返回有效数据。')
        const pageBuckets = page.buckets ?? []
        const nextTotalCount = page.totalCount ?? 0
        if (pageBuckets.length === 0 && buckets.length < nextTotalCount) {
          throw new Error('OEE 趋势数据没有读取完整，请刷新重试。')
        }
        buckets.push(...pageBuckets)
        totalCount = nextTotalCount
      } while (buckets.length < totalCount)

      return buckets
    },
    enabled: queryEnabled.value,
  }))

  return {
    trendBuckets: computed<BusinessConsoleTelemetryOeeAggregateBucket[]>(
      () => trendQuery.data.value ?? [],
    ),
    trendError: trendQuery.error,
    trendPending: trendQuery.isLoading,
    refreshTrend: () => (queryEnabled.value ? trendQuery.refetch() : Promise.resolve()),
  }
}

export function useBusinessTelemetryOee(initialFilters: Partial<TelemetryWindowFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultWindowFilters(initialFilters)
  const deviceAssetId = computed(() => filters.deviceAssetId.trim())
  const oeeEnabled = computed(
    () => hasBusinessContext(businessContext) && deviceAssetId.value.length > 0,
  )
  const oeeQuery = useQuery(() => ({
    ...queryBusinessConsoleTelemetryOeeQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetId: deviceAssetId.value,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
      },
    }),
    enabled: oeeEnabled.value,
  }))
  const runtimeAvailabilityQuery = useQuery(() => ({
    ...queryBusinessConsoleTelemetryRuntimeAvailabilityQueryOptions({
      query: {
        ...toContextQuery(businessContext),
        deviceAssetIds: deviceAssetId.value || undefined,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
      },
    }),
    enabled: oeeEnabled.value,
  }))

  return {
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(() =>
      listItems<EquipmentRuntimeAvailabilityWindow>(
        runtimeAvailabilityQuery.data.value as EquipmentRuntimeAvailabilityEnvelope | undefined,
      ),
    ),
    filters,
    oee: computed<BusinessConsoleTelemetryOeeResponse | undefined>(() =>
      unwrapData<BusinessConsoleTelemetryOeeResponse>(
        oeeQuery.data.value as BusinessConsoleTelemetryOeeEnvelope | undefined,
      ),
    ),
    oeeError: oeeQuery.error,
    oeePending: computed(
      () => oeeQuery.isLoading.value || runtimeAvailabilityQuery.isLoading.value,
    ),
    refreshOee: () =>
      oeeEnabled.value
        ? Promise.all([oeeQuery.refetch(), runtimeAvailabilityQuery.refetch()])
        : Promise.resolve(),
    runtimeAvailabilityError: runtimeAvailabilityQuery.error,
  }
}
