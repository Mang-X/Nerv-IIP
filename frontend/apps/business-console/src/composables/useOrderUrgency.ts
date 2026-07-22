import type { BusinessConsoleOrderUrgency } from '@nerv-iip/api-client'
import { listBusinessConsoleOrderUrgenciesQueryOptions } from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'

const urgencyRank: Record<string, number> = {
  critical: 5,
  urgent: 4,
  highrisk: 3,
  attention: 2,
  normal: 1,
}

function isMoreUrgent(
  candidate: BusinessConsoleOrderUrgency,
  current: BusinessConsoleOrderUrgency,
) {
  const candidateRank = urgencyRank[candidate.level?.toLowerCase() ?? ''] ?? 0
  const currentRank = urgencyRank[current.level?.toLowerCase() ?? ''] ?? 0
  if (candidateRank !== currentRank) return candidateRank > currentRank
  return (candidate.orderId ?? '').localeCompare(current.orderId ?? '') < 0
}

export function indexOrderUrgenciesByReference(items: BusinessConsoleOrderUrgency[]) {
  const map = new Map<string, BusinessConsoleOrderUrgency>()
  for (const item of items) {
    if (item.orderId) map.set(item.orderId, item)
    if (!item.businessReference) continue
    const current = map.get(item.businessReference)
    if (!current || isMoreUrgent(item, current)) map.set(item.businessReference, item)
  }
  return map
}

export function useOrderUrgencies(
  references: MaybeRefOrGetter<readonly (string | null | undefined)[]>,
) {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(
    reactive({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
    }),
  )
  const normalizedReferences = computed(() =>
    [
      ...new Set(
        toValue(references)
          .map((value) => value?.trim())
          .filter((value): value is string => Boolean(value)),
      ),
    ].sort(),
  )
  const query = useQuery(() => ({
    ...listBusinessConsoleOrderUrgenciesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        orderReferences: normalizedReferences.value.join(','),
      },
    }),
    enabled: hasBusinessContext(filters) && normalizedReferences.value.length > 0,
  }))
  const items = computed<BusinessConsoleOrderUrgency[]>(() => {
    const envelope = query.data.value as
      | { success?: boolean; data?: BusinessConsoleOrderUrgency[] | null }
      | undefined
    return envelope?.success ? (envelope.data ?? []) : []
  })
  const byReference = computed(() => {
    return indexOrderUrgenciesByReference(items.value)
  })

  return {
    byReference,
    error: query.error,
    items,
    pending: query.isLoading,
    refresh: query.refetch,
  }
}
