import {
  getBusinessConsoleSchedulingPlanQueryOptions,
  listBusinessConsoleSchedulingPlansQueryOptions,
  releaseBusinessConsoleSchedulingPlanMutationOptions,
  revokeBusinessConsoleSchedulingPlanMutationOptions,
  upsertBusinessConsoleSchedulingOperationOverrideMutationOptions,
  type BusinessConsoleSchedulePlanEnvelope,
  type BusinessConsoleSchedulingPlanSummaryListEnvelope,
  type BusinessConsoleSchedulingPlanSummaryResponse,
  type BusinessConsoleSchedulePlan,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  type BusinessContextFields,
} from './businessContextBinding'
import { assertEnvelopeSuccess } from './serviceEnvelope'

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
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      pageIndex: 1,
      pageSize: SINGLE_PAGE_PLAN_LIST_SIZE,
    }),
  )
}

function defaultSelection(): SchedulingPlanSelection {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      planId: '',
    }),
  )
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
      return (
        typeof part === 'object' && part !== null && '_id' in part && ids.includes(String(part._id))
      )
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
    // Scheduling ListSchedulePlans is a 0-based pageIndex contract (Skip(pageIndex * pageSize),
    // asserted by SchedulingEndpointContractTests). `page` is the 1-based UI page, so a page of 1
    // must map to API pageIndex 0 — otherwise the first 100 plans are skipped and the workbench
    // shows nothing until there are >100 plans.
    filters.pageIndex = Math.max(0, page.value - 1)
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

  const revokeMutation = useMutation({
    ...revokeBusinessConsoleSchedulingPlanMutationOptions(),
    onSuccess() {
      void invalidateSchedulingQueries().catch(ignoreBackgroundError)
    },
  })

  const operationOverrideMutation = useMutation({
    ...upsertBusinessConsoleSchedulingOperationOverrideMutationOptions(),
    onSuccess() {
      void invalidateSchedulingQueries().catch(ignoreBackgroundError)
    },
  })

  function mutationScope() {
    return {
      organizationId: detailSelection.organizationId || filters.organizationId,
      environmentId: detailSelection.environmentId || filters.environmentId,
    }
  }

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
      unwrapPlans(
        plansQuery.data.value as BusinessConsoleSchedulingPlanSummaryListEnvelope | undefined,
      ),
    ),
    plansError: plansQuery.error,
    plansPending: plansQuery.isLoading,
    // 与 revoke/override 同款诚实失败：200 + success:false 一律抛错，不给界面假成功。
    releasePlan: (planId: string) =>
      releaseMutation
        .mutateAsync({
          path: { planId },
          query: mutationScope(),
        })
        .then((envelope) => assertEnvelopeSuccess(envelope, '排程服务未确认发布结果。')),
    releasePlanError: releaseMutation.error,
    releasePlanPending: releaseMutation.isLoading,
    // 撤销已发布方案：MES 侧回流撤销对应工序排程。scope 为空不发请求。
    revokePlan: (planId: string) => {
      const scope = mutationScope()
      if (!hasBusinessContext(scope) || !planId.trim()) {
        return Promise.reject(new Error('缺少组织/环境上下文或方案标识，未发起撤销请求。'))
      }
      return revokeMutation
        .mutateAsync({
          path: { planId },
          query: scope,
        })
        .then((envelope) => assertEnvelopeSuccess(envelope, '排程服务未确认撤销结果。'))
    },
    revokePlanError: revokeMutation.error,
    revokePlanPending: revokeMutation.isLoading,
    // 单工序持久化 override：落库后被后端 create/preview/assemble 三条建方案路径自动叠加继承。
    upsertOperationOverride: (input: {
      planId: string
      operationId: string
      resourceId: string
      startUtc?: string
      endUtc?: string
    }) => {
      const scope = mutationScope()
      if (
        !hasBusinessContext(scope) ||
        !input.planId.trim() ||
        !input.operationId.trim() ||
        !input.resourceId.trim()
      ) {
        return Promise.reject(
          new Error('缺少组织/环境上下文、方案、工序或资源标识，未发起持久化请求。'),
        )
      }
      return operationOverrideMutation
        .mutateAsync({
          path: { planId: input.planId, operationId: input.operationId },
          body: {
            ...scope,
            resourceId: input.resourceId,
            startUtc: input.startUtc,
            endUtc: input.endUtc,
          },
        })
        .then((envelope) => assertEnvelopeSuccess(envelope, '排程服务未确认工序持久化结果。'))
    },
    upsertOperationOverrideError: operationOverrideMutation.error,
    upsertOperationOverridePending: operationOverrideMutation.isLoading,
    refreshPlanDetail: () =>
      hasBusinessContext(detailSelection) && detailSelection.planId.trim().length > 0
        ? detailQuery.refetch()
        : Promise.resolve(),
    refreshPlans: () => refetchWithBusinessContext(filters, plansQuery),
  }
}
