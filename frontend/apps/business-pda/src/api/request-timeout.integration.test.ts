import {
  configureApiClient,
  recordBusinessConsoleMesProductionReportMutationOptions,
} from '@nerv-iip/api-client'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createTimeoutFetch, RequestTimeoutError } from './request-timeout'

/**
 * End-to-end wiring proof: the fetch handed to `configureApiClient` (as done in
 * main.ts) is the one the generated business-console client actually dispatches
 * through, so a hung facade call surfaces as a `RequestTimeoutError` — the same
 * `Error` every PDA page renders as "网络超时…" in its `NvMobileResult` error state.
 *
 * A real backend is not needed: the base fetch hangs until aborted, exactly like a
 * request stuck on flaky 车间 WiFi.
 */
function hangUntilAborted(): typeof fetch {
  return ((_input: RequestInfo | URL, init?: RequestInit) =>
    new Promise<Response>((_resolve, reject) => {
      init?.signal?.addEventListener('abort', () => {
        reject(new DOMException('The operation was aborted.', 'AbortError'))
      })
    })) as typeof fetch
}

/** Headers resolve immediately, but the response BODY read (`text()`) stalls until abort. */
function headersOkBodyStalls(): typeof fetch {
  return ((_input: RequestInfo | URL, init?: RequestInit) =>
    Promise.resolve({
      ok: true,
      status: 200,
      body: {},
      headers: new Headers({ 'Content-Type': 'application/json' }),
      text: () =>
        new Promise((_resolve, reject) => {
          init?.signal?.addEventListener('abort', () =>
            reject(new DOMException('The operation was aborted.', 'AbortError')),
          )
        }),
    })) as unknown as typeof fetch
}

afterEach(() => {
  // Reset the shared client singletons back to their default fetch/baseUrl.
  configureApiClient()
  vi.useRealTimers()
})

describe('PDA api-client global timeout wiring', () => {
  it('surfaces a RequestTimeoutError when a facade write hangs past the timeout', async () => {
    configureApiClient({
      baseUrl: 'http://gateway.test',
      fetch: createTimeoutFetch({
        baseFetch: hangUntilAborted(),
        isOffline: () => false,
        timeoutMs: 50,
      }),
    })

    const options = recordBusinessConsoleMesProductionReportMutationOptions()
    const pending = options.mutation!(
      {
        body: {
          organizationId: 'org-1',
          environmentId: 'env-1',
          workOrderId: 'WO-1',
          operationTaskId: 'OP-1',
          goodQuantity: 1,
          scrapQuantity: 0,
          reportedAtUtc: '2026-07-10T00:00:00.000Z',
          idempotencyKey: 'idem-fixed-key',
        },
      } as Parameters<NonNullable<typeof options.mutation>>[0],
      // Pinia Colada passes a mutation context we don't need here.
      {} as never,
    )

    await expect(pending).rejects.toBeInstanceOf(RequestTimeoutError)
  })

  it('surfaces a RequestTimeoutError when a real facade RESPONSE BODY stalls after headers', async () => {
    vi.useFakeTimers()
    configureApiClient({
      baseUrl: 'http://gateway.test',
      fetch: createTimeoutFetch({
        baseFetch: headersOkBodyStalls(),
        isOffline: () => false,
        timeoutMs: 1_000,
      }),
    })

    const options = recordBusinessConsoleMesProductionReportMutationOptions()
    const pending = options.mutation!(
      {
        body: {
          organizationId: 'org-1',
          environmentId: 'env-1',
          workOrderId: 'WO-1',
          operationTaskId: 'OP-1',
          goodQuantity: 1,
          scrapQuantity: 0,
          reportedAtUtc: '2026-07-10T00:00:00.000Z',
          idempotencyKey: 'idem-fixed-key',
        },
      } as Parameters<NonNullable<typeof options.mutation>>[0],
      {} as never,
    )

    const assertion = expect(pending).rejects.toBeInstanceOf(RequestTimeoutError)
    await vi.advanceTimersByTimeAsync(1_000)
    await assertion
  })
})
