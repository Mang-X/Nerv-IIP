import { computed, toValue, type MaybeRefOrGetter } from 'vue'

export function usePermission(permissionCodes: MaybeRefOrGetter<readonly string[] | undefined>) {
  const isReady = computed(() => toValue(permissionCodes) !== undefined)
  const codes = computed(() => new Set(toValue(permissionCodes)))
  const can = (code: string) => codes.value.has(code)
  const canAny = (required: readonly string[]) => required.some(can)
  const canAll = (required: readonly string[]) => isReady.value && required.every(can)
  return { can, canAny, canAll, isReady }
}
