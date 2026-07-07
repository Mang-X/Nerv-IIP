import {
  approveBusinessConsoleErpQuotationMutationOptions,
  createBusinessConsoleErpSalesOrderMutationOptions,
  createBusinessConsoleErpQuotationMutationOptions,
  createBusinessConsoleErpAccountReceivableMutationOptions,
  createBusinessConsoleErpAccountPayableMutationOptions,
  createBusinessConsoleErpCostCandidateMutationOptions,
  createBusinessConsoleErpPurchaseOrderMutationOptions,
  createBusinessConsoleErpRequestForQuotationMutationOptions,
  convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrderMutationOptions,
  getBusinessConsoleErpFinanceSummaryQueryOptions,
  listBusinessConsoleErpCostCandidatesQueryOptions,
  listBusinessConsoleErpDeliveryOrdersQueryOptions,
  listBusinessConsoleErpJournalVouchersQueryOptions,
  listBusinessConsoleErpOpportunitiesQueryOptions,
  listBusinessConsoleErpPayablesQueryOptions,
  listBusinessConsoleErpPurchaseOrdersQueryOptions,
  listBusinessConsoleErpPurchaseRequisitionsQueryOptions,
  listBusinessConsoleErpQuotationsQueryOptions,
  listBusinessConsoleErpReceivablesQueryOptions,
  listBusinessConsoleErpRequestsForQuotationQueryOptions,
  listBusinessConsoleErpSalesOrdersQueryOptions,
  openBusinessConsoleErpOpportunityMutationOptions,
  postBusinessConsoleErpJournalVoucherMutationOptions,
  receiveBusinessConsoleErpSupplierQuotationMutationOptions,
  recordBusinessConsoleErpPurchaseReceiptMutationOptions,
  releaseBusinessConsoleErpDeliveryOrderMutationOptions,
  type BusinessConsoleErpCostCandidateItem,
  type BusinessConsoleErpCostCandidateListEnvelope,
  type BusinessConsoleErpDeliveryOrderItem,
  type BusinessConsoleErpDeliveryOrderListEnvelope,
  type BusinessConsoleErpFinanceSummaryEnvelope,
  type BusinessConsoleErpFinanceSummaryResponse,
  type BusinessConsoleErpJournalVoucherItem,
  type BusinessConsoleErpJournalVoucherListEnvelope,
  type BusinessConsoleErpOpportunityItem,
  type BusinessConsoleErpOpportunityListEnvelope,
  type BusinessConsoleErpPayableItem,
  type BusinessConsoleErpPayableListEnvelope,
  type BusinessConsoleErpPurchaseOrderItem,
  type BusinessConsoleErpPurchaseOrderListEnvelope,
  type BusinessConsoleErpPurchaseRequisitionItem,
  type BusinessConsoleErpPurchaseRequisitionListEnvelope,
  type BusinessConsoleErpQuotationItem,
  type BusinessConsoleErpQuotationListEnvelope,
  type BusinessConsoleErpReceivableItem,
  type BusinessConsoleErpReceivableListEnvelope,
  type BusinessConsoleErpRequestForQuotationItem,
  type BusinessConsoleErpRequestForQuotationListEnvelope,
  type BusinessConsoleErpSalesOrderItem,
  type BusinessConsoleErpSalesOrderListEnvelope,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { hasBusinessContext, refetchWithBusinessContext } from './businessContextBinding'

const DEFAULT_TAKE = 10

export interface BusinessErpListFilters {
  status?: string
  purchaseOrderStatus?: string
  purchaseRequisitionStatus?: string
  keyword?: string
  skip: number
  take: number
}

function defaultFilters(initial: Partial<BusinessErpListFilters> = {}): BusinessErpListFilters {
  return reactive({
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  })
}

interface ErpListQuery {
  organizationId: string
  environmentId: string
  status?: string
  keyword?: string
  skip: number
  take: number
}

function unwrapItems<T>(envelope: { success?: boolean, data?: { items?: T[] } | null } | undefined): T[] {
  return envelope?.success ? envelope.data?.items ?? [] : []
}

function unwrapTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined): number {
  return envelope?.success ? envelope.data?.total ?? 0 : 0
}

function unwrapData<T>(envelope: { success?: boolean, data?: T | null } | undefined): T | undefined {
  return envelope?.success ? envelope.data ?? undefined : undefined
}

// 写操作幂等键，避免重复提交；浏览器原生 UUID，测试环境亦可用。
function makeIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  return `idem-${Date.now()}-${Math.round(Math.random() * 1e9)}`
}

