import {
  createBusinessConsoleErpAccountReceivableMutationOptions,
  createBusinessConsoleErpAccountPayableMutationOptions,
  createBusinessConsoleErpCostCandidateMutationOptions,
  getBusinessConsoleErpFinanceSummaryQueryOptions,
  listBusinessConsoleErpCostCandidatesQueryOptions,
  listBusinessConsoleErpJournalVouchersQueryOptions,
  listBusinessConsoleErpPayablesQueryOptions,
  listBusinessConsoleErpReceivablesQueryOptions,
  postBusinessConsoleErpJournalVoucherMutationOptions,
  type BusinessConsoleErpCostCandidateItem,
  type BusinessConsoleErpCostCandidateListEnvelope,
  type BusinessConsoleErpFinanceSummaryEnvelope,
  type BusinessConsoleErpFinanceSummaryResponse,
  type BusinessConsoleErpJournalVoucherItem,
  type BusinessConsoleErpJournalVoucherListEnvelope,
  type BusinessConsoleErpPayableItem,
  type BusinessConsoleErpPayableListEnvelope,
  type BusinessConsoleErpReceivableItem,
  type BusinessConsoleErpReceivableListEnvelope,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { hasBusinessContext, refetchWithBusinessContext } from '../shared/businessContextBinding'
import {
  makeIdempotencyKey,
  type BusinessErpListFilters,
  unwrapData,
  useErpDocumentList,
} from './erpDocumentList'

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
    ready: computed(() => hasBusinessContext(businessContext)),
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
  const list = useErpDocumentList<
    BusinessConsoleErpReceivableItem,
    BusinessConsoleErpReceivableListEnvelope
  >((query) => listBusinessConsoleErpReceivablesQueryOptions({ query }), initialFilters)

  const createMutation = useMutation({
    ...createBusinessConsoleErpAccountReceivableMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    createReceivable: (payload: {
      sourceDocumentNo: string
      customerCode: string
      amount: number
      currencyCode: string
      receivableNo?: string
    }) =>
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
  const list = useErpDocumentList<
    BusinessConsoleErpPayableItem,
    BusinessConsoleErpPayableListEnvelope
  >((query) => listBusinessConsoleErpPayablesQueryOptions({ query }), initialFilters)

  const createMutation = useMutation({
    ...createBusinessConsoleErpAccountPayableMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    createPayable: (payload: {
      sourceDocumentNo: string
      supplierCode: string
      amount: number
      currencyCode: string
      payableNo?: string
    }) =>
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
  const list = useErpDocumentList<
    BusinessConsoleErpJournalVoucherItem,
    BusinessConsoleErpJournalVoucherListEnvelope
  >((query) => listBusinessConsoleErpJournalVouchersQueryOptions({ query }), initialFilters)

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
      lines: { accountCode: string; debitAmount: number; creditAmount: number; memo: string }[]
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
  const list = useErpDocumentList<
    BusinessConsoleErpCostCandidateItem,
    BusinessConsoleErpCostCandidateListEnvelope
  >((query) => listBusinessConsoleErpCostCandidatesQueryOptions({ query }), initialFilters)

  const createMutation = useMutation({
    ...createBusinessConsoleErpCostCandidateMutationOptions(),
    onSuccess() {
      void list.refresh()
    },
  })

  return {
    ...list,
    createCostCandidate: (payload: {
      sourceType: string
      sourceDocumentNo: string
      amount: number
      currencyCode: string
      candidateNo?: string
    }) =>
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
