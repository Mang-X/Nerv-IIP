import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'

function isSuccessfulEnvelope(value: unknown) {
  return (
    typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    'success' in value &&
    value.success === true
  )
}

export function useScopeBoundListResponse<TResponse>(
  data: MaybeRefOrGetter<TResponse | undefined>,
  scopeKey: MaybeRefOrGetter<string>,
  enabled: MaybeRefOrGetter<boolean>,
) {
  const response = shallowRef<TResponse>()
  const responseScopeKey = shallowRef<string | null>(null)

  watch(
    [() => toValue(scopeKey), () => toValue(enabled)],
    ([nextScopeKey, ready], [previousScopeKey, wasReady]) => {
      if (!ready || !wasReady || nextScopeKey !== previousScopeKey) {
        response.value = undefined
        responseScopeKey.value = null
      }
    },
    { flush: 'sync' },
  )

  watch(
    () => toValue(data),
    (value) => {
      const currentScopeKey = toValue(scopeKey)
      if (value !== undefined && toValue(enabled) && currentScopeKey) {
        response.value = value
        responseScopeKey.value = currentScopeKey
      }
    },
    { immediate: true, flush: 'sync' },
  )

  return computed(() =>
    toValue(enabled) && responseScopeKey.value === toValue(scopeKey) ? response.value : undefined,
  )
}

export function useListResponseState(
  data: MaybeRefOrGetter<unknown>,
  enabled: MaybeRefOrGetter<boolean>,
  pending: MaybeRefOrGetter<boolean>,
) {
  const hasSuccessfulResponse = computed(
    () => toValue(enabled) && !toValue(pending) && isSuccessfulEnvelope(toValue(data)),
  )
  const hasFailedResponse = computed(() => {
    const value = toValue(data)
    return (
      toValue(enabled) && !toValue(pending) && value !== undefined && !isSuccessfulEnvelope(value)
    )
  })

  return { hasSuccessfulResponse, hasFailedResponse }
}

/** Records when a successful list response became available for the current scope. */
export function useListFreshness(
  data: MaybeRefOrGetter<unknown>,
  enabled: MaybeRefOrGetter<boolean>,
) {
  const lastUpdatedAt = shallowRef<string | null>(null)

  watch(
    [() => toValue(data), () => toValue(enabled)],
    ([value, ready]) => {
      if (!ready || value === undefined) {
        lastUpdatedAt.value = null
      } else if (isSuccessfulEnvelope(value)) {
        lastUpdatedAt.value = new Date().toISOString()
      }
    },
    { immediate: true, flush: 'sync' },
  )

  return computed(() => lastUpdatedAt.value)
}
