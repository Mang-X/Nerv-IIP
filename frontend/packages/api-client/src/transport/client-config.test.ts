import { describe, expect, it, vi } from 'vitest'
import { client } from '../generated/client.gen'
import { getApiBaseUrl } from './base-url'
import { configureApiClient } from './client-config'

describe('getApiBaseUrl', () => {
  it('uses explicit Vite environment value first', () => {
    expect(
      getApiBaseUrl({
        VITE_NERV_IIP_API_BASE_URL: 'http://127.0.0.1:58204',
      } as unknown as ImportMetaEnv),
    ).toBe('http://127.0.0.1:58204')
  })

  it('uses browser-relative API base URL when no explicit value is configured', () => {
    expect(getApiBaseUrl({} as unknown as ImportMetaEnv)).toBe('')
  })
})

describe('configureApiClient', () => {
  it('injects a bearer token from configured provider', async () => {
    const requests: Request[] = []
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const request = new Request(input, init)
      requests.push(request)
      return new Response('{}', {
        headers: { 'content-type': 'application/json' },
        status: 200,
      })
    })

    configureApiClient({
      accessTokenProvider: () => 'test-token',
      baseUrl: 'https://gateway.example.test',
      fetch,
    })

    await client.get({ url: '/secure' })

    expect(fetch).toHaveBeenCalledTimes(1)
    expect(requests[0]?.headers.get('Authorization')).toBe('Bearer test-token')
  })

  it('does not send Authorization after provider returns nothing', async () => {
    const requests: Request[] = []
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const request = new Request(input, init)
      requests.push(request)
      return new Response('{}', {
        headers: { 'content-type': 'application/json' },
        status: 200,
      })
    })
    let accessToken: string | undefined = 'test-token'

    configureApiClient({
      accessTokenProvider: () => accessToken,
      baseUrl: 'https://gateway.example.test',
      fetch,
    })

    await client.get({ url: '/secure' })
    accessToken = undefined
    await client.get({ url: '/secure' })

    expect(requests[0]?.headers.get('Authorization')).toBe('Bearer test-token')
    expect(requests[1]?.headers.has('Authorization')).toBe(false)
  })

  it('notifies once when response is 401', async () => {
    const onUnauthorized = vi.fn()
    const fetch = vi.fn(async () => {
      return new Response('{}', {
        headers: { 'content-type': 'application/json' },
        status: 401,
      })
    })

    configureApiClient({
      baseUrl: 'https://gateway.example.test',
      fetch,
      onUnauthorized: vi.fn(),
    })
    configureApiClient({
      baseUrl: 'https://gateway.example.test',
      fetch,
      onUnauthorized,
    })

    await client.get({ url: '/secure' })

    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })
})
