import {
  listBusinessConsoleErpPurchaseOrdersQueryOptions,
  type BusinessConsoleErpPurchaseOrderItem,
  type BusinessConsoleErpPurchaseOrderListEnvelope,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 10

export interface BusinessErpListFilters {
  status?: string
  keyword?: string
  skip: number
  take: number
}

function unwrapItems<T>(envelope: { success?: boolean; data?: { items?: T[] } | null } | undefined): T[] {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function unwrapTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined): number {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

export function useBusinessErp() {
  const businessContext = useBusinessContextStore()
  const filters = reactive<BusinessErpListFilters>({
    skip: 0,
    take: DEFAULT_TAKE,
  })
  const purchaseOrdersQuery = useQuery(() =>
    listBusinessConsoleErpPurchaseOrdersQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        status: filters.status,
        keyword: filters.keyword,
        skip: filters.skip,
        take: filters.take,
      },
    }),
  )

  return {
    filters,
    purchaseOrders: computed<BusinessConsoleErpPurchaseOrderItem[]>(() =>
      unwrapItems(
        purchaseOrdersQuery.data.value as BusinessConsoleErpPurchaseOrderListEnvelope | undefined,
      ),
    ),
    purchaseOrdersTotal: computed(() =>
      unwrapTotal(purchaseOrdersQuery.data.value as BusinessConsoleErpPurchaseOrderListEnvelope | undefined),
    ),
    purchaseOrdersError: purchaseOrdersQuery.error,
    purchaseOrdersPending: purchaseOrdersQuery.isLoading,
    refreshPurchaseOrders: purchaseOrdersQuery.refetch,
  }
}
