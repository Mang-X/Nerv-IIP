import { describeRequestError } from '@/api/request-timeout'
import { computed, ref } from 'vue'

/**
 * Result state machine for a write whose endpoint has NO server-side idempotency key
 * (Maintenance 报修/点检). Shared by `equipment/repair.vue` and `equipment/inspect.vue`
 * so the "when is a retry safe" rule lives in ONE place — the pages were duplicating it
 * line-for-line, which invites the two copies to drift.
 *
 * The core rule: only a DISPATCHED-but-unanswered failure (timeout / network drop) is
 * `indeterminate` — the write may already have taken effect server-side, so a blind
 * retry could duplicate it. Those steer the user to the list to verify. Everything else
 * (a definite server error, or an offline pre-check that never left the device) is safe
 * to retry.
 */
export interface NonIdempotentWriteResultOptions {
  /** Title + fallback copy for a determinate failure, e.g. '报修提交失败'. */
  failureTitle: string
  /** List the user is sent to verify against when the result is unknown, e.g. '近期维修工单'. */
  verifyListLabel: string
  /** Verb for the verify hint — '创建' (报修) / '记录' (点检). */
  verifyVerb: string
  /** Refresh that list so the user can check whether the write actually landed. */
  onVerify: () => void
}

export type WritePhase = 'form' | 'success' | 'error'

export function useNonIdempotentWriteResult(options: NonIdempotentWriteResultOptions) {
  const phase = ref<WritePhase>('form')
  const lastError = ref<unknown>(null)

  const errorInfo = computed(() => describeRequestError(lastError.value, options.failureTitle))

  const errorTitle = computed(() =>
    errorInfo.value.indeterminate ? '提交结果未知' : options.failureTitle,
  )

  const errorDescription = computed(() =>
    errorInfo.value.indeterminate
      ? `${errorInfo.value.message}。请勿重复提交，返回后在"${options.verifyListLabel}"中核实是否已${options.verifyVerb}。`
      : errorInfo.value.message,
  )

  /**
   * Whether the error Result should offer a retry. Indeterminate (dispatched, unknown)
   * → no retry (steer to verify). Determinate server error OR offline-not-dispatched →
   * safe to retry.
   */
  const canRetry = computed(() => !errorInfo.value.indeterminate)

  /** Run one submit; success → 'success', failure → 'error' keeping the raw error to classify. */
  async function run(submit: () => Promise<unknown>): Promise<boolean> {
    lastError.value = null
    try {
      await submit()
      phase.value = 'success'
      return true
    } catch (error) {
      lastError.value = error
      phase.value = 'error'
      return false
    }
  }

  /** Determinate/offline failure (no pending side effect) → safe to return to the form. */
  function retry() {
    lastError.value = null
    phase.value = 'form'
  }

  /** Indeterminate result → refresh the list and return to the form; NEVER auto-resubmit. */
  function verify() {
    options.onVerify()
    lastError.value = null
    phase.value = 'form'
  }

  /** Success continue / return → clear back to the form. */
  function reset() {
    lastError.value = null
    phase.value = 'form'
  }

  return {
    phase,
    lastError,
    errorInfo,
    errorTitle,
    errorDescription,
    canRetry,
    run,
    retry,
    verify,
    reset,
  }
}
