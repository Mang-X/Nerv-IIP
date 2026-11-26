import {
  BusinessOperationUnconfirmedError,
  createBusinessConsoleErpPurchaseOrderMutationOptions,
  reviseBusinessConsoleErpPurchaseOrderMutationOptions,
  createBusinessConsoleErpRequestForQuotationMutationOptions,
  convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrderMutationOptions,
  listBusinessConsoleErpPurchaseOrdersQueryOptions,
  listBusinessConsoleErpPurchaseRequisitionsQueryOptions,
  listBusinessConsoleErpRequestsForQuotationQueryOptions,
  listBusinessConsoleErpSupplierQuotationsQueryOptions,
  receiveBusinessConsoleErpSupplierQuotationMutationOptions,
  recordBusinessConsoleErpPurchaseReceiptMutationOptions,
  type BusinessConsoleErpPurchaseOrderItem,
  type BusinessConsoleErpPurchaseOrderListEnvelope,
  type BusinessConsoleErpPurchaseRequisitionItem,
  type BusinessConsoleErpPurchaseRequisitionListEnvelope,
  type BusinessConsoleErpRequestForQuotationItem,
  type BusinessConsoleErpRequestForQuotationListEnvelope,
  type BusinessConsoleErpSupplierQuotationItem,
  type BusinessConsoleErpSupplierQuotationListEnvelope,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { hasBusinessContext, refetchWithBusinessContext } from '../shared/businessContextBinding'
import { assertEnvelopeSuccess } from '../shared/serviceEnvelope'
import {
  DEFAULT_TAKE,
  defaultFilters,
  makeIdempotencyKey,
  type BusinessErpListFilters,
  type ConvertPurchaseRequisitionOptions,
  unwrapItems,
  unwrapTotal,
  useErpDocumentList,
} from './erpDocumentList'

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
    ready: computed(() => hasBusinessContext(businessContext)),
    purchaseRequisitions: computed<BusinessConsoleErpPurchaseRequisitionItem[]>(() =>
      unwrapItems(
        purchaseRequisitionsQuery.data.value as
          | BusinessConsoleErpPurchaseRequisitionListEnvelope
          | undefined,
      ),
    ),
    purchaseRequisitionsTotal: computed(() =>
      unwrapTotal(
        purchaseRequisitionsQuery.data.value as
          | BusinessConsoleErpPurchaseRequisitionListEnvelope
          | undefined,
      ),
    ),
    purchaseRequisitionsError: purchaseRequisitionsQuery.error,
    purchaseRequisitionsPending: purchaseRequisitionsQuery.isLoading,
    refreshPurchaseRequisitions: () =>
      refetchWithBusinessContext(businessContext, purchaseRequisitionsQuery),
    purchaseOrders: computed<BusinessConsoleErpPurchaseOrderItem[]>(() =>
      unwrapItems(
        purchaseOrdersQuery.data.value as BusinessConsoleErpPurchaseOrderListEnvelope | undefined,
      ),
    ),
    purchaseOrdersTotal: computed(() =>
      unwrapTotal(
        purchaseOrdersQuery.data.value as BusinessConsoleErpPurchaseOrderListEnvelope | undefined,
      ),
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
  const list = useErpDocumentList<
    BusinessConsoleErpPurchaseRequisitionItem,
    BusinessConsoleErpPurchaseRequisitionListEnvelope
  >((query) => listBusinessConsoleErpPurchaseRequisitionsQueryOptions({ query }), initialFilters)
  const convertMutation = useMutation({
    ...convertBusinessConsoleErpPurchaseRequisitionsToPurchaseOrderMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    convertToPurchaseOrder: (
      purchaseRequisitionNos: string[],
      options: ConvertPurchaseRequisitionOptions = {},
    ) =>
      convertMutation.mutateAsync({
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          purchaseRequisitionNos,
          rfqSupplierCodes: options.rfqSupplierCodes ?? [],
          currencyCode: 'CNY',
          idempotencyKey: makeIdempotencyKey(),
        },
      }),
    convertToPurchaseOrderPending: convertMutation.isLoading,
    convertToPurchaseOrderError: convertMutation.error,
  }
}

export function useErpRequestsForQuotation(initialFilters: Partial<BusinessErpListFilters> = {}) {
  const list = useErpDocumentList<
    BusinessConsoleErpRequestForQuotationItem,
    BusinessConsoleErpRequestForQuotationListEnvelope
  >((query) => listBusinessConsoleErpRequestsForQuotationQueryOptions({ query }), initialFilters)

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
      lines: {
        lineNo: string
        skuCode: string
        uomCode: string
        quantity: number
        requiredDate: string
      }[]
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

export interface BusinessErpSupplierQuotationFilters {
  rfqNo?: string
  supplierCode?: string
  keyword?: string
  skip: number
  take: number
}

/**
 * 供应商报价读面。
 *
 * 这里查的是**真正的报价单**（`erp.supplier_quotations`），不是询价单列表——
 * 早先本 composable 直接复用 `useErpRequestsForQuotation`，导致「提交一条报价、刷新页面报价就消失」。
 * 过滤维度按服务端读面：询价单 / 供应商 / 关键字；报价聚合上没有状态字段，故不提供 status。
 */
export function useErpSupplierQuotations(
  initialFilters: Partial<BusinessErpSupplierQuotationFilters> = {},
) {
  const businessContext = useBusinessContextStore()
  const filters = reactive<BusinessErpSupplierQuotationFilters>({
    skip: 0,
    take: DEFAULT_TAKE,
    ...initialFilters,
  })
  const query = useQuery(() => ({
    ...listBusinessConsoleErpSupplierQuotationsQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        rfqNo: filters.rfqNo,
        supplierCode: filters.supplierCode,
        keyword: filters.keyword,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))
  const refresh = () => refetchWithBusinessContext(businessContext, query)
  const receiveMutation = useMutation({
    ...receiveBusinessConsoleErpSupplierQuotationMutationOptions(),
    onSuccess() {
      void refresh()
    },
  })
  const organizationId = computed(() => businessContext.organizationId)
  const environmentId = computed(() => businessContext.environmentId)

  return {
    filters,
    organizationId,
    environmentId,
    ready: computed(() => hasBusinessContext(businessContext)),
    items: computed<BusinessConsoleErpSupplierQuotationItem[]>(() =>
      unwrapItems(query.data.value as BusinessConsoleErpSupplierQuotationListEnvelope | undefined),
    ),
    total: computed(() =>
      unwrapTotal(query.data.value as BusinessConsoleErpSupplierQuotationListEnvelope | undefined),
    ),
    error: query.error,
    pending: query.isLoading,
    refresh,
    receiveSupplierQuotation: (payload: {
      rfqNo: string
      supplierCode: string
      quotationNo?: string
      lines: {
        lineNo: string
        skuCode: string
        uomCode: string
        quantity: number
        unitPrice: number
        promisedDate: string
      }[]
    }) =>
      receiveMutation.mutateAsync({
        body: {
          organizationId: organizationId.value,
          environmentId: environmentId.value,
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
  const list = useErpDocumentList<
    BusinessConsoleErpPurchaseOrderItem,
    BusinessConsoleErpPurchaseOrderListEnvelope
  >((query) => listBusinessConsoleErpPurchaseOrdersQueryOptions({ query }), initialFilters)

  const createMutation = useMutation({
    ...createBusinessConsoleErpPurchaseOrderMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  const reviseMutation = useMutation(reviseBusinessConsoleErpPurchaseOrderMutationOptions())

  return {
    ...list,
    revisePurchaseOrder: async (
      purchaseOrderNo: string,
      lines: { lineNo: string; orderedQuantity: number; unitPrice: number; promisedDate: string }[],
    ) => {
      const result = await reviseMutation.mutateAsync({
        path: { purchaseOrderNo },
        body: {
          organizationId: list.organizationId.value,
          environmentId: list.environmentId.value,
          lines,
        },
      })
      assertEnvelopeSuccess(result, '采购单重提失败。')
      void list.refresh()
      return result.data
    },
    revisePurchaseOrderPending: reviseMutation.isLoading,
    createPurchaseOrder: (payload: {
      supplierCode: string
      siteCode: string
      purchaseOrderNo?: string
      lines: {
        lineNo: string
        skuCode: string
        uomCode: string
        quantity: number
        unitPrice: number
        promisedDate: string
      }[]
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
  const auth = useAuthStore()
  const recordMutation = useMutation(recordBusinessConsoleErpPurchaseReceiptMutationOptions())

  return {
    ...purchaseOrders,
    recordPurchaseReceipt: async (payload: {
      purchaseOrderNo: string
      // qualityStatus 是 ERP 收货命令的必填业务决策点（#1345），缺失会被后端 400 拒绝。
      // locationCode 是直接过账的收货库位（#3900），ERP 缺库位即拒绝，不再回落到工厂编码。
      // 我方收货单号由 ERP 编码规则生成，控制台不提供手填通道。
      lines: {
        purchaseOrderLineNo: string
        receivedQuantity: number
        qualityStatus: string
        locationCode: string
      }[]
    }) => {
      const organizationId = purchaseOrders.organizationId.value
      const environmentId = purchaseOrders.environmentId.value
      const scope = {
        principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
        organizationId,
        environmentId,
        operationType: 'erp.purchase-receipt.record',
        payloadFingerprint: JSON.stringify(payload),
      }
      const intent = acquirePendingBusinessIntent(scope, makeIdempotencyKey)
      const recorded = await completePendingBusinessIntent(scope, async () => {
        const envelope = await recordMutation.mutateAsync({
          body: {
            organizationId,
            environmentId,
            purchaseOrderNo: payload.purchaseOrderNo,
            lines: payload.lines,
            idempotencyKey: intent.idempotencyKey,
          },
        })
        if (envelope?.success === false) {
          assertEnvelopeSuccess(envelope, '确认收货失败，请检查收货数据。')
        }
        if (
          !envelope?.success ||
          !envelope.data?.purchaseReceiptNo ||
          !envelope.data.purchaseReceiptId
        ) {
          throw new BusinessOperationUnconfirmedError('暂未确认收货单号。')
        }
        return envelope.data
      })
      void purchaseOrders.refresh()
      return recorded
    },
    recordPurchaseReceiptPending: recordMutation.isLoading,
  }
}
