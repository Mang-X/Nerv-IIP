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
 * Failure surfaces as a typed `Error` whose `.message` is the user-facing copy. Pages
 * classify the error via {@link describeRequestError} to decide both the copy AND
 * whether a retry is safe:
 *  - read-list pages show the copy + a refetch retry (GET retries are always safe);
 *  - write pages with a stable per-action idempotency key (MES/WMS) keep their retry —
 *    a lost response never double-applies;
 *  - write pages WITHOUT server-side idempotency (Maintenance report/inspect) must NOT
 *    offer a blind retry on an `indeterminate` failure (a dispatched-but-unanswered
 *    timeout / network drop), since the write may already have taken effect
 *    server-side; they steer the user to the list to verify instead. An OFFLINE
 *    pre-check is NOT indeterminate — the request never left the device, so those
 *    pages keep a safe retry (the #814 offline actionable-error requirement).
 *
 * Business errors thrown by the gateway are plain objects/strings (not `Error`) → a
 * determinate failure the server actually responded to, so retrying them is safe.
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
/** Response body-consuming methods the generated client uses (`response.text()` etc). */
const BODY_READERS = new Set(['arrayBuffer', 'blob', 'formData', 'json', 'text'])

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
    let settled = false
    // Deterministic, response-lifecycle cleanup: called when the request completes —
    // either the (bodyless) response is returned, the base fetch rejects, OR the body
    // finishes/aborts. This detaches the caller listener promptly instead of leaving it
    // attached on a signal the caller might reuse/long-hold (no listener accumulation).
    const settle = () => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      callerSignal?.removeEventListener('abort', onCallerAbort)
    }
    const timer = setTimeout(() => {
      timedOut = true
      controller.abort()
      callerSignal?.removeEventListener('abort', onCallerAbort)
    }, timeoutMs)

    let response: Response
    try {
      response = await baseFetch(input, { ...init, signal: controller.signal })
    } catch (error) {
      settle()
      // Our timer fired (not a caller cancellation) and the base fetch aborted → timeout.
      if (timedOut && !(callerSignal?.aborted ?? false) && isAbortError(error)) {
        throw new RequestTimeoutError()
      }
      throw error
    }

    // fetch() resolves at HEADERS, but the 30s must cover the WHOLE facade call: the
    // generated client (and the SOP blob download) read the body AFTER this. If there is
    // no body to read, the ceiling is already satisfied.
    const hasNoBody =
      response.body === null ||
      response.status === 204 ||
      response.status === 205 ||
      response.headers.get('Content-Length') === '0'
    if (hasNoBody) {
      settle()
      return response
    }

    // Keep the timer armed through body consumption: a body that stalls after headers
    // still aborts within the ceiling, and the abort is surfaced as a RequestTimeoutError.
    return wrapResponseBodyWithTimeout(response, {
      settle,
      isTimeout: () => timedOut && !(callerSignal?.aborted ?? false),
    })
  }
}

/**
 * Wrap a Response so its body-reading methods keep the timeout armed until the body
 * settles: on success clear the ceiling, on a stalled-body abort surface the timeout
 * copy. Non-body access (`status`/`headers`/`ok`/`clone`/…) passes straight through.
 */
function wrapResponseBodyWithTimeout(
  response: Response,
  hooks: { settle: () => void; isTimeout: () => boolean },
): Response {
  return new Proxy(response, {
    get(target, prop) {
      if (typeof prop === 'string' && BODY_READERS.has(prop)) {
        return async () => {
          try {
            const result = await (target as unknown as Record<string, () => Promise<unknown>>)[
              prop
            ]()
            hooks.settle()
            return result
          } catch (error) {
            hooks.settle()
            if (hooks.isTimeout() && isAbortError(error)) {
              throw new RequestTimeoutError()
            }
            throw error
          }
        }
      }
      const value = Reflect.get(target, prop, target)
      return typeof value === 'function' ? value.bind(target) : value
    },
  })
}

export type RequestErrorKind = 'timeout' | 'offline' | 'network' | 'business' | 'unknown'

export interface DescribedRequestError {
  kind: RequestErrorKind
  /** User-facing copy — the typed-error message for transport failures, else the server message. */
  message: string
  /**
   * `true` when the request was DISPATCHED but its server-side outcome is unknown — a
   * 30s timeout or a mid-flight network drop. A blind retry of a NON-idempotent write
   * could then duplicate the business fact. `false` when a retry is safe: either the
   * server responded with a definite error (no side effect), OR the request never left
   * the device (offline pre-check), so nothing could have happened server-side.
   */
  indeterminate: boolean
}

function extractServerMessage(error: unknown): string | undefined {
  if (typeof error === 'string') {
    return error.trim() || undefined
  }
  if (typeof error === 'object' && error !== null) {
    const record = error as Record<string, unknown>
    for (const key of ['message', 'detail', 'title', 'error']) {
      const value = record[key]
      if (typeof value === 'string' && value.trim()) {
        return value
      }
    }
  }
  return undefined
}

/**
 * Classify any error thrown by a facade call into a display message + a retry-safety
 * verdict. This is what lets pages tell timeout/offline (indeterminate — a
 * non-idempotent write must NOT auto-retry) apart from a definite server-side business
 * failure (safe to retry).
 *
 * `fallback` is the page's existing generic copy, kept for business errors without a
 * usable server message and for the unknown case.
 */
export function describeRequestError(
  error: unknown,
  fallback = '操作失败，请重试',
): DescribedRequestError {
  if (error instanceof OfflineError) {
    // The offline pre-check throws BEFORE baseFetch — the request never left the device,
    // so there is no server-side effect and a retry is safe once back online.
    return { kind: 'offline', message: error.message, indeterminate: false }
  }
  if (error instanceof RequestTimeoutError) {
    return { kind: 'timeout', message: error.message, indeterminate: true }
  }
  // A raw fetch network failure (DNS, refused connection, mid-flight drop) throws a
  // TypeError — the request may still have reached the server, so treat as indeterminate.
  if (error instanceof TypeError) {
    return { kind: 'network', message: '网络连接失败，请检查网络后重试', indeterminate: true }
  }
  // Any other Error: unknown shape, but it carries a stack → the request reached code,
  // not a transport hang; treat as a determinate failure.
  if (error instanceof Error) {
    return { kind: 'unknown', message: error.message || fallback, indeterminate: false }
  }
  // Non-Error thrown value = the gateway's business/HTTP error body (object or string).
  // The server responded, so no side effect is pending — safe to retry.
  return {
    kind: 'business',
    message: extractServerMessage(error) ?? fallback,
    indeterminate: false,
  }
}

/** True when a failure leaves the write's server-side outcome unknown (see {@link DescribedRequestError.indeterminate}). */
export function isIndeterminateError(error: unknown): boolean {
  return describeRequestError(error).indeterminate
}
