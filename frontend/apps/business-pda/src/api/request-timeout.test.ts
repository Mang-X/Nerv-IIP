import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  createTimeoutFetch,
  OfflineError,
  REQUEST_TIMEOUT_MS,
  RequestTimeoutError,
} from './request-timeout'

/** A fetch that never resolves on its own — only rejects (AbortError) when its signal aborts. */
function hangingFetch(): typeof fetch {
  return ((_input: RequestInfo | URL, init?: RequestInit) =>
    new Promise<Response>((_resolve, reject) => {
      const signal = init?.signal
      if (signal) {
        signal.addEventListener('abort', () => {
          reject(new DOMException('The operation was aborted.', 'AbortError'))
        })
      }
    })) as typeof fetch
}

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('createTimeoutFetch', () => {
  it('rejects with OfflineError and never dispatches when offline', async () => {
    const baseFetch = vi.fn<typeof fetch>()
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => true })

    await expect(timeoutFetch('/api/business-console/v1/ping')).rejects.toBeInstanceOf(OfflineError)
    expect(baseFetch).not.toHaveBeenCalled()
  })

  it('passes a successful response straight through', async () => {
    const response = new Response('ok', { status: 200 })
    const baseFetch = vi.fn<typeof fetch>().mockResolvedValue(response)
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false })

    await expect(timeoutFetch('/api/business-console/v1/ping')).resolves.toBe(response)
    expect(baseFetch).toHaveBeenCalledTimes(1)
  })

  it('translates its own timeout abort into a RequestTimeoutError', async () => {
    vi.useFakeTimers()
    const timeoutFetch = createTimeoutFetch({
      baseFetch: hangingFetch(),
      isOffline: () => false,
      timeoutMs: 1_000,
    })

    const pending = timeoutFetch('/api/business-console/v1/slow')
    const assertion = expect(pending).rejects.toBeInstanceOf(RequestTimeoutError)
    await vi.advanceTimersByTimeAsync(1_000)
    await assertion
  })

  it('propagates a caller cancellation verbatim instead of masking it as a timeout', async () => {
    const controller = new AbortController()
    const timeoutFetch = createTimeoutFetch({
      baseFetch: hangingFetch(),
      isOffline: () => false,
    })

    const pending = timeoutFetch('/api/business-console/v1/slow', { signal: controller.signal })
    const assertion = expect(pending).rejects.toSatisfy(
      (error: unknown) =>
        !(error instanceof RequestTimeoutError) &&
        (error as { name?: string })?.name === 'AbortError',
    )
    controller.abort()
    await assertion
  })

  it('short-circuits to the base fetch when the caller signal is already aborted', async () => {
    const controller = new AbortController()
    controller.abort()
    const rejected = new DOMException('The operation was aborted.', 'AbortError')
    const baseFetch = vi.fn<typeof fetch>().mockRejectedValue(rejected)
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false })

    await expect(
      timeoutFetch('/api/business-console/v1/ping', { signal: controller.signal }),
    ).rejects.toBe(rejected)
    expect(baseFetch).toHaveBeenCalledTimes(1)
  })

  it('passes gateway/business errors through untouched (not every failure is a timeout)', async () => {
    const businessError = { success: false, message: '工序状态非法' }
    const baseFetch = vi.fn<typeof fetch>().mockRejectedValue(businessError)
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false })

    await expect(timeoutFetch('/api/business-console/v1/report')).rejects.toBe(businessError)
  })
})

describe('typed request errors', () => {
  it('default to actionable Chinese copy and remain Error instances', () => {
    const timeout = new RequestTimeoutError()
    const offline = new OfflineError()

    expect(timeout).toBeInstanceOf(Error)
    expect(timeout.message).toBe('网络超时，请检查连接后重试')
    expect(offline).toBeInstanceOf(Error)
    expect(offline.message).toBe('当前离线，请检查网络连接后重试')
  })

  it('exposes a 30s default ceiling', () => {
    expect(REQUEST_TIMEOUT_MS).toBe(30_000)
  })
})
