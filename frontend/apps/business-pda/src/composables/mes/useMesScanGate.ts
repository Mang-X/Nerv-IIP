import { computed, shallowReactive } from 'vue'

import type { MesScanPrevalidationStatus } from './useMesScanPrevalidation'

const blockingStatuses = new Set<MesScanPrevalidationStatus>([
  'ambiguous',
  'unknown',
  'unsupported',
  'forbidden',
  'rejected',
  'error',
])

export function useMesScanGate() {
  const statuses = shallowReactive(new Map<string, MesScanPrevalidationStatus>())

  function set(source: string, status: MesScanPrevalidationStatus) {
    if (status === 'idle') {
      statuses.delete(source)
      return
    }
    statuses.set(source, status)
  }

  function clear(source: string) {
    statuses.delete(source)
  }

  const pending = computed(() => [...statuses.values()].some((status) => status === 'pending'))
  const guarded = computed(
    () => pending.value || [...statuses.values()].some((status) => blockingStatuses.has(status)),
  )

  return { set, clear, pending, guarded }
}
