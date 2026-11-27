import {
  listBusinessConsoleMesEngineeringChangeWorkOrderImpacts,
  type BusinessConsoleMesEngineeringChangeImpactsResponse,
} from '@nerv-iip/api-client'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { hasBusinessContext, type BusinessContextFields } from '../shared/businessContextBinding'
import { usePagedList } from '../shared/usePagedList'

interface ImpactFilters extends BusinessContextFields {
  changeNumber?: string
  workOrderId?: string
  scopeKind?: string
  scopeId?: string
}

export function useEngineeringWorkOrderImpacts(filters: () => ImpactFilters) {
  const auth = useAuthStore()
  const principal = computed(() => auth.principal?.principalId)
  const canRead = computed(() => {
    const codes = auth.principal?.permissionCodes ?? []
    return codes.includes(P.engineeringChangesRead) && codes.includes(P.mesWorkOrdersRead)
  })
  const query = computed(filters)
  const ready = computed(
    () =>
      hasBusinessContext(query.value) &&
      Boolean(query.value.changeNumber || query.value.workOrderId),
  )
  const offset = reactive({ skip: 0, take: 10 })
  const { page, pageSize } = usePagedList(offset, { resetOn: [query, principal] })
  const result = shallowRef<BusinessConsoleMesEngineeringChangeImpactsResponse>()
  const pending = ref(false)
  const failed = ref(false)
  const refreshVersion = ref(0)

  watch(
    [query, principal, canRead, () => offset.skip, () => offset.take, refreshVersion],
    async ([current, , allowed, skip, take], _, onCleanup) => {
      const controller = new AbortController()
      onCleanup(() => controller.abort())
      result.value = undefined
      failed.value = false
      pending.value = false
      if (!allowed || !ready.value) return

      pending.value = true
      try {
        const { data } = await listBusinessConsoleMesEngineeringChangeWorkOrderImpacts({
          query: { ...current, skip, take },
          signal: controller.signal,
          throwOnError: true,
        })
        if (controller.signal.aborted) return
        if (!data.success || !data.data) {
          failed.value = true
          return
        }
        result.value = data.data
        const lastPage = Math.max(1, Math.ceil(data.data.total / take))
        if (page.value > lastPage) page.value = lastPage
      } catch {
        if (!controller.signal.aborted) failed.value = true
      } finally {
        if (!controller.signal.aborted) pending.value = false
      }
    },
    { immediate: true },
  )

  return {
    canRead,
    ready,
    page,
    pageSize,
    result,
    pending,
    failed,
    refresh: () => {
      refreshVersion.value++
    },
  }
}
