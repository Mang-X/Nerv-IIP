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
  keyword?: string
  skip: number
  take: number
}

function defaultFilters(initial: Partial<QualityListFilters> = {}): QualityListFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    ...optionalQuery('status', initial.status),
    ...optionalQuery('keyword', initial.keyword),
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
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
    ...optionalQuery('keyword', filters.keyword),
    skip: filters.skip,
    take: filters.take,
  }
}

function listItems(envelope: BusinessConsoleQualityListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function listTotal(envelope: BusinessConsoleQualityListEnvelope | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
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

export function useQualityInspectionPlans(initialFilters: Partial<QualityListFilters> = {}) {
  const filters = defaultFilters(initialFilters)

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
    inspectionPlansTotal: computed(() => listTotal(plansQuery.data.value)),
    refreshInspectionPlans: plansQuery.refetch,
  }
}

export function useQualityNcrs(initialFilters: Partial<QualityListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
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
    ncrsTotal: computed(() => listTotal(ncrsQuery.data.value)),
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
