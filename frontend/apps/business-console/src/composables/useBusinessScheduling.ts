import {
  getBusinessConsoleSchedulingPlanQueryOptions,
  listBusinessConsoleSchedulingPlansQueryOptions,
  releaseBusinessConsoleSchedulingPlanMutationOptions,
  type BusinessConsoleSchedulePlanEnvelope,
  type BusinessConsoleSchedulingPlanSummaryListEnvelope,
  type BusinessConsoleSchedulingPlanSummaryResponse,
  type BusinessConsoleSchedulePlan,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'
import { bindBusinessContext, hasBusinessContext, type BusinessContextFields } from './businessContextBinding'

const SCHEDULING_QUERY_IDS = [
  'listBusinessConsoleSchedulingPlans',
  'getBusinessConsoleSchedulingPlan',
  'getBusinessConsoleSchedulingPlanGantt',
]
// TODO(#630): restore real pagination when the Scheduling summary facade returns total/horizon.
const SINGLE_PAGE_PLAN_LIST_SIZE = 100

export interface SchedulingPlanListFilters extends BusinessContextFields {
  pageIndex: number
  pageSize: number
}

export interface SchedulingPlanSelection extends BusinessContextFields {
  planId: string
}

function defaultFilters(): SchedulingPlanListFilters {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    pageIndex: 1,
    pageSize: SINGLE_PAGE_PLAN_LIST_SIZE,
  }))
}

function defaultSelection(): SchedulingPlanSelection {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    planId: '',
  }))
}

function unwrapPlans(envelope: BusinessConsoleSchedulingPlanSummaryListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data ?? []
}

function unwrapPlan(envelope: BusinessConsoleSchedulePlanEnvelope | undefined) {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function isBusinessQuery(ids: string[]) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && ids.includes(String(part._id))
    })
  }
}

function ignoreBackgroundError(_error: unknown) {}

export function useBusinessScheduling() {
  const filters = defaultFilters()
  const detailSelection = defaultSelection()
  const page = shallowRef(1)
  const pageSize = shallowRef(String(SINGLE_PAGE_PLAN_LIST_SIZE))
  const queryCache = useQueryCache()

  const plansQuery = useQuery(() => {
    filters.pageIndex = page.value
    filters.pageSize = Number(pageSize.value) || SINGLE_PAGE_PLAN_LIST_SIZE

    return {
      ...listBusinessConsoleSchedulingPlansQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          pageIndex: filters.pageIndex,
          pageSize: filters.pageSize,
        },
      }),
      enabled: hasBusinessContext(filters),
    }
  })

  const detailQuery = useQuery(() => ({
    ...getBusinessConsoleSchedulingPlanQueryOptions({
      path: { planId: detailSelection.planId },
      query: {
        organizationId: detailSelection.organizationId,
        environmentId: detailSelection.environmentId,
      },
    }),
    enabled: hasBusinessContext(detailSelection) && detailSelection.planId.trim().length > 0,
  }))

  const invalidateSchedulingQueries = () =>
    queryCache.invalidateQueries({ predicate: isBusinessQuery(SCHEDULING_QUERY_IDS) })

  const releaseMutation = useMutation({
    ...releaseBusinessConsoleSchedulingPlanMutationOptions(),
    onSuccess() {
      void invalidateSchedulingQueries().catch(ignoreBackgroundError)
    },
  })

  return {
    detailSelection,
    filters,
    page,
    pageSize,
    planDetail: computed<BusinessConsoleSchedulePlan | undefined>(() =>
      unwrapPlan(detailQuery.data.value as BusinessConsoleSchedulePlanEnvelope | undefined),
    ),
    planDetailError: detailQuery.error,
    planDetailPending: detailQuery.isLoading,
    plans: computed<BusinessConsoleSchedulingPlanSummaryResponse[]>(() =>
      unwrapPlans(plansQuery.data.value as BusinessConsoleSchedulingPlanSummaryListEnvelope | undefined),
    ),
    plansError: plansQuery.error,
    plansPending: plansQuery.isLoading,
    releasePlan: (planId: string) =>
      releaseMutation.mutateAsync({
        path: { planId },
        query: {
          organizationId: detailSelection.organizationId || filters.organizationId,
          environmentId: detailSelection.environmentId || filters.environmentId,
        },
      }),
    releasePlanError: releaseMutation.error,
    releasePlanPending: releaseMutation.isLoading,
    refreshPlanDetail: detailQuery.refetch,
    refreshPlans: plansQuery.refetch,
  }
}
