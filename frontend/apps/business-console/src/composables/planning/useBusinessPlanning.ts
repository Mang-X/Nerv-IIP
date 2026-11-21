import { useMrpRunPolling } from './useMrpRunPolling'
import { usePlanningDownstreamStatuses } from './usePlanningDownstreamStatuses'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import {
  acceptBusinessConsolePlanningSuggestionMutationOptions,
  cancelBusinessConsolePlanningDemandMutationOptions,
  rejectBusinessConsolePlanningSuggestionMutationOptions,
  createBusinessConsolePlanningMpsBucketMutationOptions,
  createOrUpdateBusinessConsolePlanningDemandMutationOptions,
  getBusinessConsolePlanningMrpPeggingQueryOptions,
  listBusinessConsolePlanningDemandsQueryOptions,
  listBusinessConsolePlanningMpsBucketsQueryOptions,
  listBusinessConsolePlanningMrpRunsQueryOptions,
  listBusinessConsolePlanningSuggestionsQueryOptions,
  releaseBusinessConsolePlanningMpsBucketMutationOptions,
  reviewBusinessConsolePlanningMpsBucketMutationOptions,
  runBusinessConsolePlanningMrpMutationOptions,
  updateBusinessConsolePlanningMpsBucketMutationOptions,
  type BusinessConsoleDemandSourceItem,
  type BusinessConsoleDemandSourceListEnvelope,
  type BusinessConsoleMpsBucketItem,
  type BusinessConsoleMpsBucketListEnvelope,
  type BusinessConsoleMrpPeggingItem,
  type BusinessConsoleMrpPeggingListEnvelope,
  type BusinessConsoleMrpRunItem,
  type BusinessConsoleMrpRunListEnvelope,
  type BusinessConsolePlanningSuggestionItem,
  type BusinessConsolePlanningSuggestionListEnvelope,
  type BusinessConsoleRunMrpRequest,
} from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { bindBusinessContext, hasBusinessContext } from '../businessContextBinding'
import { assertEnvelopeSuccess } from '../serviceEnvelope'

export interface PlanningContextFilters {
  organizationId: string
  environmentId: string
}

export interface PlanningSuggestionFilters extends PlanningContextFilters {
  status: string
}

export interface PlanningMpsFilters extends PlanningContextFilters {
  skuCode?: string
  siteCode?: string
  status?: string
}

export interface PlanningRunSelection {
  runId: string
}

export interface PlanningSuggestionTypeFilter {
  /** '', planned supply suggestion, or MRP exception type. Empty means all types. */
  type: string
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

export interface PlanningMpsForm {
  organizationId: string
  environmentId: string
  skuCode: string
  uomCode: string
  siteCode: string
  bucketDate: string
  quantity: number
}

export interface PlanningSuggestionAcceptInput {
  suggestionId: string
  suggestionType: string
}

export interface PlanningSuggestionRejectInput {
  suggestionId: string
  reason: string
}

/** 拒绝原因上限（与网关请求契约一致）。 */
export const SUGGESTION_REJECT_REASON_MAX_LENGTH = 128

export {
  type ActiveMrpRunState,
  MRP_RUN_POLL_INTERVAL_MS,
  MRP_RUN_POLL_TIMEOUT_MS,
} from './useMrpRunPolling'

const PLANNING_QUERY_IDS = [
  'listBusinessConsolePlanningDemands',
  'listBusinessConsolePlanningMpsBuckets',
  'listBusinessConsolePlanningMrpRuns',
  'getBusinessConsolePlanningMrpPegging',
  'listBusinessConsolePlanningSuggestions',
]

function defaultContextFilters(
  organizationId: string,
  environmentId: string,
): PlanningContextFilters {
  return bindBusinessContext(
    reactive({
      organizationId,
      environmentId,
    }),
  )
}

function defaultSuggestionFilters(
  organizationId: string,
  environmentId: string,
): PlanningSuggestionFilters {
  return bindBusinessContext(
    reactive({
      organizationId,
      environmentId,
      status: 'all',
    }),
  )
}

function defaultMpsFilters(organizationId: string, environmentId: string): PlanningMpsFilters {
  return bindBusinessContext(
    reactive({
      organizationId,
      environmentId,
      skuCode: undefined,
      siteCode: undefined,
      status: undefined,
    }),
  )
}

function defaultDemandForm(organizationId: string, environmentId: string): PlanningDemandForm {
  return bindBusinessContext(
    reactive({
      organizationId,
      environmentId,
      demandType: 'forecast',
      sourceReference: '',
      skuCode: '',
      uomCode: '',
      siteCode: '',
      quantity: 0,
      dueDate: new Date().toISOString().slice(0, 10),
      idempotencyKey: '',
    }),
  )
}

function defaultMpsForm(organizationId: string, environmentId: string): PlanningMpsForm {
  return bindBusinessContext(
    reactive({
      organizationId,
      environmentId,
      skuCode: '',
      uomCode: '',
      siteCode: '',
      bucketDate: new Date().toISOString().slice(0, 10),
      quantity: 0,
    }),
  )
}

function defaultRunRequest(
  organizationId: string,
  environmentId: string,
): BusinessConsoleRunMrpRequest {
  return bindBusinessContext(
    reactive({
      organizationId,
      environmentId,
      horizonStart: new Date().toISOString().slice(0, 10),
      horizonEnd: new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10),
    }),
  )
}

