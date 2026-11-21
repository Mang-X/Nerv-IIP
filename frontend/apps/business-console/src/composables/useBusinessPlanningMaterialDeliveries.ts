import {
  getBusinessConsolePlanningMaterialDeliveriesQueryOptions,
  listBusinessConsoleSchedulingPlanHistoryQueryOptions,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive, shallowRef, watch } from 'vue'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'
import { assertEnvelopeSuccess } from './serviceEnvelope'

import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'

export function useCanReadMaterialDeliveries() {
  const auth = useAuthStore()
  return computed(() =>
    [P.planningMrpRead, P.planningDemandsRead, P.erpProcurementRead].every((permission) =>
      auth.principal?.permissionCodes?.includes(permission),
    ),
  )
}

export function useBusinessPlanningMaterialDeliveries() {
  const selection = bindBusinessContext(
    reactive({ organizationId: '', environmentId: '', runId: '', planId: '' }),
  )
  const auth = useAuthStore()
  const canReadDeliveries = useCanReadMaterialDeliveries()
  const canSelectPlan = computed(
    () =>
      canReadDeliveries.value &&
      [
        P.schedulingPlansRead,
        P.mesWorkOrdersRead,
        P.mesReportingRead,
        P.engineeringProductionVersionsRead,
        P.engineeringRoutingsRead,
        P.masterDataResourcesRead,
      ].every((permission) => auth.principal?.permissionCodes?.includes(permission)),
  )
  watch(
    canSelectPlan,
    (allowed) => {
      if (!allowed) selection.planId = ''
    },
    { flush: 'sync' },
  )
  const planPage = shallowRef(1)
  watch(
    () => [selection.organizationId, selection.environmentId],
    () => {
      selection.planId = ''
      planPage.value = 1
    },
  )
  const plansQuery = useQuery(() => {
    const options = listBusinessConsoleSchedulingPlanHistoryQueryOptions({
      query: {
        organizationId: selection.organizationId,
        environmentId: selection.environmentId,
        pageIndex: planPage.value - 1,
        pageSize: 20,
      },
    })
    return {
      ...options,
      enabled: hasBusinessContext(selection) && canSelectPlan.value,
      query: async (context) =>
        assertEnvelopeSuccess(await options.query(context), '读取 APS 方案失败。'),
    }
  })
  const deliveriesQuery = useQuery(() => {
    const options = getBusinessConsolePlanningMaterialDeliveriesQueryOptions({
      path: { runId: selection.runId },
      query: {
        organizationId: selection.organizationId,
        environmentId: selection.environmentId,
        planId: selection.planId || undefined,
      },
    })
    return {
      ...options,
      enabled: hasBusinessContext(selection) && canReadDeliveries.value && !!selection.runId,
      query: async (context) =>
        assertEnvelopeSuccess(await options.query(context), '读取物料交付失败。'),
    }
  })
  return {
    selection,
    planPage,
    canSelectPlan,
    plans: computed(() => plansQuery.data.value?.data?.items ?? []),
    plansTotal: computed(() => plansQuery.data.value?.data?.total ?? 0),
    plansPending: plansQuery.isLoading,
    plansError: plansQuery.error,
    deliveries: computed(() => deliveriesQuery.data.value?.data),
    deliveriesPending: deliveriesQuery.isLoading,
    deliveriesError: deliveriesQuery.error,
    refresh: () =>
      Promise.all([
        hasBusinessContext(selection) && canSelectPlan.value ? plansQuery.refetch() : undefined,
        hasBusinessContext(selection) && canReadDeliveries.value && selection.runId
          ? deliveriesQuery.refetch()
          : undefined,
      ]),
  }
}
