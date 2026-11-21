import {
  convertBusinessConsoleMesPlanToWorkOrderMutationOptions,
  getBusinessConsoleMesFoundationReadinessQueryOptions,
  getBusinessConsoleMesOverviewQueryOptions,
  getBusinessConsoleMesProductionPlanReadinessQueryOptions,
  listBusinessConsoleMesProductionPlansQueryOptions,
  type BusinessConsoleMesFoundationReadinessEnvelope,
  type BusinessConsoleMesOverviewEnvelope,
  type BusinessConsoleMesProductionPlanListEnvelope,
  type BusinessConsoleMesProductionPlanRow,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import { businessReadState } from '../shared/businessReadState'
import {
  defaultContext,
  defaultFilters,
  defaultFoundationFilters,
  envelopeItems,
  envelopeTotal,
  ignoreBackgroundError,
  invalidateMesQueries,
  isNonEmpty,
  toContextQuery,
  toFoundationQuery,
  toListQuery,
  unwrapData,
} from './mesShared'

export function useMesProductionPlans() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const plansQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesProductionPlansQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )

  const convertMutation = useMutation({
    ...convertBusinessConsoleMesPlanToWorkOrderMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesProductionPlans',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesOverview',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    convertPlanToWorkOrder: (
      productionPlanId: string,
      body: {
        organizationId: string
        environmentId: string
        workOrderId?: string
        workCenterId?: string
        dueUtc?: string
        idempotencyKey: string
      },
    ) =>
      convertMutation.mutateAsync({
        path: { productionPlanId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    convertPlanToWorkOrderError: convertMutation.error,
    convertPlanToWorkOrderPending: convertMutation.isLoading,
    filters,
    productionPlans: computed<BusinessConsoleMesProductionPlanRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesProductionPlanRow,
        BusinessConsoleMesProductionPlanListEnvelope
      >(plansQuery.data.value),
    ),
    productionPlansError: plansQuery.error,
    productionPlansPending: plansQuery.isLoading,
    productionPlansState: businessReadState(plansQuery, () => hasBusinessContext(filters)),
    productionPlansTotal: computed(() => envelopeTotal(plansQuery.data.value)),
    refreshProductionPlans: () => refetchWithBusinessContext(filters, plansQuery),
  }
}

export function useMesProductionPlanReadiness(productionPlanId = '') {
  const filters = bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      productionPlanId,
    }),
  )
  const readinessEnabled = computed(
    () => hasBusinessContext(filters) && isNonEmpty(filters.productionPlanId),
  )

  const readinessQuery = useQuery(() => ({
    ...getBusinessConsoleMesProductionPlanReadinessQueryOptions({
      path: { productionPlanId: filters.productionPlanId },
      query: toContextQuery(filters),
    }),
    enabled: readinessEnabled.value,
  }))

  return {
    filters,
    planReadiness: computed(() =>
      unwrapData<
        NonNullable<BusinessConsoleMesFoundationReadinessEnvelope['data']>,
        BusinessConsoleMesFoundationReadinessEnvelope
      >(readinessQuery.data.value),
    ),
    planReadinessError: readinessQuery.error,
    planReadinessPending: readinessQuery.isLoading,
    planReadinessState: businessReadState(readinessQuery, () => readinessEnabled.value),
    refreshPlanReadiness: () =>
      readinessEnabled.value ? readinessQuery.refetch() : Promise.resolve(),
  }
}

export function useMesFoundationReadiness() {
  const filters = defaultFoundationFilters()

  const readinessQuery = useQuery(() =>
    withBusinessContextEnabled(
      getBusinessConsoleMesFoundationReadinessQueryOptions({
        query: toFoundationQuery(filters),
      }),
      filters,
    ),
  )

  return {
    filters,
    readiness: computed(() =>
      unwrapData<
        NonNullable<BusinessConsoleMesFoundationReadinessEnvelope['data']>,
        BusinessConsoleMesFoundationReadinessEnvelope
      >(readinessQuery.data.value),
    ),
    readinessError: readinessQuery.error,
    readinessPending: readinessQuery.isLoading,
    readinessState: businessReadState(readinessQuery, () => hasBusinessContext(filters)),
    refreshReadiness: () => refetchWithBusinessContext(filters, readinessQuery),
  }
}

export function useMesOverview() {
  const filters = defaultContext()

  const overviewQuery = useQuery(() =>
    withBusinessContextEnabled(
      getBusinessConsoleMesOverviewQueryOptions({
        query: toContextQuery(filters),
      }),
      filters,
    ),
  )

  const overview = computed(() =>
    unwrapData<
      NonNullable<BusinessConsoleMesOverviewEnvelope['data']>,
      BusinessConsoleMesOverviewEnvelope
    >(overviewQuery.data.value),
  )

  return {
    blockers: computed(() => overview.value?.blockers ?? []),
    counts: computed(() => overview.value?.counts ?? []),
    filters,
    overview,
    overviewError: overviewQuery.error,
    overviewPending: overviewQuery.isLoading,
    // 驾驶舱据此区分「取不到」与「真的没有阻塞」——没有它，读面 500 会被渲染成「现场无阻塞」。
    overviewState: businessReadState(overviewQuery, () => hasBusinessContext(filters)),
    pendingWork: computed(() => overview.value?.pendingWork ?? []),
    refreshOverview: () => refetchWithBusinessContext(filters, overviewQuery),
  }
}
