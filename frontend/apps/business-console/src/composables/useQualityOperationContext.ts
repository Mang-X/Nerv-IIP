import { getBusinessConsoleMesWorkOrderDetailQueryOptions } from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { hasBusinessContext, type BusinessContextFields } from './businessContextBinding'
import { useMesPrincipalWorkScope } from './useBusinessMes'
import { useScopeBoundListResponse } from './useScopeBoundListResponse'
import { inlineErrorMessage } from '@/utils/notify'

// 工序检验的产品来自工单公开读面，沿用 MES 的主体与作业范围选择。
export function useQualityOperationContext(
  context: BusinessContextFields,
  workOrderId: () => string,
) {
  const scope = useMesPrincipalWorkScope(context, 'business.mes.work-orders.read')
  const enabled = computed(
    () => Boolean(workOrderId()) && hasBusinessContext(context) && scope.scopeReady.value,
  )
  const identity = computed(() =>
    [
      scope.principalIdentity.value,
      context.organizationId,
      context.environmentId,
      scope.selectedScope.value?.kind,
      scope.selectedScope.value?.id,
      workOrderId(),
    ].join(':'),
  )
  const query = useQuery(() => {
    const selected = scope.selectedScope.value
    const options = getBusinessConsoleMesWorkOrderDetailQueryOptions({
      path: { workOrderId: workOrderId() },
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        ...(selected ? { scopeKind: selected.kind, scopeId: selected.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal-scope:${identity.value}`],
      enabled: enabled.value,
    }
  })
  const response = useScopeBoundListResponse(() => query.data.value, identity, enabled)
  return {
    detail: computed(() => (response.value?.success ? response.value.data : undefined)),
    pending: computed(() => scope.scopePending.value || query.isLoading.value),
    errorMessage: computed(
      () =>
        scope.scopeMessage.value ||
        inlineErrorMessage(query.error.value) ||
        (response.value?.success === false ? response.value.message || '工单读取失败。' : ''),
    ),
    refresh: async () => {
      await scope.refreshScope()
      if (enabled.value) await query.refetch()
    },
  }
}
