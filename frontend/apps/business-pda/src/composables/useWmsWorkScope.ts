import {
  getBusinessConsolePrincipalWorkContextQueryOptions,
  type BusinessConsolePrincipalWorkContextResponse,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, shallowRef, watch } from 'vue'

import { useAuthStore } from '@/stores/auth'

const SUPPORTED_SCOPE_KINDS = new Set(['self', 'team', 'site', 'organization'])

interface PrincipalWorkContextEnvelope {
  success?: boolean
  data?: BusinessConsolePrincipalWorkContextResponse | null
}

export interface WmsWorkScopeOption {
  label: string
  value: string
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

export function useWmsWorkScope(permissionCode: string) {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const principalId = computed(
    () => auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
  )
  const hasTenant = computed(() => Boolean(organizationId.value && environmentId.value))
  const selectedScopeKey = shallowRef<string>()

  const contextQuery = useQuery(() => ({
    ...getBusinessConsolePrincipalWorkContextQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        permissionCode,
      },
    }),
    enabled: hasTenant.value,
  }))

  const envelope = computed(
    () => contextQuery.data.value as PrincipalWorkContextEnvelope | undefined,
  )
  const authorizedScopes = computed(() =>
    envelope.value?.success
      ? (envelope.value.data?.authorizedScopes ?? []).filter(
          (scope) => Boolean(scope.kind && scope.id) && SUPPORTED_SCOPE_KINDS.has(scope.kind ?? ''),
        )
      : [],
  )
  const scopeOptions = computed<WmsWorkScopeOption[]>(() =>
    authorizedScopes.value.map((scope) => ({
      label:
        scope.kind === 'self'
          ? '我的任务'
          : scope.kind === 'organization'
            ? '全组织'
            : (scope.displayName ?? scope.id ?? ''),
      value: scopeValue(scope.kind!, scope.id!),
    })),
  )

  watch(
    scopeOptions,
    (options) => {
      if (options.some((option) => option.value === selectedScopeKey.value)) return
      const selfValue = scopeValue('self', principalId.value)
      selectedScopeKey.value =
        options.find((option) => option.value === selfValue)?.value ?? options[0]?.value
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
    principalId,
    hasTenant,
    hasSelection,
    scopeKey: selectedScopeKey,
    scopeKind: computed(() => (hasSelection.value ? parsedSelection.value?.kind : undefined)),
    scopeId: computed(() => (hasSelection.value ? parsedSelection.value?.id : undefined)),
    scopeOptions,
    pending: contextQuery.isLoading,
    error: contextQuery.error,
    refresh: contextQuery.refetch,
  }
}
