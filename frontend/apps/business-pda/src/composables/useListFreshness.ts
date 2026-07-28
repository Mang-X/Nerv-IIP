import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'

/** Records when a successful list response became available in the current page. */
export function useListFreshness(
  data: MaybeRefOrGetter<unknown>,
  enabled: MaybeRefOrGetter<boolean>,
) {
  const lastUpdatedAt = shallowRef<string | null>(null)

  watch(
    [() => toValue(data), () => toValue(enabled)],
    ([value, ready]) => {
      const hasSuccessfulEnvelope =
        typeof value !== 'object' ||
        value === null ||
        !('success' in value) ||
        value.success === true
      if (ready && value !== undefined && hasSuccessfulEnvelope) {
        lastUpdatedAt.value = new Date().toISOString()
      }
    },
    { immediate: true },
  )

  return computed(() => lastUpdatedAt.value)
}