// 通用「单据列表」读面工厂：org/env + 服务端分页 skip/take + 状态/关键字过滤，无假分页。
function useErpDocumentList<TItem, TEnvelope extends { success?: boolean, data?: { items?: TItem[], total?: number } | null }>(
  buildOptions: (query: ErpListQuery) => unknown,
  initialFilters: Partial<BusinessErpListFilters> = {},
) {
  const businessContext = useBusinessContextStore()
  const filters = defaultFilters(initialFilters)
  const query = useQuery(() => ({
    // 各单据 query options 仅 data 泛型不同，统一经工厂收敛，故此处收窄类型。
    ...(buildOptions({
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      status: filters.status,
      keyword: filters.keyword,
      skip: filters.skip,
      take: filters.take,
    }) as object),
    enabled: hasBusinessContext(businessContext),
  }) as never)

  return {
    filters,
    organizationId: computed(() => businessContext.organizationId),
    environmentId: computed(() => businessContext.environmentId),
    items: computed<TItem[]>(() => unwrapItems(query.data.value as TEnvelope | undefined)),
    total: computed(() => unwrapTotal(query.data.value as TEnvelope | undefined)),
    error: query.error,
    pending: query.isLoading,
    refresh: () => refetchWithBusinessContext(businessContext, query),
  }
}

