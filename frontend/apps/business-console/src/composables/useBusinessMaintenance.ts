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
  recordBusinessConsoleMaintenanceInspectionMutationOptions,
  type BusinessConsoleCompleteMaintenanceWorkOrderRequest,
  type BusinessConsoleCreateMaintenancePlanRequest,
  type BusinessConsoleCreateMaintenanceSparePartRequest,
  type BusinessConsoleCreateMaintenanceWorkOrderRequest,
  type BusinessConsoleMaintenanceAssetReliabilityEnvelope,
  type BusinessConsoleMaintenanceAssetReliabilityResponse,
  type BusinessConsoleMaintenanceInspectionItem,
  type BusinessConsoleMaintenanceInspectionListEnvelope,
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
import { bindBusinessContext, hasBusinessContext, withBusinessContextEnabled } from './businessContextBinding'

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
      void workOrdersQuery.refetch()
    },
  })
  const completeMutation = useMutation({
    ...completeBusinessConsoleMaintenanceWorkOrderMutationOptions(),
    onSuccess() {
      void workOrdersQuery.refetch()
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
    refreshWorkOrders: workOrdersQuery.refetch,
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
      void inspectionsQuery.refetch()
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
    refreshInspections: inspectionsQuery.refetch,
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
      void sparePartsQuery.refetch()
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
    refreshSpareParts: sparePartsQuery.refetch,
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
      void plansQuery.refetch()
    },
  })
  const generateDueMutation = useMutation({
    ...generateDueBusinessConsoleMaintenanceWorkOrdersMutationOptions(),
    onSuccess() {
      void plansQuery.refetch()
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
    refreshPlans: plansQuery.refetch,
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
