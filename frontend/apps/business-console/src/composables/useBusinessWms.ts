import {
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsOutboundOrdersQueryOptions,
  listBusinessConsoleWmsWcsTasksQueryOptions,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsInventoryContext,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsWcsTaskItem,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

// WMS facade 当前的 list 端点只接受组织/环境（入库另含库存维度过滤），
// 不返回 skip/take/total，因此前端按完整列表渲染，不做假分页（见 nav-map 与后端跟进 issue）。
const ORG = 'org-001'
const ENV = 'env-dev'

function optionalQuery<T>(key: string, value: T | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function unwrap<T>(envelope: { success?: boolean; data?: T | null } | undefined): T | undefined {
  return envelope?.success ? envelope.data ?? undefined : undefined
}

export interface WmsInboundFilters {
  organizationId: string
  environmentId: string
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

export function useWmsInboundOrders() {
  const filters = reactive<WmsInboundFilters>({ organizationId: ORG, environmentId: ENV })

  const query = useQuery(() =>
    listBusinessConsoleWmsInboundOrdersQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
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

  const data = computed(() => unwrap(query.data.value))

  return {
    filters,
    inboundOrders: computed<BusinessConsoleWmsInboundOrderItem[]>(() => data.value?.items ?? []),
    inventoryContext: computed<BusinessConsoleWmsInventoryContext | undefined>(
      () => data.value?.inventoryContext ?? undefined,
    ),
    inboundError: query.error,
    inboundPending: query.isLoading,
    refreshInbound: query.refetch,
  }
}

export function useWmsOutboundOrders() {
  const query = useQuery(() =>
    listBusinessConsoleWmsOutboundOrdersQueryOptions({
      query: { organizationId: ORG, environmentId: ENV },
    }),
  )

  return {
    outboundOrders: computed<BusinessConsoleWmsOutboundOrderItem[]>(
      () => unwrap(query.data.value)?.items ?? [],
    ),
    outboundError: query.error,
    outboundPending: query.isLoading,
    refreshOutbound: query.refetch,
  }
}

export interface WmsWcsFilters {
  organizationId: string
  environmentId: string
  externalTaskId?: string
  warehouseTaskId?: string
}

export function useWmsWcsTasks() {
  const filters = reactive<WmsWcsFilters>({ organizationId: ORG, environmentId: ENV })

  const query = useQuery(() =>
    listBusinessConsoleWmsWcsTasksQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('externalTaskId', filters.externalTaskId),
        ...optionalQuery('warehouseTaskId', filters.warehouseTaskId),
      },
    }),
  )

  return {
    filters,
    wcsTasks: computed<BusinessConsoleWmsWcsTaskItem[]>(() => unwrap(query.data.value)?.items ?? []),
    wcsError: query.error,
    wcsPending: query.isLoading,
    refreshWcs: query.refetch,
  }
}
