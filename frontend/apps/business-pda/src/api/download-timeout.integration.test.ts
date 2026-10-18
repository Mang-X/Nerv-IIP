import { configureApiClient } from '@nerv-iip/api-client'
import { openSopFileContent } from '@nerv-iip/business-core'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createTimeoutFetch } from './request-timeout'

/**
 * End-to-end proof of the REAL composition the reviewer flagged: the SDK uses
 * the configured `createTimeoutFetch()` for downloads. Even though `fetch()` resolves at
 * headers, the caller→signal link must stay live so `openSopFileContent`'s own
 * ceiling can abort a body that stalls AFTER headers — otherwise `response.blob()`
 * hangs unbounded.
 */
afterEach(() => {
  configureApiClient()
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('SOP download composed with the real PDA timeout fetch', () => {
  it('aborts a body that stalls after headers and rejects with the timeout copy', async () => {
    vi.useFakeTimers()

    let bodyStarted!: () => void
    const started = new Promise<void>((resolve) => {
      bodyStarted = resolve
    })
    const baseFetch: typeof fetch = async (input, init) => {
      const request = new Request(input, init)
      return new Response(
        new ReadableStream({
          start(controller) {
            request.signal.addEventListener('abort', () =>
              controller.error(new DOMException('Aborted', 'AbortError')),
            )
            bodyStarted()
          },
        }),
      )
    }
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false })
    configureApiClient({ baseUrl: 'https://gateway.example', fetch: timeoutFetch })
    const pending = openSopFileContent(
      'file-sop-1',
      { organizationId: 'org-001', environmentId: 'env-dev' },
      { timeoutMs: 50 },
    )
    const settled = vi.fn()
    void pending.then(settled, settled)
    // Advance past the download's own 50ms ceiling → outer abort must reach the native body.
    await started
    await vi.advanceTimersByTimeAsync(50)
    expect(settled).toHaveBeenCalledOnce()
    await expect(pending).rejects.toThrow('网络超时')
  })
})
