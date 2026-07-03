import { useBusinessContextStore } from '@/stores/businessContext'
import { watch } from 'vue'

export interface BusinessContextFields {
  organizationId: string
  environmentId: string
}

export function bindBusinessContext<T extends BusinessContextFields>(filters: T): T {
  const context = useBusinessContextStore()

  watch(
    () => [context.organizationId, context.environmentId] as const,
    ([organizationId, environmentId]) => {
      filters.organizationId = organizationId
      filters.environmentId = environmentId
    },
    { flush: 'sync', immediate: true },
  )

  return filters
}

export function hasBusinessContext(filters: BusinessContextFields) {
  return filters.organizationId.trim().length > 0 && filters.environmentId.trim().length > 0
}

export function withBusinessContextEnabled<TOptions extends object>(
  options: TOptions,
  filters: BusinessContextFields,
) {
  return {
    ...options,
    enabled: hasBusinessContext(filters),
  }
}

export function refetchWithBusinessContext<TQuery extends { refetch: () => unknown }>(
  filters: BusinessContextFields,
  query: TQuery,
) {
  return hasBusinessContext(filters) ? query.refetch() : Promise.resolve()
}
