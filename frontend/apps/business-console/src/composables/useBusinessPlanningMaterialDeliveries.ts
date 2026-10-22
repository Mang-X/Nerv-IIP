import {
  getBusinessConsolePlanningMaterialDeliveriesQueryOptions,
  listBusinessConsoleSchedulingPlanHistoryQueryOptions,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive, shallowRef, watch } from 'vue'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'
import { assertEnvelopeSuccess } from './serviceEnvelope'

export function useBusinessPlanningMaterialDeliveries() {
  const selection = bindBusinessContext(
    reactive({ organizationId: '', environmentId: '', runId: '', planId: '' }),
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
      enabled: hasBusinessContext(selection),
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
      enabled: hasBusinessContext(selection) && !!selection.runId,
      query: async (context) =>
        assertEnvelopeSuccess(await options.query(context), '读取物料交付失败。'),
    }
  })
  return {
    selection,
    planPage,
    plans: computed(() => plansQuery.data.value?.data?.items ?? []),
    plansTotal: computed(() => plansQuery.data.value?.data?.total ?? 0),
    plansPending: plansQuery.isLoading,
    plansError: plansQuery.error,
    deliveries: computed(() => deliveriesQuery.data.value?.data),
    deliveriesPending: deliveriesQuery.isLoading,
    deliveriesError: deliveriesQuery.error,
    refresh: () =>
      Promise.all([
        hasBusinessContext(selection) ? plansQuery.refetch() : undefined,
        hasBusinessContext(selection) && selection.runId ? deliveriesQuery.refetch() : undefined,
      ]),
  }
}
