/**
 * PDA global request timeout + offline fallback — the single choke point for every
 * facade call.
 *
 * The generated api-client (`@nerv-iip/api-client`) routes ALL requests through the
 * `fetch` it is handed via `configureApiClient({ fetch })`. Wrapping that one fetch
 * here gives every PDA facade call (platform + business-console clients, reads and
 * writes alike) a 30s ceiling and an offline pre-check — without each page or
 * composable re-implementing an AbortController. Do NOT add per-page timeout logic;
 * this is the one place it lives.
 *
 * Failure surfaces as a typed `Error` whose `.message` is the user-facing copy. Every
 * PDA page already renders errors as `e instanceof Error ? e.message : '<fallback>'`
 * (list inline messages + `NvMobileResult` error states), so timeouts and offline
 * states show the right copy and their existing retry buttons "just work". Retries are
 * safe because pages reuse a stable idempotency key per action — this layer never
 * touches it.
 *
 * Business errors thrown by the gateway are plain objects/strings (not `Error`), so
 * they keep falling through to each page's own fallback copy — distinct from the
 * timeout/offline messages defined here.
 */

/** Hard ceiling for any single facade request. 车间 WiFi hangs must not block forever. */
export const REQUEST_TIMEOUT_MS = 30_000

/** Thrown when a request exceeds {@link REQUEST_TIMEOUT_MS}. */
export class RequestTimeoutError extends Error {
  constructor(message = '网络超时，请检查连接后重试') {
    super(message)
    this.name = 'RequestTimeoutError'
  }
}

/** Thrown before dispatch when the device reports itself offline (`navigator.onLine`). */
export class OfflineError extends Error {
  constructor(message = '当前离线，请检查网络连接后重试') {
    super(message)
    this.name = 'OfflineError'
  }
}

/** Default offline probe — guarded so it is inert in non-browser (SSR/test) contexts. */
function defaultIsOffline(): boolean {
  return typeof navigator !== 'undefined' && navigator.onLine === false
}

function isAbortError(error: unknown): boolean {
  return (
    typeof error === 'object' &&
    error !== null &&
    (error as { name?: unknown }).name === 'AbortError'
  )
}

function resolveCallerSignal(
  input: RequestInfo | URL,
  init?: RequestInit,
): AbortSignal | undefined {
  if (init?.signal) {
    return init.signal
  }
  return input instanceof Request ? input.signal : undefined
}

export interface TimeoutFetchConfig {
  /** Override the timeout (ms). Defaults to {@link REQUEST_TIMEOUT_MS}. */
  timeoutMs?: number
  /** Underlying fetch. Defaults to `globalThis.fetch`. Injectable for tests. */
  baseFetch?: typeof fetch
  /** Offline probe. Defaults to a `navigator.onLine` check. Injectable for tests. */
  isOffline?: () => boolean
}

/**
 * Build a `fetch` wrapper that:
 *  1. rejects immediately with {@link OfflineError} when the device is offline
 *     (no point waiting 30s for a request that cannot leave the WebView), and
 *  2. aborts any request that runs past the timeout, rejecting with
 *     {@link RequestTimeoutError}.
 *
 * A caller-supplied signal (e.g. Pinia Colada cancelling a superseded query on
 * navigation) is honoured and its abort is propagated verbatim — only our own timer
 * firing is translated into a `RequestTimeoutError`, so navigation-cancellations never
 * masquerade as timeouts.
 */
export function createTimeoutFetch(config: TimeoutFetchConfig = {}): typeof fetch {
  const timeoutMs = config.timeoutMs ?? REQUEST_TIMEOUT_MS
  const baseFetch = config.baseFetch ?? globalThis.fetch
  const isOffline = config.isOffline ?? defaultIsOffline

  return async function timeoutFetch(
    input: RequestInfo | URL,
    init?: RequestInit,
  ): Promise<Response> {
    if (isOffline()) {
      throw new OfflineError()
    }

    const callerSignal = resolveCallerSignal(input, init)
    // Already cancelled by the caller → let the base fetch reject with that reason.
    if (callerSignal?.aborted) {
      return baseFetch(input, init)
    }

    const controller = new AbortController()
    const onCallerAbort = () => controller.abort()
    callerSignal?.addEventListener('abort', onCallerAbort, { once: true })

    let timedOut = false
    const timer = setTimeout(() => {
      timedOut = true
      controller.abort()
    }, timeoutMs)

    try {
      return await baseFetch(input, { ...init, signal: controller.signal })
    } catch (error) {
      // Our timer fired (not a caller cancellation) and the base fetch aborted → timeout.
      if (timedOut && !callerSignal?.aborted && isAbortError(error)) {
        throw new RequestTimeoutError()
      }
      throw error
    } finally {
      clearTimeout(timer)
      callerSignal?.removeEventListener('abort', onCallerAbort)
    }
  }
}
