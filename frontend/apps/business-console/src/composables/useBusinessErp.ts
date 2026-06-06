import {
  createBusinessConsoleErpSalesOrderMutationOptions,
  listBusinessConsoleErpPurchaseOrdersQueryOptions,
  listBusinessConsoleErpSalesOrdersQueryOptions,
  type BusinessConsoleErpPurchaseOrderItem,
  type BusinessConsoleErpPurchaseOrderListEnvelope,
  type BusinessConsoleErpSalesOrderItem,
  type BusinessConsoleErpSalesOrderListEnvelope,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery } from '@pinia/colada'
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

// 写操作幂等键，避免重复提交；浏览器原生 UUID，测试环境亦可用。
function makeIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  return `idem-${Date.now()}-${Math.round(Math.random() * 1e9)}`
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

export function useErpSalesOrders() {
  const businessContext = useBusinessContextStore()
  const filters = reactive<BusinessErpListFilters>({
    skip: 0,
    take: DEFAULT_TAKE,
  })
  const salesOrdersQuery = useQuery(() =>
    listBusinessConsoleErpSalesOrdersQueryOptions({
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

  const createMutation = useMutation({
    ...createBusinessConsoleErpSalesOrderMutationOptions(),
    onSuccess() {
      void salesOrdersQuery.refetch()
    },
  })

  return {
    filters,
    salesOrders: computed<BusinessConsoleErpSalesOrderItem[]>(() =>
      unwrapItems(salesOrdersQuery.data.value as BusinessConsoleErpSalesOrderListEnvelope | undefined),
    ),
    salesOrdersTotal: computed(() =>
      unwrapTotal(salesOrdersQuery.data.value as BusinessConsoleErpSalesOrderListEnvelope | undefined),
    ),
    salesOrdersError: salesOrdersQuery.error,
    salesOrdersPending: salesOrdersQuery.isLoading,
    refreshSalesOrders: salesOrdersQuery.refetch,
    // 销售订单由已批准报价转换生成（quotationNo 必填，单号可留空由系统编号）。
    createSalesOrder: (payload: { quotationNo: string; salesOrderNo?: string }) =>
      createMutation.mutateAsync({
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          quotationNo: payload.quotationNo,
          salesOrderNo: payload.salesOrderNo || null,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    createSalesOrderPending: createMutation.isLoading,
    createSalesOrderError: createMutation.error,
  }
}
