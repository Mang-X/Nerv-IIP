import {
  getBusinessConsoleWorkbenchSummaryQueryOptions,
  type BusinessConsoleWorkbenchAlertItem,
  type BusinessConsoleWorkbenchKpiItem,
  type BusinessConsoleWorkbenchMessageItem,
  type BusinessConsoleWorkbenchSourceStatus,
  type BusinessConsoleWorkbenchSummaryEnvelope,
  type BusinessConsoleWorkbenchSummaryResponse,
  type BusinessConsoleWorkbenchTodoItem,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { hasBusinessContext, refetchWithBusinessContext } from './businessContextBinding'

const WORKBENCH_TAKE = 8

function unwrapData<TData, TEnvelope extends { success?: boolean; data?: TData | null }>(
  envelope: TEnvelope | undefined,
) {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function isAvailable(status: string | null | undefined) {
  return status?.trim().toLowerCase() === 'available'
}

export function useBusinessWorkbenchSummary() {
  const businessContext = useBusinessContextStore()
  const summaryQuery = useQuery(() => ({
    ...getBusinessConsoleWorkbenchSummaryQueryOptions({
      query: {
        organizationId: businessContext.organizationId,
        environmentId: businessContext.environmentId,
        take: WORKBENCH_TAKE,
      },
    }),
    enabled: hasBusinessContext(businessContext),
  }))

  const summary = computed(() =>
    unwrapData<BusinessConsoleWorkbenchSummaryResponse, BusinessConsoleWorkbenchSummaryEnvelope>(
      summaryQuery.data.value,
    ),
  )

  return {
    alertItems: computed<BusinessConsoleWorkbenchAlertItem[]>(() =>
      isAvailable(summary.value?.alerts?.status) ? (summary.value?.alerts?.items ?? []) : [],
    ),
    /**
     * 业务上下文（组织 / 环境）是否已就绪。未就绪时 `enabled: false`，pinia-colada
     * 的 `asyncStatus` 停在 `idle`（`isLoading === false`），页面若只看 pending/error
     * 就会把「压根没查」渲染成「查过了、是 0」——所以这一位必须显式暴露给页面。
     */
    contextReady: computed(() => hasBusinessContext(businessContext)),
    availableKpis: computed<BusinessConsoleWorkbenchKpiItem[]>(() =>
      (summary.value?.kpis ?? []).filter((kpi) => isAvailable(kpi.status)),
    ),
    messageItems: computed<BusinessConsoleWorkbenchMessageItem[]>(() =>
      isAvailable(summary.value?.messages?.status) ? (summary.value?.messages?.items ?? []) : [],
    ),
    refreshWorkbenchSummary: () => refetchWithBusinessContext(businessContext, summaryQuery),
    sourceStatuses: computed<BusinessConsoleWorkbenchSourceStatus[]>(
      () => summary.value?.sourceStatuses ?? [],
    ),
    summary,
    summaryError: summaryQuery.error,
    summaryPending: summaryQuery.isLoading,
    todoItems: computed<BusinessConsoleWorkbenchTodoItem[]>(() =>
      isAvailable(summary.value?.todos?.status) ? (summary.value?.todos?.items ?? []) : [],
    ),
  }
}
