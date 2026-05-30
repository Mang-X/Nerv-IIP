import {
  listBusinessConsoleErpPurchaseOrdersQueryOptions,
  type BusinessConsoleErpPurchaseOrderItem,
  type BusinessConsoleErpPurchaseOrderListEnvelope,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'

function unwrapItems<T>(envelope: { success?: boolean; data?: { items?: T[] } | null } | undefined): T[] {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

export function useBusinessErp() {
  const businessContext = useBusinessContextStore()
  const purchaseOrdersQuery = useQuery(() =>
    listBusinessConsoleErpPurchaseOrdersQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
      },
    }),
  )

  return {
    purchaseOrders: computed<BusinessConsoleErpPurchaseOrderItem[]>(() =>
      unwrapItems(
        purchaseOrdersQuery.data.value as BusinessConsoleErpPurchaseOrderListEnvelope | undefined,
      ),
    ),
    purchaseOrdersError: purchaseOrdersQuery.error,
    purchaseOrdersPending: purchaseOrdersQuery.isLoading,
    refreshPurchaseOrders: purchaseOrdersQuery.refetch,
  }
}
