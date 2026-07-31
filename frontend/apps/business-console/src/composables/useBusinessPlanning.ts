import {
  acceptBusinessConsolePlanningSuggestionMutationOptions,
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
import { computed, getCurrentScope, onScopeDispose, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  withBusinessContextEnabled,
} from './businessContextBinding'
import { assertEnvelopeSuccess } from './serviceEnvelope'

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

/**
 * 运行 MRP 的异步跟踪状态（#1306）：提交即受理（202 + runId），
 * 之后轮询运行列表读面直到终态；弹框全程可关闭，后台继续跑。
 */
export interface ActiveMrpRunState {
  runId: string
  /** '' = 无进行中的运行。polling-timeout = 轮询超时但任务可能仍在后台执行。 */
  status: '' | 'queued' | 'running' | 'completed' | 'failed' | 'polling-timeout'
  failureReason: string
  suggestionCount: number | null
}

/** 轮询间隔与上限：MRP 实测十余秒量级，2s 步进、5 分钟封顶足够并且不刷爆网关。 */
export const MRP_RUN_POLL_INTERVAL_MS = 2_000
export const MRP_RUN_POLL_TIMEOUT_MS = 5 * 60 * 1_000

function normalizeMrpRunStatus(status: string | null | undefined): ActiveMrpRunState['status'] {
  const value = (status ?? '').toLowerCase()
  if (value === 'completed') return 'completed'
  if (value === 'failed') return 'failed'
  if (value === 'running') return 'running'
  // Created（服务端排队态）与未知值都按排队中处理，等待下一次轮询澄清。
  return 'queued'
}

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
      status: 'open',
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

export function useBusinessPlanning() {
  const auth = useAuthStore()
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

  const demandsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsolePlanningDemandsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
      }),
      filters,
    ),
  )
  const runsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsolePlanningMrpRunsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
      }),
      filters,
    ),
  )
  const mpsBucketsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsolePlanningMpsBucketsQueryOptions({
        query: {
          organizationId: mpsFilters.organizationId,
          environmentId: mpsFilters.environmentId,
          skuCode: mpsFilters.skuCode?.trim() || undefined,
          siteCode: mpsFilters.siteCode?.trim() || undefined,
          status: mpsFilters.status?.trim() || undefined,
        },
      }),
      mpsFilters,
    ),
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
    enabled: hasBusinessContext(filters) && runSelection.runId.trim().length > 0,
  }))
  const suggestionsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsolePlanningSuggestionsQueryOptions({
        query: {
          organizationId: suggestionFilters.organizationId,
          environmentId: suggestionFilters.environmentId,
          status: suggestionFilters.status,
        },
      }),
      suggestionFilters,
    ),
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
  const runMrpMutation = useMutation({
    ...runBusinessConsolePlanningMrpMutationOptions(),
    onSuccess() {
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
    },
  })

  // —— 运行 MRP 异步跟踪（#1306）——提交受理后轮询运行列表直到终态。
  const activeMrpRun = reactive<ActiveMrpRunState>({
    runId: '',
    status: '',
    failureReason: '',
    suggestionCount: null,
  })
  let mrpPollTimer: ReturnType<typeof setTimeout> | null = null
  let mrpPollDeadline = 0

  function stopMrpRunPolling() {
    if (mrpPollTimer !== null) {
      clearTimeout(mrpPollTimer)
      mrpPollTimer = null
    }
  }

  if (getCurrentScope()) {
    onScopeDispose(stopMrpRunPolling)
  }

  function applyActiveRunSnapshot(run: BusinessConsoleMrpRunItem | undefined): boolean {
    if (!run) {
      return false
    }

    activeMrpRun.status = normalizeMrpRunStatus(run.status)
    if (activeMrpRun.status === 'completed') {
      activeMrpRun.suggestionCount = run.suggestionCount ?? 0
    }
    if (activeMrpRun.status === 'failed') {
      activeMrpRun.failureReason = run.failureReason ?? ''
    }
    return activeMrpRun.status === 'completed' || activeMrpRun.status === 'failed'
  }

  async function pollActiveMrpRunOnce() {
    mrpPollTimer = null
    if (!activeMrpRun.runId) {
      return
    }

    try {
      await runsQuery.refetch()
    } catch {
      // 单次轮询失败不终止跟踪：网络抖动交给下一轮，超时判定兜底。
    }

    const run = mrpRuns.value.find((item) => item.runId === activeMrpRun.runId)
    if (applyActiveRunSnapshot(run)) {
      // 终态：建议/KPI 等读面统一失效重取。
      void invalidatePlanningQueries().catch(ignoreBackgroundError)
      return
    }

    if (Date.now() >= mrpPollDeadline) {
      // 轮询超时 ≠ 任务失败：后台可能仍在计算，把决定权还给用户（运行列表可回看）。
      activeMrpRun.status = 'polling-timeout'
      return
    }

    mrpPollTimer = setTimeout(() => void pollActiveMrpRunOnce(), MRP_RUN_POLL_INTERVAL_MS)
  }

  function trackMrpRun(runId: string) {
    stopMrpRunPolling()
    activeMrpRun.runId = runId
    activeMrpRun.status = 'queued'
    activeMrpRun.failureReason = ''
    activeMrpRun.suggestionCount = null
    mrpPollDeadline = Date.now() + MRP_RUN_POLL_TIMEOUT_MS
    mrpPollTimer = setTimeout(() => void pollActiveMrpRunOnce(), MRP_RUN_POLL_INTERVAL_MS)
  }
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
    demandForm,
    demands: computed<BusinessConsoleDemandSourceItem[]>(() =>
      unwrapItems(demandsQuery.data.value as BusinessConsoleDemandSourceListEnvelope | undefined),
    ),
    demandsError: demandsQuery.error,
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
    mrpRunsError: runsQuery.error,
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

      const queries: Array<Promise<unknown>> = [
        demandsQuery.refetch(),
        mpsBucketsQuery.refetch(),
        runsQuery.refetch(),
        suggestionsQuery.refetch(),
      ]

      if (runSelection.runId.trim().length > 0) {
        queries.push(peggingQuery.refetch())
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
    suggestions: computed<BusinessConsolePlanningSuggestionItem[]>(() =>
      unwrapItems(
        suggestionsQuery.data.value as BusinessConsolePlanningSuggestionListEnvelope | undefined,
      ),
    ),
    suggestionsError: suggestionsQuery.error,
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