// 采购与供应（已有「采购与供应」页使用）。
export function useBusinessErp() {
  const businessContext = useBusinessContextStore()
  const filters = defaultFilters()
  const purchaseOrdersQuery = useQuery(() => ({
    ...listBusinessConsoleErpPurchaseOrdersQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        status: filters.purchaseOrderStatus,
        keyword: filters.keyword,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))
  const purchaseRequisitionsQuery = useQuery(() => ({
    ...listBusinessConsoleErpPurchaseRequisitionsQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        status: filters.purchaseRequisitionStatus,
        keyword: filters.keyword,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))

  return {
    filters,
    purchaseRequisitions: computed<BusinessConsoleErpPurchaseRequisitionItem[]>(() =>
      unwrapItems(purchaseRequisitionsQuery.data.value as BusinessConsoleErpPurchaseRequisitionListEnvelope | undefined),
    ),
    purchaseRequisitionsTotal: computed(() =>
      unwrapTotal(purchaseRequisitionsQuery.data.value as BusinessConsoleErpPurchaseRequisitionListEnvelope | undefined),
    ),
    purchaseRequisitionsError: purchaseRequisitionsQuery.error,
    purchaseRequisitionsPending: purchaseRequisitionsQuery.isLoading,
    refreshPurchaseRequisitions: () => refetchWithBusinessContext(businessContext, purchaseRequisitionsQuery),
    purchaseOrders: computed<BusinessConsoleErpPurchaseOrderItem[]>(() =>
      unwrapItems(purchaseOrdersQuery.data.value as BusinessConsoleErpPurchaseOrderListEnvelope | undefined),
    ),
    purchaseOrdersTotal: computed(() =>
      unwrapTotal(purchaseOrdersQuery.data.value as BusinessConsoleErpPurchaseOrderListEnvelope | undefined),
    ),
    purchaseOrdersError: purchaseOrdersQuery.error,
    purchaseOrdersPending: purchaseOrdersQuery.isLoading,
    refreshPurchaseOrders: () => refetchWithBusinessContext(businessContext, purchaseOrdersQuery),
    refreshProcurementDocuments: () => {
      void refetchWithBusinessContext(businessContext, purchaseRequisitionsQuery)
      void refetchWithBusinessContext(businessContext, purchaseOrdersQuery)
    },
  }
}

export function useErpPurchaseRequisitions(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpPurchaseRequisitionItem, BusinessConsoleErpPurchaseRequisitionListEnvelope>(
    (query) => listBusinessConsoleErpPurchaseRequisitionsQueryOptions({ query }),
    initialFilters,
  )
  const convertMutation = useMutation({
    ...convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrderMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    convertToPurchaseOrder: (purchaseRequisitionNos: string[]) =>
      convertMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          purchaseRequisitionNos,
          rfqSupplierCodes: [],
          currencyCode: 'CNY',
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    convertToPurchaseOrderPending: convertMutation.isLoading,
    convertToPurchaseOrderError: convertMutation.error,
  }
}

export function useErpRequestsForQuotation(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpRequestForQuotationItem, BusinessConsoleErpRequestForQuotationListEnvelope>(
    (query) => listBusinessConsoleErpRequestsForQuotationQueryOptions({ query }),
    initialFilters,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleErpRequestForQuotationMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    createRequestForQuotation: (payload: {
      supplierCodes: string[]
      rfqNo?: string
      lines: { lineNo: string, skuCode: string, uomCode: string, quantity: number, requiredDate: string }[]
    }) =>
      createMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          rfqNo: payload.rfqNo || null,
          supplierCodes: payload.supplierCodes,
          lines: payload.lines,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    createRequestForQuotationPending: createMutation.isLoading,
    createRequestForQuotationError: createMutation.error,
  }
}

export function useErpSupplierQuotations(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const rfqs = useErpRequestsForQuotation(initialFilters)
  const receiveMutation = useMutation({
    ...receiveBusinessConsoleErpSupplierQuotationMutationOptions(),
    onSuccess() {
      void rfqs.refresh()
    },
  })

  return {
    ...rfqs,
    receiveSupplierQuotation: (payload: {
      rfqNo: string
      supplierCode: string
      quotationNo?: string
      lines: { lineNo: string, skuCode: string, uomCode: string, quantity: number, unitPrice: number, promisedDate: string }[]
    }) =>
      receiveMutation.mutateAsync({
        body: {
          organizationId: rfqs.organizationId.value,
          environmentId: rfqs.environmentId.value,
          quotationNo: payload.quotationNo || null,
          rfqNo: payload.rfqNo,
          supplierCode: payload.supplierCode,
          lines: payload.lines,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    receiveSupplierQuotationPending: receiveMutation.isLoading,
    receiveSupplierQuotationError: receiveMutation.error,
  }
}

export function useErpPurchaseOrders(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpPurchaseOrderItem, BusinessConsoleErpPurchaseOrderListEnvelope>(
    (query) => listBusinessConsoleErpPurchaseOrdersQueryOptions({ query }),
    initialFilters,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleErpPurchaseOrderMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    createPurchaseOrder: (payload: {
      supplierCode: string
      siteCode: string
      purchaseOrderNo?: string
      lines: { lineNo: string, skuCode: string, uomCode: string, quantity: number, unitPrice: number, promisedDate: string }[]
    }) =>
      createMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          purchaseOrderNo: payload.purchaseOrderNo || null,
          supplierCode: payload.supplierCode,
          siteCode: payload.siteCode,
          lines: payload.lines,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    createPurchaseOrderPending: createMutation.isLoading,
    createPurchaseOrderError: createMutation.error,
  }
}

export function useErpPurchaseReceipts(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const purchaseOrders = useErpPurchaseOrders(initialFilters)
  const recordMutation = useMutation({
    ...recordBusinessConsoleErpPurchaseReceiptMutationOptions(),
    onSuccess() {
      void purchaseOrders.refresh()
    },
  })

  return {
    ...purchaseOrders,
    recordPurchaseReceipt: (payload: {
      purchaseOrderNo: string
      purchaseReceiptNo?: string
      lines: { purchaseOrderLineNo: string, receivedQuantity: number }[]
    }) =>
      recordMutation.mutateAsync({
        body: {
          organizationId: purchaseOrders.organizationId.value,
          environmentId: purchaseOrders.environmentId.value,
          purchaseReceiptNo: payload.purchaseReceiptNo || null,
          purchaseOrderNo: payload.purchaseOrderNo,
          lines: payload.lines,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    recordPurchaseReceiptPending: recordMutation.isLoading,
    recordPurchaseReceiptError: recordMutation.error,
  }
}

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
    refreshSalesOrders: () => refetchWithBusinessContext(businessContext, salesOrdersQuery),
    createSalesOrder: (payload: { quotationNo: string, salesOrderNo?: string }) =>
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

// 报价单：读面 + 审批（approve）+ 创建（多明细行）。
export function useErpQuotations(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpQuotationItem, BusinessConsoleErpQuotationListEnvelope>(
    (query) => listBusinessConsoleErpQuotationsQueryOptions({ query }),
    initialFilters,
  )

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
      customerCode: string
      expiresOn: string
      quotationNo?: string
      lines: { lineNo: string, skuCode: string, uomCode: string, quantity: number, unitPrice: number, requiredDate: string }[]
    }) =>
      createMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          quotationNo: payload.quotationNo || null,
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
  const list = useErpDocumentList<BusinessConsoleErpOpportunityItem, BusinessConsoleErpOpportunityListEnvelope>(
    (query) => listBusinessConsoleErpOpportunitiesQueryOptions({ query }),
    initialFilters,
  )

  const openMutation = useMutation({
    ...openBusinessConsoleErpOpportunityMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    openOpportunity: (payload: { customerCode: string, topic: string, opportunityNo?: string }) =>
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

// 发货单：读面（由销售订单履约生成）。
export function useErpDeliveryOrders(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpDeliveryOrderItem, BusinessConsoleErpDeliveryOrderListEnvelope>(
    (query) => listBusinessConsoleErpDeliveryOrdersQueryOptions({ query }),
    initialFilters,
  )
  const releaseMutation = useMutation({
    ...releaseBusinessConsoleErpDeliveryOrderMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    releaseDeliveryOrder: (deliveryOrderNo: string) =>
      releaseMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          deliveryOrderNo,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    releaseDeliveryOrderPending: releaseMutation.isLoading,
    releaseDeliveryOrderError: releaseMutation.error,
  }
}

// 财务汇总（语义 KPI 来源）：应收/应付未结、成本候选、已过账凭证。
export function useErpFinanceSummary() {
  const businessContext = useBusinessContextStore()
  const summaryQuery = useQuery(() => ({
    ...getBusinessConsoleErpFinanceSummaryQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))

  return {
    summary: computed<BusinessConsoleErpFinanceSummaryResponse | undefined>(() =>
      unwrapData(summaryQuery.data.value as BusinessConsoleErpFinanceSummaryEnvelope | undefined),
    ),
    summaryError: summaryQuery.error,
    summaryPending: summaryQuery.isLoading,
    refreshSummary: () => refetchWithBusinessContext(businessContext, summaryQuery),
  }
}

// 应收账款：读面 + 登记。
export function useErpReceivables(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpReceivableItem, BusinessConsoleErpReceivableListEnvelope>(
    (query) => listBusinessConsoleErpReceivablesQueryOptions({ query }),
    initialFilters,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleErpAccountReceivableMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    createReceivable: (payload: { sourceDocumentNo: string, customerCode: string, amount: number, currencyCode: string, receivableNo?: string }) =>
      createMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          receivableNo: payload.receivableNo || null,
          sourceDocumentNo: payload.sourceDocumentNo,
          customerCode: payload.customerCode,
          amount: payload.amount,
          currencyCode: payload.currencyCode,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    createReceivablePending: createMutation.isLoading,
    createReceivableError: createMutation.error,
  }
}

// 应付账款：读面 + 登记。
export function useErpPayables(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpPayableItem, BusinessConsoleErpPayableListEnvelope>(
    (query) => listBusinessConsoleErpPayablesQueryOptions({ query }),
    initialFilters,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleErpAccountPayableMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    createPayable: (payload: { sourceDocumentNo: string, supplierCode: string, amount: number, currencyCode: string, payableNo?: string }) =>
      createMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          payableNo: payload.payableNo || null,
          sourceDocumentNo: payload.sourceDocumentNo,
          supplierCode: payload.supplierCode,
          amount: payload.amount,
          currencyCode: payload.currencyCode,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    createPayablePending: createMutation.isLoading,
    createPayableError: createMutation.error,
  }
}

// 会计凭证：读面 + 过账（借贷分录）。
export function useErpJournalVouchers(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpJournalVoucherItem, BusinessConsoleErpJournalVoucherListEnvelope>(
    (query) => listBusinessConsoleErpJournalVouchersQueryOptions({ query }),
    initialFilters,
  )

  const postMutation = useMutation({
    ...postBusinessConsoleErpJournalVoucherMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    postVoucher: (payload: {
      postingDate: string
      voucherNo?: string
      lines: { accountCode: string, debitAmount: number, creditAmount: number, memo: string }[]
    }) =>
      postMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          voucherNo: payload.voucherNo || null,
          postingDate: payload.postingDate,
          lines: payload.lines,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    postVoucherPending: postMutation.isLoading,
    postVoucherError: postMutation.error,
  }
}

// 成本候选：读面 + 登记（待入账成本）。
export function useErpCostCandidates(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<BusinessConsoleErpCostCandidateItem, BusinessConsoleErpCostCandidateListEnvelope>(
    (query) => listBusinessConsoleErpCostCandidatesQueryOptions({ query }),
    initialFilters,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleErpCostCandidateMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    createCostCandidate: (payload: { sourceType: string, sourceDocumentNo: string, amount: number, currencyCode: string, candidateNo?: string }) =>
      createMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          candidateNo: payload.candidateNo || null,
          sourceType: payload.sourceType,
          sourceDocumentNo: payload.sourceDocumentNo,
          amount: payload.amount,
          currencyCode: payload.currencyCode,
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    createCostCandidatePending: createMutation.isLoading,
    createCostCandidateError: createMutation.error,
  }
}
