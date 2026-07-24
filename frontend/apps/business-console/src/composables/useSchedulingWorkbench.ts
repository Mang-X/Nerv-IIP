import {
  createBusinessConsoleSchedulingPlanRevisionMutationOptions,
  createBusinessConsoleSchedulingWorkbenchPlanMutationOptions,
  type BusinessConsoleCreateSchedulePlanRevisionRequest,
  type BusinessConsoleCreateSchedulingWorkbenchPlanRequest,
  type BusinessConsoleSchedulePlan,
  type BusinessConsoleSchedulingPlanRevision,
} from '@nerv-iip/api-client'
import { useMutation, useQueryCache } from '@pinia/colada'
import type { UseQueryEntry } from '@pinia/colada'
import { computed } from 'vue'
import { useMesWorkOrders } from './useBusinessMes'

const SCHEDULING_IDS = ['listBusinessConsoleSchedulingPlans', 'getBusinessConsoleSchedulingPlan']
const SCHEDULING_WORKBENCH_TERMINAL_WORK_ORDER_STATUSES = new Set([
  // UX prefilter only. The Scheduling service remains authoritative; keep aligned with
  // SchedulingWorkbenchSourceProvider.TerminalStatuses.
  'completed',
  'closed',
  'cancelled',
  'canceled',
  'scrapped',
])

export function useSchedulingWorkbench() {
  const mes = useMesWorkOrders({ initialTake: 500 })
  const queryCache = useQueryCache()

  const invalidatePlans = () =>
    Promise.all(
      SCHEDULING_IDS.map((id) =>
        queryCache.invalidateQueries({
          predicate: isSchedulingWorkbenchQuery([id]),
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
      mes.workOrders.value.filter(isSchedulableWorkbenchCandidate),
    ),
  }
}

export function isSchedulingWorkbenchQuery(ids: string[]) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
    return keyParts.some(
      (part) =>
        typeof part === 'object' &&
        part !== null &&
        '_id' in part &&
        ids.includes(String(part._id)),
    )
  }
}

export function isSchedulableWorkbenchCandidate(order: {
  workOrderId?: string | null
  productionVersionId?: string | null
  status?: string | null
}) {
  return (
    Boolean(order.workOrderId && order.productionVersionId) &&
    !SCHEDULING_WORKBENCH_TERMINAL_WORK_ORDER_STATUSES.has(order.status?.toLowerCase() ?? '')
  )
}

function unwrap<T>(envelope: unknown): T {
  const response = envelope as { success?: boolean; data?: T | null; message?: string }
  if (!response.success || !response.data) {
    throw new Error(response.message || 'Scheduling service returned no data.')
  }
  return response.data
}
