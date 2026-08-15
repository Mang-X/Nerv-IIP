import {
  getBusinessConsoleWmsCountWorkScopesQueryOptions,
  getBusinessConsoleWmsReceiptWorkScopesQueryOptions,
  getBusinessConsoleWmsShipmentWorkScopesQueryOptions,
  type BusinessConsoleWmsWorkScopeCatalogEnvelope,
  type BusinessConsoleWmsWorkScopeCatalogItem,
} from '@nerv-iip/api-client'
import { formatWorkScopeKey, parseWorkScopeKey } from '@nerv-iip/business-core'
import { useQuery } from '@pinia/colada'
import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'

import { useAuthStore } from '@/stores/auth'

const SUPPORTED_SCOPE_KINDS = new Set(['self', 'work-pool', 'site'])

export type WmsWorkScopeCatalogKind = 'receipts' | 'shipments' | 'counts'

export interface WmsWorkScopeOption {
  label: string
  value: string
}

const selectedScopeKeys: Record<WmsWorkScopeCatalogKind, ReturnType<typeof shallowRef<string>>> = {
  receipts: shallowRef<string>(),
  shipments: shallowRef<string>(),
  counts: shallowRef<string>(),
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

export function useWmsWorkScope(
  catalog: WmsWorkScopeCatalogKind,
  consumerEnabled: MaybeRefOrGetter<boolean> = true,
) {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const hasTenant = computed(() => Boolean(organizationId.value && environmentId.value))
  const selectedScopeKey = selectedScopeKeys[catalog]

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
      enabled: hasTenant.value && toValue(consumerEnabled),
    }
  })

  const envelope = computed(
    () => catalogQuery.data.value as BusinessConsoleWmsWorkScopeCatalogEnvelope | undefined,
  )
  const catalogData = computed(() => (envelope.value?.success ? envelope.value.data : undefined))
  const principalId = computed(
    () =>
      catalogData.value?.actorPrincipalId?.trim() ??
      auth.principal?.principalId ??
      auth.sessionId ??
      'unrestored-session',
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
      value: formatWorkScopeKey(scope.scopeKind, scope.scopeId),
    })),
  )

  watch(
    scopeOptions,
    (options) => {
      if (options.some((option) => option.value === selectedScopeKey.value)) return
      selectedScopeKey.value =
        options.find((option) => parseWorkScopeKey(option.value)?.kind === 'self')?.value ??
        options[0]?.value
    },
    { immediate: true, flush: 'sync' },
  )

  const parsedSelection = computed(() => parseWorkScopeKey(selectedScopeKey.value))
  const hasSelection = computed(
    () =>
      hasTenant.value &&
      scopeOptions.value.some((option) => option.value === selectedScopeKey.value),
  )

  return {
    organizationId,
    environmentId,
    principalId,
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
    hasSuccessfulResponse: computed(
      () => envelope.value?.success === true && Boolean(envelope.value.data),
    ),
    hasFailedResponse: computed(
      () =>
        Boolean(catalogQuery.error.value) ||
        (!catalogQuery.isLoading.value &&
          envelope.value !== undefined &&
          !(envelope.value.success === true && envelope.value.data)),
    ),
    refresh: () => (hasTenant.value ? catalogQuery.refetch() : Promise.resolve()),
  }
}
