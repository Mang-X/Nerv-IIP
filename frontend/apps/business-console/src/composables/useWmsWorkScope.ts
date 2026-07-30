import {
  getBusinessConsoleWmsCountWorkScopesQueryOptions,
  getBusinessConsoleWmsReceiptWorkScopesQueryOptions,
  getBusinessConsoleWmsShipmentWorkScopesQueryOptions,
  type BusinessConsoleWmsWorkScopeCatalogEnvelope,
  type BusinessConsoleWmsWorkScopeCatalogItem,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, shallowRef, watch } from 'vue'

import { useBusinessContextStore } from '@/stores/businessContext'

const SUPPORTED_SCOPE_KINDS = new Set(['self', 'work-pool', 'site'])

export type WmsWorkScopeCatalogKind = 'receipts' | 'shipments' | 'counts'

export interface WmsWorkScopeOption {
  label: string
  value: string
}

export interface WmsWorkScopeFilters {
  scopeKind?: string
  scopeId?: string
  skip: number
}

function scopeValue(kind: string, id: string) {
  return `${kind}:${id}`
}

function parseScope(value: string | undefined) {
  if (!value) return undefined
  const separator = value.indexOf(':')
  if (separator <= 0 || separator === value.length - 1) return undefined
  return { kind: value.slice(0, separator), id: value.slice(separator + 1) }
}

function normalizeScope(
  item: BusinessConsoleWmsWorkScopeCatalogItem,
): Required<Pick<BusinessConsoleWmsWorkScopeCatalogItem, 'scopeKind' | 'scopeId'>> &
  BusinessConsoleWmsWorkScopeCatalogItem {
  return {
    ...item,
    scopeKind: item.scopeKind?.trim() ?? '',
    scopeId: item.scopeId?.trim() ?? '',
  }
}

export function useWmsWorkScope(catalog: WmsWorkScopeCatalogKind) {
  const context = useBusinessContextStore()
  const organizationId = computed(() => context.organizationId)
  const environmentId = computed(() => context.environmentId)
  const hasTenant = computed(() => Boolean(organizationId.value && environmentId.value))
  const selectedScopeKey = shallowRef<string>()

  const catalogQuery = useQuery(() => {
    const options = {
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
      },
    }
    const queryOptions =
      catalog === 'receipts'
        ? getBusinessConsoleWmsReceiptWorkScopesQueryOptions(options)
        : catalog === 'shipments'
          ? getBusinessConsoleWmsShipmentWorkScopesQueryOptions(options)
          : getBusinessConsoleWmsCountWorkScopesQueryOptions(options)

    return {
      ...queryOptions,
      enabled: hasTenant.value,
    }
  })

  const envelope = computed(
    () => catalogQuery.data.value as BusinessConsoleWmsWorkScopeCatalogEnvelope | undefined,
  )
  const catalogData = computed(() =>
    envelope.value?.success === true && envelope.value.data ? envelope.value.data : undefined,
  )
  const authorizedScopes = computed(() =>
    (catalogData.value?.items ?? [])
      .map(normalizeScope)
      .filter(
        (scope) =>
          Boolean(scope.scopeKind && scope.scopeId) && SUPPORTED_SCOPE_KINDS.has(scope.scopeKind),
      ),
  )
  const scopeOptions = computed<WmsWorkScopeOption[]>(() =>
    authorizedScopes.value.map((scope) => ({
      label: scope.scopeKind === 'self' ? '我的任务' : (scope.displayName?.trim() ?? scope.scopeId),
      value: scopeValue(scope.scopeKind, scope.scopeId),
    })),
  )

  watch(
    scopeOptions,
    (options) => {
      if (options.some((option) => option.value === selectedScopeKey.value)) return
      selectedScopeKey.value = options[0]?.value
    },
    { immediate: true, flush: 'sync' },
  )

  const parsedSelection = computed(() => parseScope(selectedScopeKey.value))
  const hasSelection = computed(
    () =>
      hasTenant.value &&
      scopeOptions.value.some((option) => option.value === selectedScopeKey.value),
  )

  return {
    organizationId,
    environmentId,
    principalId: computed(() => catalogData.value?.actorPrincipalId?.trim() ?? ''),
    hasTenant,
    hasSelection,
    scopeKey: selectedScopeKey,
    scopeKind: computed(() => (hasSelection.value ? parsedSelection.value?.kind : undefined)),
    scopeId: computed(() => (hasSelection.value ? parsedSelection.value?.id : undefined)),
    scopeOptions,
    selectedScopeLabel: computed(
      () =>
        scopeOptions.value.find((option) => option.value === selectedScopeKey.value)?.label ?? '',
    ),
    pending: catalogQuery.isLoading,
    error: catalogQuery.error,
    refresh: () => (hasTenant.value ? catalogQuery.refetch() : Promise.resolve()),
  }
}

export function bindWmsWorkScopeFilters<TFilters extends WmsWorkScopeFilters>(
  filters: TFilters,
  catalog: WmsWorkScopeCatalogKind,
) {
  const scope = useWmsWorkScope(catalog)

  watch(
    () => [scope.scopeKind.value, scope.scopeId.value] as const,
    ([scopeKind, scopeId]) => {
      filters.scopeKind = scopeKind
      filters.scopeId = scopeId
      filters.skip = 0
    },
    { immediate: true, flush: 'sync' },
  )

  return scope
}
