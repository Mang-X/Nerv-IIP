import { useScreenData } from '@nerv-iip/ui'
import { computed, onScopeDispose, ref, shallowRef } from 'vue'
import { accessError, NoWorkScope } from '@/data/real/readError'
import type { ScopeOption } from '@/data/real/workScope'

export const scopeKeyOf = (scope: ScopeOption) => JSON.stringify([scope.kind, scope.id])

export function useScopedReading<T>(
  catalog: () => Promise<ScopeOption[]>,
  read: (scope: ScopeOption) => Promise<T>,
  intervalMs = 10000,
) {
  const options = shallowRef<ScopeOption[]>([])
  const selected = ref('')
  const data = shallowRef<T>()
  const error = shallowRef<unknown>()
  const loading = ref(false)
  const lastUpdated = ref<number>()
  let generation = 0

  function clearSample() {
    data.value = undefined
    lastUpdated.value = undefined
  }

  async function refresh() {
    if (loading.value) return
    const current = ++generation
    loading.value = true
    let catalogRead = false
    try {
      const scopes = await catalog()
      if (current !== generation) return
      catalogRead = true
      options.value = scopes
      const scope = selected.value ? scopes.find(s => scopeKeyOf(s) === selected.value) : scopes[0]
      if (!scope) throw new NoWorkScope('当前范围未获授权')
      selected.value = scopeKeyOf(scope)
      const result = await read(scope)
      if (current !== generation) return
      data.value = result
      error.value = undefined
      lastUpdated.value = Date.now()
    } catch (cause) {
      if (current !== generation) return
      error.value = cause
      if (accessError(cause)) {
        clearSample()
        if (!catalogRead) options.value = []
      }
    } finally {
      if (current === generation) loading.value = false
    }
  }

  function select(key: string) {
    if (!options.value.some(s => scopeKeyOf(s) === key)) return
    generation++
    clearSample()
    selected.value = key
    error.value = undefined
    loading.value = false
    void refresh()
  }

  useScreenData(refresh, { intervalMs })
  onScopeDispose(() => generation++)
  return { data, error, loading, options, selected, lastUpdated, select, refresh,
    scope: computed(() => options.value.find(s => scopeKeyOf(s) === selected.value)) }
}
