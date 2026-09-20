import {
  claimBusinessConsoleMesAndonCall,
  closeBusinessConsoleMesAndonCall,
  listBusinessConsoleMesAndonCalls,
  type BusinessConsoleMesAndonCallResponse,
  type ListBusinessConsoleMesAndonCallsData,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive, ref } from 'vue'
import { bindBusinessContext, hasBusinessContext } from '@/composables/businessContextBinding'
import { useAuthStore } from '@/stores/auth'
import { errorStatusCode } from '@/utils/notify'
import { useMesPrincipalWorkScope } from '@/composables/useBusinessMes'

export type AndonAction = 'claim' | 'close'

export function useMesAndon(initial: Partial<ListBusinessConsoleMesAndonCallsData['query']> = {}) {
  const auth = useAuthStore()
  const filters = bindBusinessContext(
    reactive<ListBusinessConsoleMesAndonCallsData['query']>({
      organizationId: '',
      environmentId: '',
      queue: 'awaitingResponse',
      skip: 0,
      take: 10,
      ...initial,
    }),
  )
  const scope = useMesPrincipalWorkScope(filters, 'business.mes.operations.read')
  const writeScope = useMesPrincipalWorkScope(filters, 'business.mes.operations.manage')
  const pendingAction = ref<string | null>(null)
  const query = useQuery(() => {
    const selected = scope.selectedScope.value
    const request = { ...filters, scopeKind: selected?.kind, scopeId: selected?.id }
    return {
      key: ['mes-andon', auth.principal?.principalId ?? null, request],
      enabled: hasBusinessContext(filters) && scope.scopeReady.value,
      query: async () => {
        const { data } = await listBusinessConsoleMesAndonCalls({
          query: request,
          throwOnError: true,
        })
        if (!data?.success || !data.data) throw data ?? new Error('安灯队列读取失败。')
        return data.data
      },
    }
  })
  const refresh = async () => {
    await scope.refreshScope()
    if (scope.scopeReady.value) return query.refetch()
  }
  const actorRef = computed(() => {
    const principal = auth.principal
    return `${(principal?.principalType?.trim() || 'user').toLowerCase()}:${(principal?.principalId ?? principal?.loginName)?.trim()}`
  })
  function canAct(row: BusinessConsoleMesAndonCallResponse, action: AndonAction) {
    const selected = scope.selectedScope.value
    const writable = writeScope.selectedScope.value
    return Boolean(
      row.id &&
      hasBusinessContext(filters) &&
      auth.principal?.permissionCodes?.includes('business.mes.operations.manage') &&
      selected &&
      writable &&
      selected.kind === writable.kind &&
      selected.id === writable.id &&
      (action === 'claim'
        ? row.status === 'open'
        : row.status === 'claimed' && row.responderId === actorRef.value),
    )
  }
  async function act(row: BusinessConsoleMesAndonCallResponse, action: AndonAction) {
    if (!canAct(row, action)) throw new Error('当前账号不能执行此操作。')
    pendingAction.value = row.id!
    const selected = writeScope.requireSelectedScope()
    try {
      const command =
        action === 'claim' ? claimBusinessConsoleMesAndonCall : closeBusinessConsoleMesAndonCall
      const { data } = await command({
        path: { id: row.id! },
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          idempotencyKey: crypto.randomUUID(),
          scopeKind: selected.kind,
          scopeId: selected.id,
        },
        throwOnError: true,
      })
      if (!data?.success) throw data ?? new Error('安灯操作失败。')
      await refresh()
    } catch (error) {
      if (errorStatusCode(error) === 409) await refresh()
      throw error
    } finally {
      pendingAction.value = null
    }
  }
  return {
    filters,
    scope,
    items: computed(() => query.data.value?.items ?? []),
    total: computed(() => query.data.value?.total ?? 0),
    ready: computed(() => !!query.data.value && !query.error.value),
    error: query.error,
    pending: computed(() => query.isLoading.value || scope.scopePending.value),
    pendingAction,
    refresh,
    canAct,
    act,
  }
}
