import {
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  runBusinessConsoleMesScheduleMutationOptions,
  type BusinessConsoleCreateRushWorkOrderRequest,
  type BusinessConsoleMesScheduleEnvelope,
  type BusinessConsoleMesScheduleResult,
  type BusinessConsoleMesWorkOrderItem,
  type BusinessConsoleMesWorkOrderListEnvelope,
  type BusinessConsoleRecordProductionReportRequest,
  type BusinessConsoleRunScheduleRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'

const DEFAULT_TAKE = 100

export interface MesListFilters {
  organizationId: string
  environmentId: string
  status?: string
  take: number
}

function defaultFilters(): MesListFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    take: DEFAULT_TAKE,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function toListQuery(filters: MesListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    ...optionalQuery('status', filters.status),
    take: filters.take,
  }
}

function listItems(envelope: BusinessConsoleMesWorkOrderListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function unwrapSchedule(
  envelope: BusinessConsoleMesScheduleEnvelope | undefined,
): BusinessConsoleMesScheduleResult | undefined {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
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

function invalidateWorkOrders(queryCache: ReturnType<typeof useQueryCache>) {
  return queryCache.invalidateQueries({
    predicate: isBusinessQuery('listBusinessConsoleMesWorkOrders'),
  })
}

export function useMesWorkOrders() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const workOrdersQuery = useQuery(() =>
    listBusinessConsoleMesWorkOrdersQueryOptions({
      query: toListQuery(filters),
    }),
  )

  const createRushMutation = useMutation({
    ...createBusinessConsoleMesRushWorkOrderMutationOptions(),
    onSuccess() {
      void invalidateWorkOrders(queryCache).catch(ignoreBackgroundError)
    },
  })

  const reportMutation = useMutation({
    ...recordBusinessConsoleMesProductionReportMutationOptions(),
    onSuccess() {
      void invalidateWorkOrders(queryCache).catch(ignoreBackgroundError)
    },
  })

  return {
    createRushWorkOrder: (body: BusinessConsoleCreateRushWorkOrderRequest) =>
      createRushMutation.mutateAsync({ body }),
    createRushWorkOrderError: createRushMutation.error,
    createRushWorkOrderPending: createRushMutation.isLoading,
    filters,
    recordProductionReport: (body: BusinessConsoleRecordProductionReportRequest) =>
      reportMutation.mutateAsync({ body }),
    recordProductionReportError: reportMutation.error,
    recordProductionReportPending: reportMutation.isLoading,
    refreshWorkOrders: workOrdersQuery.refetch,
    workOrders: computed<BusinessConsoleMesWorkOrderItem[]>(() =>
      listItems(workOrdersQuery.data.value),
    ),
    workOrdersError: workOrdersQuery.error,
    workOrdersPending: workOrdersQuery.isLoading,
  }
}

export function useMesSchedules() {
  const queryCache = useQueryCache()
  const lastScheduleEnvelope = shallowRef<BusinessConsoleMesScheduleEnvelope>()

  const runScheduleMutation = useMutation({
    ...runBusinessConsoleMesScheduleMutationOptions(),
    onSuccess(result) {
      lastScheduleEnvelope.value = result
      void invalidateWorkOrders(queryCache).catch(ignoreBackgroundError)
    },
  })

  return {
    lastSchedule: computed(() => unwrapSchedule(lastScheduleEnvelope.value)),
    runSchedule: (body: BusinessConsoleRunScheduleRequest) =>
      runScheduleMutation.mutateAsync({ body }),
    runScheduleError: runScheduleMutation.error,
    runSchedulePending: runScheduleMutation.isLoading,
  }
}
