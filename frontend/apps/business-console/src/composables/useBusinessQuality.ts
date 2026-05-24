import {
  closeBusinessConsoleQualityNcrMutationOptions,
  createBusinessConsoleQualityInspectionRecordMutationOptions,
  listBusinessConsoleQualityInspectionPlansQueryOptions,
  listBusinessConsoleQualityNcrsQueryOptions,
  submitBusinessConsoleQualityNcrDispositionMutationOptions,
  type BusinessConsoleCreateInspectionRecordRequest,
  type BusinessConsoleNcrCloseRequest,
  type BusinessConsoleNcrDispositionRequest,
  type BusinessConsoleQualityItem,
  type BusinessConsoleQualityListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

export interface QualityListFilters {
  organizationId: string
  environmentId: string
  status?: string
  take: number
}

function defaultFilters(): QualityListFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    take: DEFAULT_TAKE,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function toListQuery(filters: QualityListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('status', filters.status),
    take: filters.take,
  }
}

function listItems(envelope: BusinessConsoleQualityListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && part._id === id
    })
  }
}

function ignoreBackgroundError(_error: unknown) {}

export function useQualityInspectionPlans() {
  const filters = defaultFilters()

  const plansQuery = useQuery(() =>
    listBusinessConsoleQualityInspectionPlansQueryOptions({
      query: toListQuery(filters),
    }),
  )

  const createRecordMutation = useMutation(
    createBusinessConsoleQualityInspectionRecordMutationOptions(),
  )

  return {
    createInspectionRecord: (body: BusinessConsoleCreateInspectionRecordRequest) =>
      createRecordMutation.mutateAsync({ body }),
    createInspectionRecordError: createRecordMutation.error,
    createInspectionRecordPending: createRecordMutation.isLoading,
    filters,
    inspectionPlans: computed<BusinessConsoleQualityItem[]>(() => listItems(plansQuery.data.value)),
    inspectionPlansError: plansQuery.error,
    inspectionPlansPending: plansQuery.isLoading,
    refreshInspectionPlans: plansQuery.refetch,
  }
}

export function useQualityNcrs() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const ncrsQuery = useQuery(() =>
    listBusinessConsoleQualityNcrsQueryOptions({
      query: toListQuery(filters),
    }),
  )

  const submitDispositionMutation = useMutation({
    ...submitBusinessConsoleQualityNcrDispositionMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleQualityNcrs') })
        .catch(ignoreBackgroundError)
    },
  })

  const closeNcrMutation = useMutation({
    ...closeBusinessConsoleQualityNcrMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleQualityNcrs') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    closeNcr: (ncrId: string, body: BusinessConsoleNcrCloseRequest) =>
      closeNcrMutation.mutateAsync({
        path: {
          ncrId,
        },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
        body,
      }),
    closeNcrError: closeNcrMutation.error,
    closeNcrPending: closeNcrMutation.isLoading,
    filters,
    ncrs: computed<BusinessConsoleQualityItem[]>(() => listItems(ncrsQuery.data.value)),
    ncrsError: ncrsQuery.error,
    ncrsPending: ncrsQuery.isLoading,
    refreshNcrs: ncrsQuery.refetch,
    submitDisposition: (ncrId: string, body: BusinessConsoleNcrDispositionRequest) =>
      submitDispositionMutation.mutateAsync({
        path: {
          ncrId,
        },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
        body,
      }),
    submitDispositionError: submitDispositionMutation.error,
    submitDispositionPending: submitDispositionMutation.isLoading,
  }
}
