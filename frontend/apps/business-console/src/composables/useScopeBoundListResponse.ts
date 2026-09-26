import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'

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
