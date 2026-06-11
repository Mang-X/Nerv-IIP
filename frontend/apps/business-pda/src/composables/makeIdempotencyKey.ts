let fallbackCounter = 0

/**
 * Generates a unique idempotency key for create/action requests so retries
 * (flaky PDA network, double taps) never double-apply on the gateway.
 *
 * Prefers the platform `crypto.randomUUID()`. Falls back to a timestamp +
 * monotonic-ish performance counter when the secure RNG is unavailable.
 */
export function makeIdempotencyKey(): string {
  const cryptoRef = globalThis.crypto
  if (cryptoRef && typeof cryptoRef.randomUUID === 'function') {
    return cryptoRef.randomUUID()
  }

  const perf = typeof performance !== 'undefined' && typeof performance.now === 'function'
    ? Math.trunc(performance.now())
    : (fallbackCounter += 1)

  return `idem-${Date.now()}-${perf}`
}
