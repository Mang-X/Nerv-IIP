import { configureApiClient } from '@nerv-iip/api-client'
import { describe, expect, it, vi } from 'vitest'
import { consoleAuthApi } from './auth'

/**
 * Maintainer concern (PR #365): in a Capacitor WebView there is NO vite proxy, so when
 * `VITE_NERV_IIP_API_BASE_URL` resolves to an absolute gateway URL, auth requests must
 * target THAT absolute host — not the relative WebView origin.
 *
 * This is an integration test against the REAL `@nerv-iip/api-client` transport: we
 * inject an absolute `baseUrl` + a mock `fetch` through `configureApiClient`, then drive
 * a genuine `consoleAuthApi.loginConsole(...)` call and assert the URL the transport
 * actually builds and hands to fetch. The generated client honours an injected `fetch`
 * (`options.fetch ?? _config.fetch ?? globalThis.fetch`) and builds the request as
 * `baseUrl + '/api/console/v1/auth/login'`, so the assertion proves the real target.
 */
describe('auth request target (non-dev origin / absolute gateway)', () => {
  it('sends the login request to the absolute gateway host, not the relative origin', async () => {
    const captured: string[] = []
    const mockFetch = vi.fn(async (input: Request | string | URL) => {
      const url = input instanceof Request ? input.url : String(input)
      captured.push(url)
      return new Response(
        JSON.stringify({ success: true, message: null, data: { accessToken: 't' } }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      )
    })

    configureApiClient({
      baseUrl: 'https://gw.example.test',
      fetch: mockFetch as unknown as typeof fetch,
    })

    await consoleAuthApi.loginConsole({ loginName: 'x', password: 'y' })

    expect(mockFetch).toHaveBeenCalledTimes(1)
    const requestedUrl = captured[0]
    expect(requestedUrl.startsWith('https://gw.example.test')).toBe(true)
    expect(new URL(requestedUrl).pathname).toBe('/api/console/v1/auth/login')
  })
})
