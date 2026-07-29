import { describeRequestError } from '@/api/request-timeout'
import { computed, ref, shallowRef } from 'vue'

export function useIdempotentWriteIntent<TPayload>(createKey: () => string) {
  const key = ref('')
  const attempted = ref(false)
  const locked = ref(false)
  const frozenPayload = shallowRef<TPayload>()

  function start() {
    key.value = createKey()
    attempted.value = false
    locked.value = false
    frozenPayload.value = undefined
  }

  function reset() {
    key.value = ''
    attempted.value = false
    locked.value = false
    frozenPayload.value = undefined
  }

  function inputChanged() {
    if (!attempted.value || locked.value) return
    key.value = createKey()
    attempted.value = false
    frozenPayload.value = undefined
  }

  function payload(factory: (idempotencyKey: string) => TPayload) {
    frozenPayload.value ??= factory(key.value)
    return frozenPayload.value
  }

  function markCommandAttempt() {
    attempted.value = true
  }

  function recordFailure(error: unknown, fallback: string) {
    const info = describeRequestError(error, fallback)
    locked.value = attempted.value && info.indeterminate
    return info
  }

  const attempt = computed<'initial' | 'retry'>(() => (attempted.value ? 'retry' : 'initial'))

  return {
    key,
    attempted,
    locked,
    attempt,
    start,
    reset,
    inputChanged,
    payload,
    markCommandAttempt,
    recordFailure,
  }
}
