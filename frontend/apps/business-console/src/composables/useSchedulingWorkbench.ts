import {
  createBusinessConsoleSchedulingPlanRevisionMutationOptions,
  createBusinessConsoleSchedulingWorkbenchPlanMutationOptions,
  type BusinessConsoleCreateSchedulePlanRevisionRequest,
  type BusinessConsoleCreateSchedulingWorkbenchPlanRequest,
  type BusinessConsoleSchedulePlan,
  type BusinessConsoleSchedulingPlanRevision,
} from '@nerv-iip/api-client'
import { useMutation, useQueryCache } from '@pinia/colada'
import { computed } from 'vue'
import { useMesWorkOrders } from './useBusinessMes'

const SCHEDULING_IDS = ['listBusinessConsoleSchedulingPlans', 'getBusinessConsoleSchedulingPlan']

export function useSchedulingWorkbench() {
  const mes = useMesWorkOrders()
  const queryCache = useQueryCache()
  mes.filters.take = 500

  const invalidatePlans = () =>
    Promise.all(
      SCHEDULING_IDS.map((id) =>
        queryCache.invalidateQueries({
          predicate: (entry) => JSON.stringify(entry.key).includes(id),
        }),
      ),
    )
  const generateMutation = useMutation({
    ...createBusinessConsoleSchedulingWorkbenchPlanMutationOptions(),
    onSuccess() {
      void invalidatePlans()
    },
  })
  const revisionMutation = useMutation({
    ...createBusinessConsoleSchedulingPlanRevisionMutationOptions(),
    onSuccess() {
      void invalidatePlans()
    },
  })

  return {
    candidates: mes.workOrders,
    candidatesError: mes.workOrdersError,
    candidatesPending: mes.workOrdersPending,
    filters: mes.filters,
    generatePending: generateMutation.isLoading,
    generatePlan: async (body: BusinessConsoleCreateSchedulingWorkbenchPlanRequest) =>
      unwrap<BusinessConsoleSchedulePlan>(await generateMutation.mutateAsync({ body })),
    refreshCandidates: mes.refreshWorkOrders,
    revisionPending: revisionMutation.isLoading,
    revisePlan: async (planId: string, body: BusinessConsoleCreateSchedulePlanRevisionRequest) =>
      unwrap<BusinessConsoleSchedulingPlanRevision>(
        await revisionMutation.mutateAsync({ path: { planId }, body }),
      ),
    schedulableCandidates: computed(() =>
      mes.workOrders.value.filter(
        (order) =>
          Boolean(order.workOrderId && order.productionVersionId) &&
          !['completed', 'closed', 'cancelled', 'canceled', 'scrapped'].includes(
            order.status?.toLowerCase() ?? '',
          ),
      ),
    ),
  }
}

function unwrap<T>(envelope: unknown): T {
  const response = envelope as { success?: boolean; data?: T | null; message?: string }
  if (!response.success || !response.data) {
    throw new Error(response.message || 'Scheduling service returned no data.')
  }
  return response.data
}
