import { useMesPrincipalWorkScope } from '../useBusinessMes'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import {
  getBusinessConsoleMesWorkOrderDetail,
  listBusinessConsoleErpPurchaseRequisitions,
  type BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, type Ref } from 'vue'
import { hasBusinessContext } from '../businessContextBinding'
import { assertEnvelopeSuccess } from '../serviceEnvelope'

interface PlanningScope {
  organizationId: string
  environmentId: string
}

export function usePlanningDownstreamStatuses(
  filters: PlanningScope,
  suggestionFilters: PlanningScope,
  suggestions: Readonly<Ref<readonly BusinessConsolePlanningSuggestionItem[]>>,
  downstreamPage: (() => readonly BusinessConsolePlanningSuggestionItem[]) | undefined,
  principalId: () => string,
  canRead: (permission: string) => boolean,
) {
  const downstreamSuggestions = computed(() =>
    (downstreamPage?.() ?? suggestions.value.slice(0, 10)).filter(
      (item) =>
        item.suggestionId &&
        item.downstreamDocumentId &&
        (item.suggestionType === 'planned-work-order' ||
          item.suggestionType === 'planned-purchase'),
    ),
  )
  const workOrderScope = useMesPrincipalWorkScope(
    filters,
    P.mesWorkOrdersRead,
    () =>
      canRead(P.mesWorkOrdersRead) &&
      downstreamSuggestions.value.some((item) => item.suggestionType === 'planned-work-order'),
  )
  const downstreamStatusesQuery = useQuery(() => ({
    key: [
      'planning',
      'downstream-statuses',
      principalId(),
      canRead(P.erpProcurementRead),
      canRead(P.mesWorkOrdersRead),
      workOrderScope.selectedScope.value?.kind ?? '',
      workOrderScope.selectedScope.value?.id ?? '',
      workOrderScope.scopePending.value,
      suggestionFilters.organizationId,
      suggestionFilters.environmentId,
      downstreamSuggestions.value
        .map((item) => `${item.suggestionId}:${item.downstreamDocumentId}`)
        .join('|'),
    ],
    enabled:
      canRead(P.planningMrpRead) &&
      hasBusinessContext(suggestionFilters) &&
      downstreamSuggestions.value.length > 0,
    query: async () => {
      const scope = {
        organizationId: suggestionFilters.organizationId,
        environmentId: suggestionFilters.environmentId,
      }
      const statuses: Record<string, string> = {}
      await Promise.all(
        downstreamSuggestions.value.map(async (item) => {
          try {
            const permission =
              item.suggestionType === 'planned-work-order'
                ? P.mesWorkOrdersRead
                : P.erpProcurementRead
            if (!canRead(permission)) {
              statuses[item.suggestionId!] = 'restricted'
              return
            }
            if (item.suggestionType === 'planned-work-order') {
              const selected = workOrderScope.selectedScope.value
              if (!selected) {
                statuses[item.suggestionId!] = workOrderScope.scopePending.value
                  ? 'loading'
                  : 'restricted'
                return
              }
              const { data } = await getBusinessConsoleMesWorkOrderDetail({
                path: { workOrderId: item.downstreamDocumentId! },
                query: {
                  organizationId: suggestionFilters.organizationId,
                  environmentId: suggestionFilters.environmentId,
                  scopeKind: selected.kind,
                  scopeId: selected.id,
                },
                throwOnError: true,
              })
              statuses[item.suggestionId!] =
                assertEnvelopeSuccess(data!, '读取工单状态失败。').data?.status ?? 'unknown'
            } else {
              // 采购侧关键字同时命中建议、单号、工厂和 SKU；回执同时校验来源和下游标识。
              let status = 'unknown'
              for (let skip = 0; skip < 400; skip += 100) {
                const { data } = await listBusinessConsoleErpPurchaseRequisitions({
                  query: { ...scope, keyword: item.suggestionId!, skip, take: 100 },
                  throwOnError: true,
                })
                const page = assertEnvelopeSuccess(data!, '读取采购申请状态失败。').data
                const rows = page?.items ?? []
                const match = rows.find(
                  (row) =>
                    row.suggestionId === item.suggestionId &&
                    (row.requisitionNo === item.downstreamDocumentId ||
                      row.purchaseRequisitionId === item.downstreamDocumentId),
                )
                if (match || rows.length < 100 || skip + rows.length >= (page?.total ?? Infinity)) {
                  status = match?.status ?? 'unknown'
                  break
                }
                status = 'partial'
              }
              statuses[item.suggestionId!] = status
            }
          } catch (error) {
            statuses[item.suggestionId!] =
              (error as { status?: number } | null)?.status === 403 ? 'restricted' : 'unavailable'
          }
        }),
      )
      return statuses
    },
  }))

  function refreshDownstreamStatuses() {
    const queries: Promise<unknown>[] = []
    if (downstreamSuggestions.value.length) {
      if (workOrderScope.scopeError.value && canRead(P.mesWorkOrdersRead))
        queries.push(workOrderScope.refreshScope())
      queries.push(downstreamStatusesQuery.refetch())
    }
    return queries
  }

  return { downstreamStatusesQuery, refreshDownstreamStatuses }
}
