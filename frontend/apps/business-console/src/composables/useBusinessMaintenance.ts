import {
  completeBusinessConsoleMaintenanceWorkOrderMutationOptions,
  createBusinessConsoleMaintenancePlanMutationOptions,
  createBusinessConsoleMaintenanceSparePartMutationOptions,
  createBusinessConsoleMaintenanceWorkOrderMutationOptions,
  generateDueBusinessConsoleMaintenanceWorkOrdersMutationOptions,
  listBusinessConsoleMaintenanceInspectionsQueryOptions,
  listBusinessConsoleMaintenancePlansQueryOptions,
  listBusinessConsoleMaintenanceSparePartsQueryOptions,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions,
  queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions,
  queryBusinessConsoleMaintenanceInspectionMeasurementTrendQueryOptions,
  queryBusinessConsoleMaintenanceReliabilitySummaryQueryOptions,
  recordBusinessConsoleMaintenanceInspectionMutationOptions,
  type BusinessConsoleCompleteMaintenanceWorkOrderRequest,
  type BusinessConsoleCreateMaintenancePlanRequest,
  type BusinessConsoleCreateMaintenanceSparePartRequest,
  type BusinessConsoleCreateMaintenanceWorkOrderRequest,
  type BusinessConsoleMaintenanceAssetReliabilityEnvelope,
  type BusinessConsoleMaintenanceAssetReliabilityResponse,
  type BusinessConsoleMaintenanceInspectionItem,
  type BusinessConsoleMaintenanceInspectionListEnvelope,
  type BusinessConsoleMaintenanceInspectionMeasurementTrendItem,
  type BusinessConsoleMaintenanceInspectionMeasurementTrendResponse,
  type BusinessConsoleMaintenanceReliabilitySummaryItem,
  type BusinessConsoleMaintenanceReliabilitySummaryResponse,
  type BusinessConsoleMaintenancePlanItem,
  type BusinessConsoleMaintenancePlanListEnvelope,
  type BusinessConsoleMaintenanceSparePartItem,
  type BusinessConsoleMaintenanceSparePartListEnvelope,
  type BusinessConsoleMaintenanceWorkOrderItem,
  type BusinessConsoleMaintenanceWorkOrderListEnvelope,
  type BusinessConsoleRecordMaintenanceInspectionRequest,
  type EquipmentRuntimeAvailabilityEnvelope,
  type EquipmentRuntimeAvailabilityWindow,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from './businessContextBinding'

const DEFAULT_TAKE = 100

export interface MaintenanceListFilters {
  organizationId: string
  environmentId: string
  skip: number
  take: number
}

export interface MaintenanceReliabilityFilters {
  organizationId: string
  environmentId: string
  deviceAssetId: string
  windowStartUtc: string
  windowEndUtc: string
}

export interface MaintenanceAvailabilityFilters {
  organizationId: string
  environmentId: string
  deviceAssetIds: string
  windowStartUtc: string
  windowEndUtc: string
  workCenterIds: string
}

function defaultFilters(initial: Partial<MaintenanceListFilters> = {}): MaintenanceListFilters {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  }))
}

function defaultWindowRange() {
  const end = new Date()
  const start = new Date(end)
  start.setDate(start.getDate() - 30)

  return {
    windowStartUtc: start.toISOString(),
    windowEndUtc: end.toISOString(),
  }
}

function defaultReliabilityFilters(initial: Partial<MaintenanceReliabilityFilters> = {}): MaintenanceReliabilityFilters {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    deviceAssetId: '',
    ...defaultWindowRange(),
    ...initial,
  }))
}

function defaultAvailabilityFilters(initial: Partial<MaintenanceAvailabilityFilters> = {}): MaintenanceAvailabilityFilters {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    deviceAssetIds: '',
    workCenterIds: '',
    ...defaultWindowRange(),
    ...initial,
  }))
}

function optionalQuery<TKey extends string>(key: TKey, value: string) {
  const normalized = value.trim()
  return normalized.length > 0 ? { [key]: normalized } : {}
}

function listItems<TItem>(envelope: { success?: boolean, data?: { items?: TItem[] } | null } | undefined) {
  return envelope?.success ? envelope.data?.items ?? [] : []
}

function listTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  return envelope?.success ? envelope.data?.total ?? 0 : 0
}

function unwrapData<TData>(envelope: { success?: boolean, data?: TData | null } | undefined) {
  return envelope?.success ? envelope.data ?? undefined : undefined
}

export function useMaintenanceWorkOrders(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const workOrdersQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleMaintenanceWorkOrdersQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
  )

  const createMutation = useMutation({
    ...createBusinessConsoleMaintenanceWorkOrderMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, workOrdersQuery)
    },
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleMaintenanceWorkOrderMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, workOrdersQuery)
    },
  })

  return {
    filters,
    workOrders: computed<BusinessConsoleMaintenanceWorkOrderItem[]>(() =>
      listItems<BusinessConsoleMaintenanceWorkOrderItem>(workOrdersQuery.data.value as BusinessConsoleMaintenanceWorkOrderListEnvelope | undefined),
    ),
    workOrdersError: workOrdersQuery.error,
    workOrdersPending: workOrdersQuery.isLoading,
    workOrdersTotal: computed(() => listTotal(workOrdersQuery.data.value as BusinessConsoleMaintenanceWorkOrderListEnvelope | undefined)),
    refreshWorkOrders: () => refetchWithBusinessContext(filters, workOrdersQuery),
    createWorkOrder: (body: BusinessConsoleCreateMaintenanceWorkOrderRequest) =>
      createMutation.mutateAsync({ body }),
    createWorkOrderPending: createMutation.isLoading,
    createWorkOrderError: createMutation.error,
    completeWorkOrder: (workOrderId: string, body: BusinessConsoleCompleteMaintenanceWorkOrderRequest) =>
      completeMutation.mutateAsync({ path: { workOrderId }, body }),
    completeWorkOrderPending: completeMutation.isLoading,
    completeWorkOrderError: completeMutation.error,
  }
}

export function useMaintenanceInspections(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const inspectionsQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleMaintenanceInspectionsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
  )

  const recordMutation = useMutation({
    ...recordBusinessConsoleMaintenanceInspectionMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, inspectionsQuery)
    },
  })

  return {
    filters,
    inspections: computed<BusinessConsoleMaintenanceInspectionItem[]>(() =>
      listItems<BusinessConsoleMaintenanceInspectionItem>(inspectionsQuery.data.value as BusinessConsoleMaintenanceInspectionListEnvelope | undefined),
    ),
    inspectionsError: inspectionsQuery.error,
    inspectionsPending: inspectionsQuery.isLoading,
    inspectionsTotal: computed(() => listTotal(inspectionsQuery.data.value as BusinessConsoleMaintenanceInspectionListEnvelope | undefined)),
    refreshInspections: () => refetchWithBusinessContext(filters, inspectionsQuery),
    recordInspection: (body: BusinessConsoleRecordMaintenanceInspectionRequest) =>
      recordMutation.mutateAsync({ body }),
    recordInspectionPending: recordMutation.isLoading,
    recordInspectionError: recordMutation.error,
  }
}

export function useMaintenanceSpareParts(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const sparePartsQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleMaintenanceSparePartsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
  )

  const createMutation = useMutation({
    ...createBusinessConsoleMaintenanceSparePartMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, sparePartsQuery)
    },
  })

  return {
    filters,
    spareParts: computed<BusinessConsoleMaintenanceSparePartItem[]>(() =>
      listItems<BusinessConsoleMaintenanceSparePartItem>(sparePartsQuery.data.value as BusinessConsoleMaintenanceSparePartListEnvelope | undefined),
    ),
    sparePartsError: sparePartsQuery.error,
    sparePartsPending: sparePartsQuery.isLoading,
    sparePartsTotal: computed(() => listTotal(sparePartsQuery.data.value as BusinessConsoleMaintenanceSparePartListEnvelope | undefined)),
    refreshSpareParts: () => refetchWithBusinessContext(filters, sparePartsQuery),
    createSparePart: (body: BusinessConsoleCreateMaintenanceSparePartRequest) =>
      createMutation.mutateAsync({ body }),
    createSparePartPending: createMutation.isLoading,
    createSparePartError: createMutation.error,
  }
}

