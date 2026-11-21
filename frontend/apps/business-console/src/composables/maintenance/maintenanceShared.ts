import { reactive } from 'vue'
import { bindBusinessContext } from '../shared/businessContextBinding'

const DEFAULT_TAKE = 100

export interface MaintenanceListFilters {
  organizationId: string
  environmentId: string
  skip: number
  take: number
  deviceAssetId?: string
}

export function defaultFilters(
  initial: Partial<MaintenanceListFilters> = {},
): MaintenanceListFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
      ...initial,
    }),
  )
}

export function optionalQuery<TKey extends string>(key: TKey, value: string) {
  const normalized = value.trim()
  return normalized.length > 0 ? { [key]: normalized } : {}
}

export function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

export function listTotal(
  envelope: { success?: boolean; data?: { total?: number } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

export function unwrapData<TData>(
  envelope: { success?: boolean; data?: TData | null } | undefined,
) {
  return envelope?.success ? (envelope.data ?? undefined) : undefined
}
