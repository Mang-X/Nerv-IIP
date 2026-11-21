import {
  getBusinessConsoleWmsCountWorkScopesQueryOptions,
  getBusinessConsoleWmsReceiptWorkScopesQueryOptions,
  getBusinessConsoleWmsShipmentWorkScopesQueryOptions,
  type BusinessConsoleWmsWorkScopeCatalogEnvelope,
} from '@nerv-iip/api-client'
import {
  parseWorkScopeKey,
  toWmsWorkScopeOptions,
  type WmsWorkScopeCatalogKind,
} from '@nerv-iip/business-core'
import { useQuery } from '@pinia/colada'
import { computed, shallowReactive, toValue, watch, type MaybeRefOrGetter } from 'vue'

import { useAuthStore } from '@/stores/auth'

export type { WmsWorkScopeCatalogKind, WmsWorkScopeOption } from '@nerv-iip/business-core'

const selections = shallowReactive<
  Partial<Record<WmsWorkScopeCatalogKind, { sessionKey: string; scopeKey: string | undefined }>>
>({})

export function useWmsSessionKey() {
  const auth = useAuthStore()
  return computed(() =>
    JSON.stringify([
      auth.sessionId ?? '',
      auth.principal?.principalId ?? '',
      auth.principal?.organizationId ?? '',
      auth.principal?.environmentId ?? '',
    ]),
  )
}

export function useWmsWorkScope(
  catalog: WmsWorkScopeCatalogKind,
  consumerEnabled: MaybeRefOrGetter<boolean> = true,
) {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const hasTenant = computed(() => Boolean(organizationId.value && environmentId.value))
  const sessionKey = useWmsSessionKey()
  const selectedScopeKey = computed<string | undefined>({
    get: () => {
      const selection = selections[catalog]
      return selection?.sessionKey === sessionKey.value ? selection.scopeKey : undefined
    },
    set: (scopeKey) => {
      selections[catalog] = { sessionKey: sessionKey.value, scopeKey }
    },
  })

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
      key: [...queryOptions.key, sessionKey.value],
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
  const scopeOptions = computed(() => toWmsWorkScopeOptions(catalogData.value?.items ?? []))

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
    sessionKey,
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
