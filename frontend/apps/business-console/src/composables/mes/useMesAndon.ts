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

export type AndonAction = 'claim' | 'close'

export function useMesAndon() {
  const auth = useAuthStore()
  const filters = bindBusinessContext(
    reactive<ListBusinessConsoleMesAndonCallsData['query']>({
      organizationId: '',
      environmentId: '',
      queue: 'awaitingResponse',
      skip: 0,
      take: 10,
    }),
  )
  const pendingAction = ref<string | null>(null)
  const query = useQuery(() => {
    const request = { ...filters }
    return {
      key: ['mes-andon', auth.principal?.principalId ?? null, request],
      enabled: hasBusinessContext(filters),
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
  const refresh = () => query.refetch()
  function canAct(row: BusinessConsoleMesAndonCallResponse, action: AndonAction) {
    return Boolean(
      row.id &&
      hasBusinessContext(filters) &&
      auth.principal?.permissionCodes?.includes('business.mes.operations.manage') &&
      (action === 'claim'
        ? row.status === 'open'
        : row.status === 'claimed' && row.responderId === auth.principal.principalId),
    )
  }
  async function act(row: BusinessConsoleMesAndonCallResponse, action: AndonAction) {
    if (!canAct(row, action)) throw new Error('当前账号不能执行此操作。')
    pendingAction.value = row.id!
    try {
      const command =
        action === 'claim' ? claimBusinessConsoleMesAndonCall : closeBusinessConsoleMesAndonCall
      const { data } = await command({
        path: { id: row.id! },
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          idempotencyKey: crypto.randomUUID(),
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
    items: computed(() => query.data.value?.items ?? []),
    total: computed(() => query.data.value?.total ?? 0),
    ready: computed(() => !!query.data.value && !query.error.value),
    error: query.error,
    pending: query.isLoading,
    pendingAction,
    refresh,
    canAct,
    act,
  }
}
