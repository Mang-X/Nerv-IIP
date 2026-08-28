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
    statuses.set(source, status)
  }

  const pending = computed(() => [...statuses.values()].some((status) => status === 'pending'))
  const guarded = computed(
    () => pending.value || [...statuses.values()].some((status) => blockingStatuses.has(status)),
  )

  return { set, pending, guarded }
}
