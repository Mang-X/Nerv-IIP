import {
  createBusinessConsoleMaintenancePlanMutationOptions,
  generateDueBusinessConsoleMaintenanceWorkOrdersMutationOptions,
  listBusinessConsoleMaintenancePlansQueryOptions,
  updateBusinessConsoleMaintenancePlanMutationOptions,
  type BusinessConsoleCreateMaintenancePlanRequest,
  type BusinessConsoleMaintenancePlanItem,
  type BusinessConsoleMaintenancePlanListEnvelope,
  type BusinessConsoleUpdateMaintenancePlanRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed } from 'vue'
import {
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import {
  defaultFilters,
  listItems,
  listTotal,
  type MaintenanceListFilters,
} from './maintenanceShared'

export function useMaintenancePlans(initialFilters: Partial<MaintenanceListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const plansQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMaintenancePlansQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          skip: filters.skip,
          take: filters.take,
          ...(filters.deviceAssetId && filters.deviceAssetId.trim().length > 0
            ? { deviceAssetId: filters.deviceAssetId.trim() }
            : {}),
        },
      }),
      filters,
    ),
  )

  const createMutation = useMutation({
    ...createBusinessConsoleMaintenancePlanMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, plansQuery)
    },
  })
  const updateMutation = useMutation(updateBusinessConsoleMaintenancePlanMutationOptions())
  const generateDueMutation = useMutation({
    ...generateDueBusinessConsoleMaintenanceWorkOrdersMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(filters, plansQuery)
    },
  })

  return {
    filters,
    plans: computed<BusinessConsoleMaintenancePlanItem[]>(() =>
      listItems<BusinessConsoleMaintenancePlanItem>(
        plansQuery.data.value as BusinessConsoleMaintenancePlanListEnvelope | undefined,
      ),
    ),
    plansError: plansQuery.error,
    plansPending: plansQuery.isLoading,
    plansTotal: computed(() =>
      listTotal(plansQuery.data.value as BusinessConsoleMaintenancePlanListEnvelope | undefined),
    ),
    refreshPlans: () => refetchWithBusinessContext(filters, plansQuery),
    createPlan: (body: BusinessConsoleCreateMaintenancePlanRequest) =>
      createMutation.mutateAsync({ body }),
    createPlanPending: createMutation.isLoading,
    createPlanError: createMutation.error,
    async updatePlan(planId: string, body: BusinessConsoleUpdateMaintenancePlanRequest) {
      const result = await updateMutation.mutateAsync({ path: { planId }, body })
      await refetchWithBusinessContext(filters, plansQuery)
      return result
    },
    updatePlanPending: updateMutation.isLoading,
    updatePlanError: updateMutation.error,
    generateDue: (payload: { businessDate: string; requestedBy: string }) =>
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
