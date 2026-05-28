import {
  acceptBusinessConsolePlanningSuggestionMutationOptions,
  createOrUpdateBusinessConsolePlanningDemandMutationOptions,
  getBusinessConsolePlanningMrpPeggingQueryOptions,
  listBusinessConsolePlanningDemandsQueryOptions,
  listBusinessConsolePlanningMrpRunsQueryOptions,
  listBusinessConsolePlanningSuggestionsQueryOptions,
  runBusinessConsolePlanningMrpMutationOptions,
  type BusinessConsoleAcceptPlanningSuggestionRequest,
  type BusinessConsoleDemandSourceItem,
  type BusinessConsoleDemandSourceListEnvelope,
  type BusinessConsoleMrpPeggingItem,
  type BusinessConsoleMrpPeggingListEnvelope,
  type BusinessConsoleMrpRunItem,
  type BusinessConsoleMrpRunListEnvelope,
  type BusinessConsolePlanningSuggestionItem,
  type BusinessConsolePlanningSuggestionListEnvelope,
  type BusinessConsoleRunMrpRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'

export interface PlanningContextFilters {
  organizationId: string
  environmentId: string
}

export interface PlanningSuggestionFilters extends PlanningContextFilters {
  status: string
}

export interface PlanningRunSelection {
  runId: string
}

export interface PlanningDemandForm {
  organizationId: string
  environmentId: string
  demandType: string
  sourceReference: string
  skuCode: string
  uomCode: string
  siteCode: string
  quantity: number
  dueDate: string
  idempotencyKey: string
}

const PLANNING_QUERY_IDS = [
  'listBusinessConsolePlanningDemands',
  'listBusinessConsolePlanningMrpRuns',
  'getBusinessConsolePlanningMrpPegging',
  'listBusinessConsolePlanningSuggestions',
]

function defaultContextFilters(organizationId: string, environmentId: string): PlanningContextFilters {
  return reactive({
    organizationId,
    environmentId,
  })
}

function defaultSuggestionFilters(organizationId: string, environmentId: string): PlanningSuggestionFilters {
  return reactive({
    organizationId,
    environmentId,
    status: 'open',
  })
}

function defaultDemandForm(organizationId: string, environmentId: string): PlanningDemandForm {
  return reactive({
    organizationId,
    environmentId,
    demandType: 'sales-order',
    sourceReference: '',
    skuCode: 'FG-SHOCK',
    uomCode: 'EA',
    siteCode: 'S1',
    quantity: 100,
    dueDate: new Date().toISOString().slice(0, 10),
    idempotencyKey: '',
  })
}

function defaultRunRequest(organizationId: string, environmentId: string): BusinessConsoleRunMrpRequest {
  return reactive({
    organizationId,
    environmentId,
    horizonStart: new Date().toISOString().slice(0, 10),
    horizonEnd: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10),
  })
}

function defaultRunSelection(): PlanningRunSelection {
  return reactive({
    runId: '',
  })
}

