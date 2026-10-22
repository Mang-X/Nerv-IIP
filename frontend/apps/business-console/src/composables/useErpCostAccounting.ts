import {
  configureBusinessConsoleErpWorkCenterCostRateMutationOptions,
  configureBusinessConsoleErpWorkCenterMachineOverheadRateMutationOptions,
  getBusinessConsoleErpWorkOrderCostVarianceQueryOptions,
  listBusinessConsoleErpWorkCenterCostRatesQueryOptions,
  listBusinessConsoleErpWorkCenterMachineOverheadRatesQueryOptions,
  listBusinessConsoleErpWorkCenterMachineOverheadReconciliationsQueryOptions,
  type BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'
import { hasBusinessContext, refetchWithBusinessContext } from './businessContextBinding'

// ERP 成本核算：工单成本差异、机器制造费用、工作中心人工费率与机器制造费用率。

export function useErpWorkOrderCostVariance() {
  const context = useBusinessContextStore()
  const workOrder = reactive({ id: '', page: 1, pageSize: 10 })
  const ready = computed(() => hasBusinessContext(context))
  const workOrderQuery = useQuery(() => ({
    ...getBusinessConsoleErpWorkOrderCostVarianceQueryOptions({
      path: { workOrderId: workOrder.id },
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        pageNumber: workOrder.page,
        pageSize: workOrder.pageSize,
      },
    }),
    enabled: ready.value && Boolean(workOrder.id),
  }))
  return {
    ready,
    workOrder,
    workOrderData: computed(() =>
      ready.value ? unwrapData(workOrderQuery.data.value) : undefined,
    ),
    workOrderPending: workOrderQuery.isLoading,
    workOrderError: workOrderQuery.error,
    refreshWorkOrder: () => refetchWithBusinessContext(context, workOrderQuery),
  }
}

export function useErpMachineOverhead() {
  const context = useBusinessContextStore()
  const order = useErpWorkOrderCostVariance()
  const { ready } = order
  const monthly = reactive({ period: '', workCenterId: '', page: 1, pageSize: 10 })
  const monthlyQuery = useQuery(() => ({
    ...listBusinessConsoleErpWorkCenterMachineOverheadReconciliationsQueryOptions({
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        accountingPeriodCode: monthly.period,
        workCenterId: monthly.workCenterId || undefined,
        pageNumber: monthly.page,
        pageSize: monthly.pageSize,
      },
    }),
    enabled: ready.value && Boolean(monthly.period),
  }))
  return {
    ...order,
    monthly,
    monthlyData: computed(() => (ready.value ? unwrapData(monthlyQuery.data.value) : undefined)),
    monthlyPending: monthlyQuery.isLoading,
    monthlyError: monthlyQuery.error,
    refreshMonthly: () => refetchWithBusinessContext(context, monthlyQuery),
  }
}

// 工作中心人工费率：按工作中心读修订历史，新增修订（只追加，不改不删）。
export function useErpWorkCenterCostRates() {
  const context = useBusinessContextStore()
  const workCenterId = shallowRef('')
  const ready = computed(() => hasBusinessContext(context))
  const ratesQuery = useQuery(() => ({
    ...listBusinessConsoleErpWorkCenterCostRatesQueryOptions({
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        workCenterId: workCenterId.value,
      },
    }),
    enabled: ready.value && Boolean(workCenterId.value),
  }))
  const configureMutation = useMutation({
    ...configureBusinessConsoleErpWorkCenterCostRateMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(context, ratesQuery)
    },
  })
  return {
    ready,
    workCenterId,
    rates: computed(() => (ready.value ? unwrapData(ratesQuery.data.value) : undefined)),
    pending: ratesQuery.isLoading,
    error: ratesQuery.error,
    refresh: () => refetchWithBusinessContext(context, ratesQuery),
    addRevision: (payload: {
      hourlyRate: number
      currencyCode: string
      effectiveFromUtc: string
      reason: string
    }) =>
      configureMutation.mutateAsync({
        body: {
          organizationId: context.organizationId,
          environmentId: context.environmentId,
          workCenterId: workCenterId.value,
          ...payload,
        },
      }),
    addRevisionPending: configureMutation.isLoading,
    addRevisionError: configureMutation.error,
  }
}

// 机器制造费用率：按工作中心 + 会计期间读修订，新增修订（只追加，不改不删）。
export function useErpWorkCenterMachineOverheadRates() {
  const context = useBusinessContextStore()
  const workCenterId = shallowRef('')
  const accountingPeriodCode = shallowRef('')
  const ready = computed(() => hasBusinessContext(context))
  const ratesQuery = useQuery(() => ({
    ...listBusinessConsoleErpWorkCenterMachineOverheadRatesQueryOptions({
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        workCenterId: workCenterId.value,
        accountingPeriodCode: accountingPeriodCode.value,
      },
    }),
    enabled: ready.value && Boolean(workCenterId.value) && Boolean(accountingPeriodCode.value),
  }))
  const configureMutation = useMutation({
    ...configureBusinessConsoleErpWorkCenterMachineOverheadRateMutationOptions(),
    onSuccess() {
      void refetchWithBusinessContext(context, ratesQuery)
    },
  })
  return {
    ready,
    workCenterId,
    accountingPeriodCode,
    rates: computed(() => (ready.value ? unwrapData(ratesQuery.data.value) : undefined)),
    pending: ratesQuery.isLoading,
    error: ratesQuery.error,
    refresh: () => refetchWithBusinessContext(context, ratesQuery),
    addRevision: (
      payload: Omit<
        BusinessConsoleConfigureErpWorkCenterMachineOverheadRateRequest,
        'organizationId' | 'environmentId' | 'workCenterId' | 'accountingPeriodCode'
      >,
    ) =>
      configureMutation.mutateAsync({
        body: {
          organizationId: context.organizationId,
          environmentId: context.environmentId,
          workCenterId: workCenterId.value,
          accountingPeriodCode: accountingPeriodCode.value,
          ...payload,
        },
      }),
    addRevisionPending: configureMutation.isLoading,
    addRevisionError: configureMutation.error,
  }
}

function unwrapData<T>(
  envelope: { success?: boolean; data?: T | null } | undefined,
): T | undefined {
  return envelope?.success ? (envelope.data ?? undefined) : undefined
}
