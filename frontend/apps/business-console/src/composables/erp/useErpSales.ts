import {
  approveBusinessConsoleErpQuotationMutationOptions,
  createBusinessConsoleErpSalesOrderMutationOptions,
  createBusinessConsoleErpQuotationMutationOptions,
  listBusinessConsoleErpDeliveryOrdersQueryOptions,
  listBusinessConsoleErpOpportunitiesQueryOptions,
  listBusinessConsoleErpQuotationsQueryOptions,
  listBusinessConsoleErpSalesOrdersQueryOptions,
  openBusinessConsoleErpOpportunityMutationOptions,
  releaseBusinessConsoleErpDeliveryOrderMutationOptions,
  releaseBusinessConsoleErpSalesOrderCreditHoldMutationOptions,
  type BusinessConsoleCreateErpSalesOrderResponse,
  type BusinessConsoleErpDeliveryOrderItem,
  type BusinessConsoleErpDeliveryOrderListEnvelope,
  type BusinessConsoleErpOpportunityItem,
  type BusinessConsoleErpOpportunityListEnvelope,
  type BusinessConsoleErpQuotationItem,
  type BusinessConsoleErpQuotationListEnvelope,
  type BusinessConsoleErpSalesOrderItem,
  type BusinessConsoleErpSalesOrderListEnvelope,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { hasBusinessContext, refetchWithBusinessContext } from '../shared/businessContextBinding'
import {
  defaultFilters,
  makeIdempotencyKey,
  type BusinessErpListFilters,
  unwrapItems,
  unwrapTotal,
  useErpDocumentList,
} from './erpDocumentList'

// 销售订单：读面 + 由已批准报价转换生成（quotationNo 必填）。
export function useErpSalesOrders(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const businessContext = useBusinessContextStore()
  const filters = defaultFilters(initialFilters)
  const salesOrdersQuery = useQuery(() => ({
    ...listBusinessConsoleErpSalesOrdersQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        status: filters.status,
        keyword: filters.keyword,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))

  const createMutation = useMutation({
    ...createBusinessConsoleErpSalesOrderMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(businessContext, salesOrdersQuery)
    },
  })

  // 信用冻结解冻复核：提交后走审批中心，审批通过订单才恢复 released，因此这里只刷新列表不改本地状态。
  const releaseCreditHoldMutation = useMutation({
    ...releaseBusinessConsoleErpSalesOrderCreditHoldMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(businessContext, salesOrdersQuery)
    },
  })

  // 释放发货：发货单是从销售订单释放出来的，入口只能挂在销售订单上。
  // 不传 lines 即整单释放，服务端按该订单当前全部未发行与剩余数量成单。
  const releaseDeliveryMutation = useMutation({
    ...releaseBusinessConsoleErpDeliveryOrderMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(businessContext, salesOrdersQuery)
    },
  })

  return {
    filters,
    ready: computed(() => hasBusinessContext(businessContext)),
    salesOrders: computed<BusinessConsoleErpSalesOrderItem[]>(() =>
      unwrapItems(
        salesOrdersQuery.data.value as BusinessConsoleErpSalesOrderListEnvelope | undefined,
      ),
    ),
    salesOrdersTotal: computed(() =>
      unwrapTotal(
        salesOrdersQuery.data.value as BusinessConsoleErpSalesOrderListEnvelope | undefined,
      ),
    ),
    salesOrdersError: salesOrdersQuery.error,
    salesOrdersPending: salesOrdersQuery.isLoading,
    refreshSalesOrders: () => refetchWithBusinessContext(businessContext, salesOrdersQuery),
    // 返回转换结果：reusedExistingOrder 为 true 表示报价已转出，本次幂等带回既有订单号而未新建。
    createSalesOrder: async (payload: {
      quotationNo: string
      siteCode: string
      salesOrderNo?: string
    }): Promise<BusinessConsoleCreateErpSalesOrderResponse | null> => {
      const envelope = await createMutation.mutateAsync({
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          quotationNo: payload.quotationNo,
          siteCode: payload.siteCode,
          salesOrderNo: payload.salesOrderNo || null,
          idempotencyKey: makeIdempotencyKey(),
        },
      })
      return envelope?.data ?? null
    },
    createSalesOrderPending: createMutation.isLoading,
    createSalesOrderError: createMutation.error,
    // salesOrderNo 走路由参数；请求体只带组织范围（StartedBy 由 Gateway 从 principal 注入）。
    releaseCreditHold: (payload: { salesOrderNo: string }) =>
      releaseCreditHoldMutation.mutateAsync({
        path: { salesOrderNo: payload.salesOrderNo },
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
        },
      }),
    releaseCreditHoldPending: releaseCreditHoldMutation.isLoading,
    releaseCreditHoldError: releaseCreditHoldMutation.error,
    releaseDeliveryOrder: (salesOrderNo: string) =>
      releaseDeliveryMutation.mutateAsync({
        body: {
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          deliveryOrderNo: null,
          salesOrderNo,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    releaseDeliveryOrderPending: releaseDeliveryMutation.isLoading,
    releaseDeliveryOrderError: releaseDeliveryMutation.error,
  }
}

// 报价单：读面 + 审批（approve）+ 创建（多明细行）。
export function useErpQuotations(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<
    BusinessConsoleErpQuotationItem,
    BusinessConsoleErpQuotationListEnvelope
  >((query) => listBusinessConsoleErpQuotationsQueryOptions({ query }), initialFilters)

  const approveMutation = useMutation({
    ...approveBusinessConsoleErpQuotationMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })
  const createMutation = useMutation({
    ...createBusinessConsoleErpQuotationMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    approveQuotation: (quotationNo: string) =>
      approveMutation.mutateAsync({
        path: { quotationNo },
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
        },
      }),
    approveQuotationPending: approveMutation.isLoading,
    approveQuotationError: approveMutation.error,
    createQuotation: (payload: {
      opportunityId?: string
      customerCode: string
      expiresOn: string
      quotationNo?: string
      lines: {
        lineNo: string
        skuCode: string
        uomCode: string
        quantity: number
        unitPrice: number
        requiredDate: string
      }[]
    }) =>
      createMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          quotationNo: payload.quotationNo || null,
          ...(payload.opportunityId && { opportunityId: payload.opportunityId }),
          customerCode: payload.customerCode,
          expiresOn: payload.expiresOn,
          lines: payload.lines,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    createQuotationPending: createMutation.isLoading,
    createQuotationError: createMutation.error,
  }
}

// 商机：读面 + 开立（open）。
export function useErpOpportunities(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<
    BusinessConsoleErpOpportunityItem,
    BusinessConsoleErpOpportunityListEnvelope
  >((query) => listBusinessConsoleErpOpportunitiesQueryOptions({ query }), initialFilters)

  const openMutation = useMutation({
    ...openBusinessConsoleErpOpportunityMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    openOpportunity: (payload: { customerCode: string; topic: string; opportunityNo?: string }) =>
      openMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          opportunityNo: payload.opportunityNo || null,
          customerCode: payload.customerCode,
          topic: payload.topic,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    openOpportunityPending: openMutation.isLoading,
    openOpportunityError: openMutation.error,
  }
}

// 发货单：只读读面。发货单由销售订单释放生成，列表里的行本身就是释放结果，
// 因此这里没有「释放发货」动作——那个入口在销售订单页（见 useErpSalesOrders）。
export function useErpDeliveryOrders(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<
    BusinessConsoleErpDeliveryOrderItem,
    BusinessConsoleErpDeliveryOrderListEnvelope
  >((query) => listBusinessConsoleErpDeliveryOrdersQueryOptions({ query }), initialFilters)

  return {
    ...list,
  }
}