function defaultRunSelection(): PlanningRunSelection {
  return reactive({
    runId: '',
  })
}

function unwrapItems<T>(
  envelope: { success?: boolean; data?: { items?: T[] } | null } | undefined,
): T[] {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function planningReadError(
  query: {
    error: { value: Error | null }
    data: { value?: { success?: boolean; message?: string | null } }
  },
  fallback: string,
) {
  return computed(
    () =>
      query.error.value ??
      (query.data.value?.success === false
        ? new Error(query.data.value.message || fallback)
        : null),
  )
}

function isBusinessQuery(ids: string[]) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return (
        typeof part === 'object' && part !== null && '_id' in part && ids.includes(String(part._id))
      )
    })
  }
}

function ignoreBackgroundError(_error: unknown) {}

export function useBusinessPlanning(
  sourceOrder: () => string = () => '',
  downstreamPage?: () => readonly BusinessConsolePlanningSuggestionItem[],
) {
  const auth = useAuthStore()
  const canRead = (permission: string) =>
    auth.principal?.permissionCodes?.includes(permission) === true
  const businessContext = useBusinessContextStore()
  const filters = defaultContextFilters(
    businessContext.organizationId,
    businessContext.environmentId,
  )
  const mpsFilters = defaultMpsFilters(
    businessContext.organizationId,
    businessContext.environmentId,
  )
  const suggestionFilters = defaultSuggestionFilters(
    businessContext.organizationId,
    businessContext.environmentId,
  )
  const demandForm = defaultDemandForm(
    businessContext.organizationId,
    businessContext.environmentId,
  )
  const mpsForm = defaultMpsForm(businessContext.organizationId, businessContext.environmentId)
  const runRequest = defaultRunRequest(
    businessContext.organizationId,
    businessContext.environmentId,
  )
  const runSelection = defaultRunSelection()
  // 计划建议「分型筛选」(生产/采购)，纯前端过滤，不带入后端查询。
  const suggestionTypeFilter = reactive<PlanningSuggestionTypeFilter>({ type: '' })
  const queryCache = useQueryCache()

  const demandsQuery = useQuery(() => ({
    ...listBusinessConsolePlanningDemandsQueryOptions({
      query: {
        ...(sourceOrder() ? { keyword: sourceOrder(), take: 200 } : {}),
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
    enabled: hasBusinessContext(filters) && canRead(P.planningDemandsRead),
  }))
  const runsQuery = useQuery(() => ({
    ...listBusinessConsolePlanningMrpRunsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
    enabled: hasBusinessContext(filters) && canRead(P.planningMrpRead),
  }))
  const mpsBucketsQuery = useQuery(() => ({
    ...listBusinessConsolePlanningMpsBucketsQueryOptions({
      query: {
        organizationId: mpsFilters.organizationId,
        environmentId: mpsFilters.environmentId,
        skuCode: mpsFilters.skuCode?.trim() || undefined,
        siteCode: mpsFilters.siteCode?.trim() || undefined,
        status: mpsFilters.status?.trim() || undefined,
      },
    }),
    enabled: hasBusinessContext(mpsFilters) && canRead(P.planningMpsRead),
  }))
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
    enabled:
      canRead(P.planningMrpRead) &&
      hasBusinessContext(filters) &&
      runSelection.runId.trim().length > 0,
  }))
  const suggestionsQuery = useQuery(() => ({
    ...listBusinessConsolePlanningSuggestionsQueryOptions({
      query: {
        organizationId: suggestionFilters.organizationId,
        environmentId: suggestionFilters.environmentId,
        status: suggestionFilters.status === 'all' ? undefined : suggestionFilters.status,
      },
    }),
    enabled: hasBusinessContext(suggestionFilters) && canRead(P.planningMrpRead),
  }))

  const suggestions = computed<BusinessConsolePlanningSuggestionItem[]>(() =>
    unwrapItems(
      suggestionsQuery.data.value as BusinessConsolePlanningSuggestionListEnvelope | undefined,
    ),
  )
  const { downstreamStatusesQuery, refreshDownstreamStatuses } = usePlanningDownstreamStatuses(
    filters,
    suggestionFilters,
    suggestions,
    downstreamPage,
    () => auth.principal?.principalId ?? '',
    canRead,
  )

  const invalidatePlanningQueries = () =>
    queryCache.invalidateQueries({ predicate: isBusinessQuery(PLANNING_QUERY_IDS) })

  const mrpRuns = computed<BusinessConsoleMrpRunItem[]>(() =>
    unwrapItems(runsQuery.data.value as BusinessConsoleMrpRunListEnvelope | undefined),
  )

  const createDemandMutation = useMutation({
    ...createOrUpdateBusinessConsolePlanningDemandMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })
  const cancelDemandMutation = useMutation({
    ...cancelBusinessConsolePlanningDemandMutationOptions(),
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

  const { activeMrpRun, trackMrpRun } = useMrpRunPolling(
    mrpRuns,
    () => runsQuery.refetch(),
    () => void invalidatePlanningQueries().catch(ignoreBackgroundError),
  )

  const createMpsMutation = useMutation({
    ...createBusinessConsolePlanningMpsBucketMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })
  const updateMpsMutation = useMutation({
    ...updateBusinessConsolePlanningMpsBucketMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })
  const reviewMpsMutation = useMutation({
    ...reviewBusinessConsolePlanningMpsBucketMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })
  const releaseMpsMutation = useMutation({
    ...releaseBusinessConsolePlanningMpsBucketMutationOptions(),
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
  const rejectSuggestionMutation = useMutation({
    ...rejectBusinessConsolePlanningSuggestionMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })

  function downstreamTargetForSuggestion(suggestionType: string) {
    if (['reschedule-in', 'reschedule-out', 'cancel'].includes(suggestionType)) {
      return { downstreamService: 'ScheduledReceipt', downstreamDocumentType: 'ScheduledReceipt' }
    }
    if (suggestionType === 'planned-work-order') {
      return {
        downstreamService: 'BusinessMes',
        downstreamDocumentType: 'WorkOrder',
      }
    }

    if (suggestionType === 'planned-purchase') {
      return {
        downstreamService: 'BusinessErp',
        downstreamDocumentType: 'PurchaseRequisition',
      }
    }

    throw new Error('当前计划建议类型暂不支持接受。')
  }

  function syncContext() {
    mpsFilters.organizationId = filters.organizationId
    mpsFilters.environmentId = filters.environmentId
    suggestionFilters.organizationId = filters.organizationId
    suggestionFilters.environmentId = filters.environmentId
    demandForm.organizationId = filters.organizationId
    demandForm.environmentId = filters.environmentId
    mpsForm.organizationId = filters.organizationId
    mpsForm.environmentId = filters.environmentId
    runRequest.organizationId = filters.organizationId
    runRequest.environmentId = filters.environmentId
  }

  function currentPlannerIdentity() {
    return (
      auth.principal?.loginName?.trim() || auth.principal?.principalId?.trim() || 'unknown-user'
    )
  }

  return {
    acceptSuggestion: (input: PlanningSuggestionAcceptInput) => {
      const target = downstreamTargetForSuggestion(input.suggestionType)
      return acceptSuggestionMutation.mutateAsync({
        path: { suggestionId: input.suggestionId },
        query: {
          organizationId: suggestionFilters.organizationId,
          environmentId: suggestionFilters.environmentId,
        },
        body: {
          downstreamService: target.downstreamService,
          downstreamDocumentType: target.downstreamDocumentType,
          downstreamDocumentId: null,
          idempotencyKey: `planning-accept:${suggestionFilters.organizationId}:${suggestionFilters.environmentId}:${input.suggestionId}`,
        },
      })
    },
    acceptSuggestionError: acceptSuggestionMutation.error,
    acceptSuggestionPending: acceptSuggestionMutation.isLoading,
    createMpsBucket: () =>
      createMpsMutation.mutateAsync({
        body: {
          ...mpsForm,
        },
      }),
    createMpsBucketError: createMpsMutation.error,
    createMpsBucketPending: createMpsMutation.isLoading,
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
    cancelDemand: async (demandSourceId: string) =>
      assertEnvelopeSuccess(
        await cancelDemandMutation.mutateAsync({
          path: { demandSourceId },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
        '作废需求失败，请稍后重试。',
      ),
    cancelDemandPending: cancelDemandMutation.isLoading,
    demandForm,
    demands: computed<BusinessConsoleDemandSourceItem[]>(() =>
      unwrapItems(demandsQuery.data.value as BusinessConsoleDemandSourceListEnvelope | undefined),
    ),
    demandsError: planningReadError(demandsQuery, '读取计划需求失败。'),
    demandsPending: demandsQuery.isLoading,
    filters,
    mpsBuckets: computed<BusinessConsoleMpsBucketItem[]>(() =>
      unwrapItems(mpsBucketsQuery.data.value as BusinessConsoleMpsBucketListEnvelope | undefined),
    ),
    mpsBucketsError: mpsBucketsQuery.error,
    mpsBucketsPending: mpsBucketsQuery.isLoading,
    mpsFilters,
    mpsForm,
    mrpRuns,
    mrpRunsError: planningReadError(runsQuery, '读取 MRP 运行失败。'),
    mrpRunsPending: runsQuery.isLoading,
    pegging: computed<BusinessConsoleMrpPeggingItem[]>(() =>
      unwrapItems(peggingQuery.data.value as BusinessConsoleMrpPeggingListEnvelope | undefined),
    ),
    peggingError: peggingQuery.error,
    peggingPending: peggingQuery.isLoading,
    refreshPlanning: async () => {
      if (!hasBusinessContext(filters)) {
        return
      }

      const queries: Array<Promise<unknown>> = []
      if (canRead(P.planningDemandsRead)) queries.push(demandsQuery.refetch())
      if (canRead(P.planningMpsRead)) queries.push(mpsBucketsQuery.refetch())
      if (canRead(P.planningMrpRead)) {
        queries.push(runsQuery.refetch(), suggestionsQuery.refetch())
        if (runSelection.runId) queries.push(peggingQuery.refetch())
        queries.push(...refreshDownstreamStatuses())
      }
      await Promise.all(queries)
    },
    rejectSuggestion: async (input: PlanningSuggestionRejectInput) => {
      const reason = input.reason.trim()
      if (!reason) {
        throw new Error('请填写拒绝原因。')
      }
      if (reason.length > SUGGESTION_REJECT_REASON_MAX_LENGTH) {
        throw new Error(`拒绝原因不能超过 ${SUGGESTION_REJECT_REASON_MAX_LENGTH} 字。`)
      }
      const envelope = await rejectSuggestionMutation.mutateAsync({
        path: { suggestionId: input.suggestionId },
        query: {
          organizationId: suggestionFilters.organizationId,
          environmentId: suggestionFilters.environmentId,
        },
        // actor 由网关按登录主体注入，前端不传。
        body: { reason },
      })
      return assertEnvelopeSuccess(envelope, '计划建议拒绝失败，请稍后重试。')
    },
    rejectSuggestionError: rejectSuggestionMutation.error,
    rejectSuggestionPending: rejectSuggestionMutation.isLoading,
    activeMrpRun,
    // 提交即受理（202 + runId）：受理成功后开始轮询运行状态直到终态；实际结果看 activeMrpRun。
    runMrp: async () => {
      const envelope = await runMrpMutation.mutateAsync({ body: { ...runRequest } })
      const runId = envelope?.data?.runId?.trim() ?? ''
      if (runId) {
        trackMrpRun(runId)
      }
      return envelope
    },
    runMrpError: runMrpMutation.error,
    runMrpPending: runMrpMutation.isLoading,
    runRequest,
    runSelection,
    releaseMpsBucket: (mpsId: string) =>
      releaseMpsMutation.mutateAsync({
        path: { mpsId },
        query: {
          organizationId: mpsFilters.organizationId,
          environmentId: mpsFilters.environmentId,
        },
        body: { releasedBy: currentPlannerIdentity() },
      }),
    releaseMpsBucketError: releaseMpsMutation.error,
    releaseMpsBucketPending: releaseMpsMutation.isLoading,
    reviewMpsBucket: (mpsId: string) =>
      reviewMpsMutation.mutateAsync({
        path: { mpsId },
        query: {
          organizationId: mpsFilters.organizationId,
          environmentId: mpsFilters.environmentId,
        },
        body: { reviewedBy: currentPlannerIdentity() },
      }),
    reviewMpsBucketError: reviewMpsMutation.error,
    reviewMpsBucketPending: reviewMpsMutation.isLoading,
    suggestionFilters,
    suggestionTypeFilter,
    suggestions,
    downstreamStatuses: computed(() => downstreamStatusesQuery.data.value ?? {}),
    suggestionsError: planningReadError(suggestionsQuery, '计划建议读取失败。'),
    suggestionsPending: suggestionsQuery.isLoading,
    syncContext,
    updateMpsBucket: (mpsId: string) =>
      updateMpsMutation.mutateAsync({
        path: { mpsId },
        body: {
          organizationId: mpsForm.organizationId,
          environmentId: mpsForm.environmentId,
          skuCode: mpsForm.skuCode,
          uomCode: mpsForm.uomCode,
          siteCode: mpsForm.siteCode,
          bucketDate: mpsForm.bucketDate,
          quantity: mpsForm.quantity,
        },
      }),
    updateMpsBucketError: updateMpsMutation.error,
    updateMpsBucketPending: updateMpsMutation.isLoading,
  }
}
