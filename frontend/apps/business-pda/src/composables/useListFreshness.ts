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
    // A query cache can publish the restored key's value immediately before the
    // reactive scope identity updates during browser back/forward. Post-flush binds
    // against the final identity without ever reusing unchanged stale-scope data.
    { immediate: true, flush: 'post' },
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
