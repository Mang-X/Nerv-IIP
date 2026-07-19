import {
  closeBusinessConsoleQualityNcrMutationOptions,
  createBusinessConsoleQualityInspectionRecordMutationOptions,
  getBusinessConsoleQualityInspectionRecordQueryOptions,
  listBusinessConsoleQualityInspectionPlanCharacteristicsQueryOptions,
  listBusinessConsoleQualityInspectionPlansQueryOptions,
  listBusinessConsoleQualityNcrsQueryOptions,
  submitBusinessConsoleQualityNcrDispositionMutationOptions,
  type BusinessConsoleCreateInspectionRecordRequest,
  type BusinessConsoleInspectionRecordDetailResponse,
  type BusinessConsoleInspectionPlanCharacteristicItem,
  type BusinessConsoleNcrCloseRequest,
  type BusinessConsoleNcrDispositionRequest,
  type BusinessConsoleQualityItem,
  type BusinessConsoleQualityListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  type BusinessContextFields,
} from './businessContextBinding'

const DEFAULT_TAKE = 100

export interface QualityListFilters extends BusinessContextFields {
  status?: string
  keyword?: string
  skip: number
  take: number
}

function defaultFilters(initial: Partial<QualityListFilters> = {}): QualityListFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
      ...initial,
    }),
  )
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

export interface QualityInspectionRecordSource {
  organizationId: string
  environmentId: string
  inspectionRecordId: string
}

export interface QualityInspectionPlanCharacteristicsSource extends BusinessContextFields {
  inspectionPlanId: string
}

export function useQualityInspectionPlanCharacteristics(
  source: () => QualityInspectionPlanCharacteristicsSource,
) {
  const enabled = computed(() => {
    const value = source()
    return (
      value.organizationId.trim().length > 0 &&
      value.environmentId.trim().length > 0 &&
      value.inspectionPlanId.trim().length > 0
    )
  })
  const characteristicsQuery = useQuery(() => {
    const value = source()
    return {
      ...listBusinessConsoleQualityInspectionPlanCharacteristicsQueryOptions({
        path: { inspectionPlanId: value.inspectionPlanId },
        query: {
          organizationId: value.organizationId,
          environmentId: value.environmentId,
        },
      }),
      enabled: enabled.value,
    }
  })

  return {
    planCharacteristics: computed<BusinessConsoleInspectionPlanCharacteristicItem[]>(() => {
      const data = characteristicsQuery.data.value
      return data?.success ? (data.data?.items ?? []) : []
    }),
    planCharacteristicsError: characteristicsQuery.error,
    planCharacteristicsPending: characteristicsQuery.isLoading,
    refreshPlanCharacteristics: () =>
      enabled.value ? characteristicsQuery.refetch() : Promise.resolve(),
  }
}

// 单条检验记录详情读面（get by id，QualityInspectionRecordsRead）。供来源检验记录互链按 inspectionRecordId 定位。
export function useQualityInspectionRecordDetail(source: () => QualityInspectionRecordSource) {
  const enabled = computed(() => {
    const s = source()
    return (
      s.organizationId.trim().length > 0 &&
      s.environmentId.trim().length > 0 &&
      s.inspectionRecordId.trim().length > 0
    )
  })

  const recordQuery = useQuery(() => {
    const s = source()
    return {
      ...getBusinessConsoleQualityInspectionRecordQueryOptions({
        path: { inspectionRecordId: s.inspectionRecordId },
        query: { organizationId: s.organizationId, environmentId: s.environmentId },
      }),
      enabled: enabled.value,
    }
  })

  return {
    record: computed<BusinessConsoleInspectionRecordDetailResponse | undefined>(() => {
      const data = recordQuery.data.value
      return data?.success ? (data.data ?? undefined) : undefined
    }),
    recordPending: recordQuery.isLoading,
    recordError: recordQuery.error,
    refreshRecord: () => (enabled.value ? recordQuery.refetch() : Promise.resolve()),
  }
}

export function useQualityInspectionPlans(initialFilters: Partial<QualityListFilters> = {}) {
  const filters = defaultFilters(initialFilters)

  const plansQuery = useQuery(() => ({
    ...listBusinessConsoleQualityInspectionPlansQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: hasBusinessContext(filters),
  }))

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
    refreshInspectionPlans: () => refetchWithBusinessContext(filters, plansQuery),
  }
}

export function useQualityNcrs(initialFilters: Partial<QualityListFilters> = {}) {
  const filters = defaultFilters(initialFilters)
  const queryCache = useQueryCache()

  const ncrsQuery = useQuery(() => ({
    ...listBusinessConsoleQualityNcrsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: hasBusinessContext(filters),
  }))

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
    refreshNcrs: () => refetchWithBusinessContext(filters, ncrsQuery),
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
