import { openDownloadGrantBlob } from '@nerv-iip/business-core'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createTimeoutFetch } from './request-timeout'

/**
 * End-to-end proof of the REAL composition the reviewer flagged: PDA passes
 * `createTimeoutFetch()` as the download fetch. Even though `fetch()` resolves at
 * headers, the caller→signal link must stay live so `openDownloadGrantBlob`'s own
 * ceiling can abort a body that stalls AFTER headers — otherwise `response.blob()`
 * hangs unbounded.
 */
afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('SOP download composed with the real PDA timeout fetch', () => {
  it('aborts a body that stalls after headers and rejects with the timeout copy', async () => {
    vi.useFakeTimers()

    // Native fetch: headers resolve immediately; blob() only settles when the (forwarded)
    // signal aborts — i.e. the body stream stalls until aborted.
    const baseFetch = ((_input: RequestInfo | URL, init?: RequestInit) =>
      Promise.resolve({
        ok: true,
        status: 200,
        body: {},
        headers: new Headers(),
        blob: () =>
          new Promise((_resolve, reject) => {
            init?.signal?.addEventListener('abort', () =>
              reject(new DOMException('The operation was aborted.', 'AbortError')),
            )
          }),
      })) as unknown as typeof fetch

    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false })
    const pending = openDownloadGrantBlob(
      { downloadUrl: '/api/business-console/v1/files/download-grants/g/content' },
      { fetch: timeoutFetch, timeoutMs: 50 },
    )
    const assertion = expect(pending).rejects.toThrow('网络超时')
    // Advance past the download's own 50ms ceiling → outer abort must reach the native body.
    await vi.advanceTimersByTimeAsync(50)
    await assertion
  })
})
