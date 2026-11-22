import { useMesPrincipalWorkScope } from '../mes'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { getBusinessConsoleMesWorkOrderDetail } from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, type Ref } from 'vue'
import { hasBusinessContext, type BusinessContextFields } from '../shared/businessContextBinding'
import type { matchSuggestionWorkOrder } from './fulfillmentMatchers'
import { runNodeSource } from './fulfillmentErrors'

export function useFulfillmentWorkOrderQuery(
  ctx: BusinessContextFields,
  matchedWorkOrder: Readonly<Ref<ReturnType<typeof matchSuggestionWorkOrder>>>,
  canRead: (permission: string) => boolean,
) {
  // 按已接受建议携带的工单号读取详情，并核对来源建议后再补状态。
  const workOrderSuggestionId = computed(() => matchedWorkOrder.value?.suggestionId)
  const mesScope = useMesPrincipalWorkScope(
    ctx,
    P.mesWorkOrdersRead,
    () => canRead(P.mesWorkOrdersRead) && Boolean(matchedWorkOrder.value),
  )
  const workOrderDetailQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'work-order-detail',
      mesScope.principalIdentity.value,
      mesScope.selectedScope.value?.kind ?? '',
      mesScope.selectedScope.value?.id ?? '',
      ctx.organizationId,
      ctx.environmentId,
      matchedWorkOrder.value?.workOrderNo ?? '',
    ],
    enabled:
      mesScope.scopeReady.value &&
      canRead(P.mesWorkOrdersRead) &&
      hasBusinessContext(ctx) &&
      Boolean(matchedWorkOrder.value),
    query: () =>
      runNodeSource(async () => {
        const { data, error, response } = await getBusinessConsoleMesWorkOrderDetail({
          path: { workOrderId: matchedWorkOrder.value!.workOrderNo },
          query: {
            organizationId: ctx.organizationId,
            environmentId: ctx.environmentId,
            scopeKind: mesScope.selectedScope.value?.kind,
            scopeId: mesScope.selectedScope.value?.id,
          },
          throwOnError: false,
        })
        return { data: data?.data, error: data?.success === false ? data : error, response }
      }),
  }))
  const matchedPlanRow = computed(() => {
    const detail = workOrderDetailQuery.data.value
    const source = detail?.sourcePlanReference
    if (
      !canRead(P.mesWorkOrdersRead) ||
      !mesScope.scopeReady.value ||
      !detail ||
      detail.workOrderId !== matchedWorkOrder.value?.workOrderNo ||
      !source ||
      source.sourceDocumentId !== workOrderSuggestionId.value
    )
      return undefined
    return {
      status: detail.status,
      sourceDocumentId: source.sourceDocumentId,
      sourceDemandReference: source.sourceDemandReference,
    }
  })
  return { workOrderSuggestionId, mesScope, workOrderDetailQuery, matchedPlanRow }
}
