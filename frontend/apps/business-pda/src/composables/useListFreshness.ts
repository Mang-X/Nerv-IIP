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

/** Records when a successful list response became available in the current page. */
export function useListFreshness(
  data: MaybeRefOrGetter<unknown>,
  enabled: MaybeRefOrGetter<boolean>,
) {
  const lastUpdatedAt = shallowRef<string | null>(null)

  watch(
    [() => toValue(data), () => toValue(enabled)],
    ([value, ready]) => {
      if (ready && isSuccessfulEnvelope(value)) {
        lastUpdatedAt.value = new Date().toISOString()
      }
    },
    { immediate: true },
  )

  return computed(() => lastUpdatedAt.value)
}
