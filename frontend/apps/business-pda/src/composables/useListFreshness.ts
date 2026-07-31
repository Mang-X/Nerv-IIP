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

/**
 * Binds cached query data to the scope that produced it.
 *
 * Disabling or changing scope invalidates the public projection immediately.
 * A restored/new scope stays empty until the query publishes a new response,
 * while same-scope refreshes may keep rendering the previously bound response.
 *
 * Invalidation is identity-based, never transition-based: both watchers below
 * run with `flush: 'sync'`, and the data watcher can legitimately observe the
 * new scope's response *before* the invalidation watcher runs — that is exactly
 * what happens on browser back/forward, where the query key returns to an entry
 * the cache already holds. Discarding a projection that is already stamped with
 * the incoming scope key would freeze it forever, because a cache hit never
 * emits a second `data` change to republish from.
 */
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
      const alreadyBoundToCurrentScope =
        responseScopeKey.value !== null && responseScopeKey.value === nextScopeKey
      if (ready && alreadyBoundToCurrentScope) return
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

/**
 * Records when a successful list response became available for the current scope.
 *
 * An unavailable/disabled projection is an explicit scope unbind and clears the timestamp.
 * A failed response in the same scope keeps the previous successful-response time.
 */
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
