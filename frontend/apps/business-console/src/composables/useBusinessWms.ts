import {
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInboundOrderListEnvelope,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsOutboundOrderListEnvelope,
  type BusinessConsoleWmsWcsTaskItem,
  type BusinessConsoleWmsWcsTaskListEnvelope,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

export interface WmsListFilters {
  organizationId: string
  environmentId: string
  skip: number
  take: number
  status?: string
  keyword?: string
}

export interface WmsInboundListFilters extends WmsListFilters {
  skuCode?: string
  uomCode?: string
  siteCode?: string
  locationCode?: string
  lotNo?: string
  serialNo?: string
  qualityStatus?: string
  ownerType?: string
  ownerId?: string
}

export interface WmsWcsTaskListFilters extends WmsListFilters {
  externalTaskId?: string
  warehouseTaskId?: string
  failed?: boolean
}

function defaultFilters<T extends WmsListFilters>(initial: Partial<T> = {}): T {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  }) as T
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function baseQuery(filters: WmsListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    skip: filters.skip,
    take: filters.take,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
  }
}

function listItems<TItem>(envelope: { success?: boolean, data?: { items?: TItem[] } | null } | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function listTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

export function useWmsInboundOrders(initialFilters: Partial<WmsInboundListFilters> = {}) {
  const filters = defaultFilters<WmsInboundListFilters>(initialFilters)
  const inboundOrdersQuery = useQuery(() =>
    listBusinessConsoleWmsInboundOrdersQueryOptions({
      query: {
        ...baseQuery(filters),
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('uomCode', filters.uomCode),
        ...optionalQuery('siteCode', filters.siteCode),
        ...optionalQuery('locationCode', filters.locationCode),
        ...optionalQuery('lotNo', filters.lotNo),
        ...optionalQuery('serialNo', filters.serialNo),
        ...optionalQuery('qualityStatus', filters.qualityStatus),
        ...optionalQuery('ownerType', filters.ownerType),
        ...optionalQuery('ownerId', filters.ownerId),
      },
    }),
  )

  return {
    filters,
    inboundOrders: computed<BusinessConsoleWmsInboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsInboundOrderItem>(inboundOrdersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined),
    ),
    inboundOrdersError: inboundOrdersQuery.error,
    inboundOrdersPending: inboundOrdersQuery.isLoading,
    inboundOrdersTotal: computed(() => listTotal(inboundOrdersQuery.data.value as BusinessConsoleWmsInboundOrderListEnvelope | undefined)),
    refreshInboundOrders: inboundOrdersQuery.refetch,
  }
}

export function useWmsOutboundOrders(initialFilters: Partial<WmsListFilters> = {}) {
  const filters = defaultFilters<WmsListFilters>(initialFilters)
  const outboundOrdersQuery = useQuery(() =>
    listBusinessConsoleWmsOutboundOrdersQueryOptions({
      query: baseQuery(filters),
    }),
  )

  return {
    filters,
    outboundOrders: computed<BusinessConsoleWmsOutboundOrderItem[]>(() =>
      listItems<BusinessConsoleWmsOutboundOrderItem>(outboundOrdersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined),
    ),
    outboundOrdersError: outboundOrdersQuery.error,
    outboundOrdersPending: outboundOrdersQuery.isLoading,
    outboundOrdersTotal: computed(() => listTotal(outboundOrdersQuery.data.value as BusinessConsoleWmsOutboundOrderListEnvelope | undefined)),
    refreshOutboundOrders: outboundOrdersQuery.refetch,
  }
}

export function useWmsWcsTasks(initialFilters: Partial<WmsWcsTaskListFilters> = {}) {
  const filters = defaultFilters<WmsWcsTaskListFilters>(initialFilters)
  const wcsTasksQuery = useQuery(() =>
    listBusinessConsoleWmsWcsTasksQueryOptions({
      query: {
        ...baseQuery(filters),
        ...optionalQuery('externalTaskId', filters.externalTaskId),
        ...optionalQuery('warehouseTaskId', filters.warehouseTaskId),
        ...optionalQuery('failed', filters.failed),
      },
    }),
  )

  return {
    filters,
    wcsTasks: computed<BusinessConsoleWmsWcsTaskItem[]>(() =>
      listItems<BusinessConsoleWmsWcsTaskItem>(wcsTasksQuery.data.value as BusinessConsoleWmsWcsTaskListEnvelope | undefined),
    ),
    wcsTasksError: wcsTasksQuery.error,
    wcsTasksPending: wcsTasksQuery.isLoading,
    wcsTasksTotal: computed(() => listTotal(wcsTasksQuery.data.value as BusinessConsoleWmsWcsTaskListEnvelope | undefined)),
    refreshWcsTasks: wcsTasksQuery.refetch,
  }
}