function unwrapItems<T>(envelope: { success?: boolean; data?: { items?: T[] } | null } | undefined): T[] {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
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

export function useBusinessPlanning() {
  const businessContext = useBusinessContextStore()
  const filters = defaultContextFilters(businessContext.organizationId, businessContext.environmentId)
  const suggestionFilters = defaultSuggestionFilters(businessContext.organizationId, businessContext.environmentId)
  const demandForm = defaultDemandForm(businessContext.organizationId, businessContext.environmentId)
  const runRequest = defaultRunRequest(businessContext.organizationId, businessContext.environmentId)
  const runSelection = defaultRunSelection()
  const queryCache = useQueryCache()

  const demandsQuery = useQuery(() =>
    listBusinessConsolePlanningDemandsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
  )
  const runsQuery = useQuery(() =>
    listBusinessConsolePlanningMrpRunsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
  )
  const peggingQuery = useQuery(() => ({
    ...getBusinessConsolePlanningMrpPeggingQueryOptions({
      path: {
        runId: runSelection.runId,
      },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
    enabled: runSelection.runId.trim().length > 0,
  }))
  const suggestionsQuery = useQuery(() =>
    listBusinessConsolePlanningSuggestionsQueryOptions({
      query: {
        organizationId: suggestionFilters.organizationId,
        environmentId: suggestionFilters.environmentId,
        status: suggestionFilters.status,
      },
    }),
  )

  const invalidatePlanningQueries = () =>
    queryCache.invalidateQueries({ predicate: isBusinessQuery(PLANNING_QUERY_IDS) })

  const createDemandMutation = useMutation({
    ...createOrUpdateBusinessConsolePlanningDemandMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })
  const runMrpMutation = useMutation({
    ...runBusinessConsolePlanningMrpMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })
  const acceptSuggestionMutation = useMutation({
    ...acceptBusinessConsolePlanningSuggestionMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })

  function syncContext() {
    suggestionFilters.organizationId = filters.organizationId
    suggestionFilters.environmentId = filters.environmentId
    demandForm.organizationId = filters.organizationId
    demandForm.environmentId = filters.environmentId
    runRequest.organizationId = filters.organizationId
    runRequest.environmentId = filters.environmentId
  }

  return {
    acceptSuggestion: (suggestionId: string, body: BusinessConsoleAcceptPlanningSuggestionRequest) =>
      acceptSuggestionMutation.mutateAsync({
        path: {
          suggestionId,
        },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
        body,
      }),
    acceptSuggestionError: acceptSuggestionMutation.error,
    acceptSuggestionPending: acceptSuggestionMutation.isLoading,
    createDemandError: createDemandMutation.error,
    createDemandPending: createDemandMutation.isLoading,
    createOrUpdateDemand: () =>
      createDemandMutation.mutateAsync({
        body: {
          ...demandForm,
          sourceReference: demandForm.sourceReference || null,
          idempotencyKey: demandForm.idempotencyKey || null,
        },
      }),
    demandForm,
    demands: computed<BusinessConsoleDemandSourceItem[]>(() =>
      unwrapItems(demandsQuery.data.value as BusinessConsoleDemandSourceListEnvelope | undefined),
    ),
    demandsError: demandsQuery.error,
    demandsPending: demandsQuery.isLoading,
    filters,
    mrpRuns: computed<BusinessConsoleMrpRunItem[]>(() =>
      unwrapItems(runsQuery.data.value as BusinessConsoleMrpRunListEnvelope | undefined),
    ),
    mrpRunsError: runsQuery.error,
    mrpRunsPending: runsQuery.isLoading,
    pegging: computed<BusinessConsoleMrpPeggingItem[]>(() =>
      unwrapItems(peggingQuery.data.value as BusinessConsoleMrpPeggingListEnvelope | undefined),
    ),
    peggingError: peggingQuery.error,
    peggingPending: peggingQuery.isLoading,
    refreshPlanning: async () => {
      const queries: Array<Promise<unknown>> = [
        demandsQuery.refetch(),
        runsQuery.refetch(),
        suggestionsQuery.refetch(),
      ]

      if (runSelection.runId.trim().length > 0) {
        queries.push(peggingQuery.refetch())
      }

      await Promise.all(queries)
    },
    runMrp: () => runMrpMutation.mutateAsync({ body: { ...runRequest } }),
    runMrpError: runMrpMutation.error,
    runMrpPending: runMrpMutation.isLoading,
    runRequest,
    runSelection,
    suggestionFilters,
    suggestions: computed<BusinessConsolePlanningSuggestionItem[]>(() =>
      unwrapItems(
        suggestionsQuery.data.value as BusinessConsolePlanningSuggestionListEnvelope | undefined,
      ),
    ),
    suggestionsError: suggestionsQuery.error,
    suggestionsPending: suggestionsQuery.isLoading,
    syncContext,
  }
}
