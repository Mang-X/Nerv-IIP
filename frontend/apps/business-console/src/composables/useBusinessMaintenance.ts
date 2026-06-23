import {
  completeBusinessConsoleMaintenanceWorkOrderMutationOptions,
  createBusinessConsoleMaintenancePlanMutationOptions,
  createBusinessConsoleMaintenanceWorkOrderMutationOptions,
  generateDueBusinessConsoleMaintenanceWorkOrdersMutationOptions,
  listBusinessConsoleMaintenancePlansQueryOptions,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  type BusinessConsoleCompleteMaintenanceWorkOrderRequest,
  type BusinessConsoleCreateMaintenancePlanRequest,
  type BusinessConsoleCreateMaintenanceWorkOrderRequest,
  type BusinessConsoleMaintenancePlanItem,
  type BusinessConsoleMaintenancePlanListEnvelope,
  type BusinessConsoleMaintenanceWorkOrderItem,
  type BusinessConsoleMaintenanceWorkOrderListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

export interface MaintenanceListFilters {
  organizationId: string
  environmentId: string
  skip: number
  take: number
}

function defaultFilters(initial: Partial<MaintenanceListFilters> = {}): MaintenanceListFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  })
}

function listItems<TItem>(envelope: { success?: boolean, data?: { items?: TItem[] } | null } | undefined) {
  return envelope?.success ? envelope.data?.items ?? [] : []
}

function listTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  return envelope?.success ? envelope.data?.total ?? 0 : 0
}

export function useMaintenanceWorkOrders(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const workOrdersQuery = useQuery(() =>
    listBusinessConsoleMaintenanceWorkOrdersQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
      },
    }),
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

export function useMaintenancePlans(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const plansQuery = useQuery(() =>
    listBusinessConsoleMaintenancePlansQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
      },
    }),
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