export function useMaintenanceReliability(initialFilters: Partial<MaintenanceReliabilityFilters> = {}) {
  const filters = defaultReliabilityFilters(initialFilters)
  const reliabilityEnabled = computed(() => hasBusinessContext(filters) && filters.deviceAssetId.trim().length > 0)
  const reliabilityQuery = useQuery(() => ({
    ...queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions({
      path: { deviceAssetId: filters.deviceAssetId.trim() },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
      },
    }),
    enabled: reliabilityEnabled.value,
  }))

  return {
    filters,
    reliability: computed<BusinessConsoleMaintenanceAssetReliabilityResponse | undefined>(() =>
      unwrapData<BusinessConsoleMaintenanceAssetReliabilityResponse>(
        reliabilityQuery.data.value as BusinessConsoleMaintenanceAssetReliabilityEnvelope | undefined,
      ),
    ),
    reliabilityError: reliabilityQuery.error,
    reliabilityPending: reliabilityQuery.isLoading,
    refreshReliability: () => reliabilityEnabled.value ? reliabilityQuery.refetch() : Promise.resolve(),
  }
}

export interface MaintenanceMeasurementTrendFilters {
  organizationId: string
  environmentId: string
  deviceAssetId: string
  characteristicCode: string
  windowStartUtc: string
  windowEndUtc: string
}

function defaultMeasurementTrendFilters(
  initial: Partial<MaintenanceMeasurementTrendFilters> = {},
): MaintenanceMeasurementTrendFilters {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    deviceAssetId: '',
    characteristicCode: '',
    ...defaultWindowRange(),
    ...initial,
  }))
}

/**
 * 同设备同特性的测量值时间序列（趋势小图数据源）。需 org/env + 设备 + 特性齐备才发请求，
 * 缺任一即静默为空态，不打空请求。
 */
export function useMaintenanceMeasurementTrend(initialFilters: Partial<MaintenanceMeasurementTrendFilters> = {}) {
  const filters = defaultMeasurementTrendFilters(initialFilters)
  const trendEnabled = computed(
    () =>
      hasBusinessContext(filters) &&
      filters.deviceAssetId.trim().length > 0 &&
      filters.characteristicCode.trim().length > 0,
  )
  const trendQuery = useQuery(() => ({
    ...queryBusinessConsoleMaintenanceInspectionMeasurementTrendQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        deviceAssetId: filters.deviceAssetId.trim(),
        characteristicCode: filters.characteristicCode.trim(),
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
      },
    }),
    enabled: trendEnabled.value,
  }))

  const trend = computed<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse | undefined>(() =>
    unwrapData<BusinessConsoleMaintenanceInspectionMeasurementTrendResponse>(
      trendQuery.data.value as
        | { success?: boolean, data?: BusinessConsoleMaintenanceInspectionMeasurementTrendResponse | null }
        | undefined,
    ),
  )

  return {
    filters,
    trend,
    trendItems: computed<BusinessConsoleMaintenanceInspectionMeasurementTrendItem[]>(
      () => trend.value?.items ?? [],
    ),
    trendError: trendQuery.error,
    trendPending: trendQuery.isLoading,
    trendEnabled,
    refreshTrend: () => (trendEnabled.value ? trendQuery.refetch() : Promise.resolve()),
  }
}

export interface MaintenanceReliabilitySummaryFilters {
  organizationId: string
  environmentId: string
  deviceAssetId: string
  technicianUserId: string
  windowStartUtc: string
  windowEndUtc: string
}

function defaultReliabilitySummaryFilters(
  initial: Partial<MaintenanceReliabilitySummaryFilters> = {},
): MaintenanceReliabilitySummaryFilters {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    deviceAssetId: '',
    technicianUserId: '',
    ...defaultWindowRange(),
    ...initial,
  }))
}

/**
 * 窗口内按（设备 · 技师）聚合的工时与费用汇总。设备/技师为可选过滤（空即不带该参数）；
 * 只需 org/env 即可查（可跨设备汇总），可靠性页按当前设备下钻。
 */
export function useMaintenanceReliabilitySummary(initialFilters: Partial<MaintenanceReliabilitySummaryFilters> = {}) {
  const filters = defaultReliabilitySummaryFilters(initialFilters)
  const summaryEnabled = computed(() => hasBusinessContext(filters))
  const summaryQuery = useQuery(() => ({
    ...queryBusinessConsoleMaintenanceReliabilitySummaryQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
        ...optionalQuery('deviceAssetId', filters.deviceAssetId),
        ...optionalQuery('technicianUserId', filters.technicianUserId),
      },
    }),
    enabled: summaryEnabled.value,
  }))

  const summary = computed<BusinessConsoleMaintenanceReliabilitySummaryResponse | undefined>(() =>
    unwrapData<BusinessConsoleMaintenanceReliabilitySummaryResponse>(
      summaryQuery.data.value as
        | { success?: boolean, data?: BusinessConsoleMaintenanceReliabilitySummaryResponse | null }
        | undefined,
    ),
  )

  return {
    filters,
    summaryItems: computed<BusinessConsoleMaintenanceReliabilitySummaryItem[]>(
      () => summary.value?.items ?? [],
    ),
    summaryError: summaryQuery.error,
    summaryPending: summaryQuery.isLoading,
    summaryEnabled,
    refreshSummary: () => (summaryEnabled.value ? summaryQuery.refetch() : Promise.resolve()),
  }
}

export function useMaintenanceAvailabilityWindows(initialFilters: Partial<MaintenanceAvailabilityFilters> = {}) {
  const filters = defaultAvailabilityFilters(initialFilters)
  const availabilityEnabled = computed(() => hasBusinessContext(filters) && filters.deviceAssetIds.trim().length > 0)
  const availabilityQuery = useQuery(() => ({
    ...queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        windowStartUtc: filters.windowStartUtc,
        windowEndUtc: filters.windowEndUtc,
        ...optionalQuery('deviceAssetIds', filters.deviceAssetIds),
        ...optionalQuery('workCenterIds', filters.workCenterIds),
      },
    }),
    enabled: availabilityEnabled.value,
  }))

  return {
    filters,
    availabilityError: availabilityQuery.error,
    availabilityPending: availabilityQuery.isLoading,
    availabilityWindows: computed<EquipmentRuntimeAvailabilityWindow[]>(() =>
      listItems<EquipmentRuntimeAvailabilityWindow>(
        availabilityQuery.data.value as EquipmentRuntimeAvailabilityEnvelope | undefined,
      ),
    ),
    refreshAvailability: () => availabilityEnabled.value ? availabilityQuery.refetch() : Promise.resolve(),
  }
}

export function useMaintenancePlans(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const plansQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleMaintenancePlansQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
      },
    }), filters),
  )

  const createMutation = useMutation({
    ...createBusinessConsoleMaintenancePlanMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, plansQuery)
    },
  })
  const generateDueMutation = useMutation({
    ...generateDueBusinessConsoleMaintenanceWorkOrdersMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, plansQuery)
    },
  })

  return {
    filters,
    plans: computed<BusinessConsoleMaintenancePlanItem[]>(() =>
      listItems<BusinessConsoleMaintenancePlanItem>(plansQuery.data.value as BusinessConsoleMaintenancePlanListEnvelope | undefined),
    ),
    plansError: plansQuery.error,
    plansPending: plansQuery.isLoading,
    plansTotal: computed(() => listTotal(plansQuery.data.value as BusinessConsoleMaintenancePlanListEnvelope | undefined)),
    refreshPlans: () => refetchWithBusinessContext(filters, plansQuery),
    createPlan: (body: BusinessConsoleCreateMaintenancePlanRequest) =>
      createMutation.mutateAsync({ body }),
    createPlanPending: createMutation.isLoading,
    createPlanError: createMutation.error,
    generateDue: (payload: { businessDate: string, requestedBy: string }) =>
      generateDueMutation.mutateAsync({
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          businessDate: payload.businessDate,
          requestedBy: payload.requestedBy,
        },
      }),
    generateDuePending: generateDueMutation.isLoading,
    generateDueError: generateDueMutation.error,
  }
}
