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
  it('does not send static headers from a previous configuration when later config omits headers', async () => {
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
      baseUrl: 'https://gateway.example.test',
      fetch,
      headers: { 'X-Org': 'org-a' },
    })
    configureApiClient({
      baseUrl: 'https://gateway.example.test',
      fetch,
    })

    await client.get({ url: '/secure' })

    expect(requests[0]?.headers.has('X-Org')).toBe(false)
  })

  it('preserves per-request headers matching a previous static header name', async () => {
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
      baseUrl: 'https://gateway.example.test',
      fetch,
      headers: { 'X-Org': 'org-a' },
    })
    configureApiClient({
      baseUrl: 'https://gateway.example.test',
      fetch,
    })

    await client.get({
      headers: { 'X-Org': 'org-b' },
      url: '/secure',
    })

    expect(requests[0]?.headers.get('X-Org')).toBe('org-b')
  })

  it('lets per-request headers override current static headers', async () => {
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
      baseUrl: 'https://gateway.example.test',
      fetch,
      headers: { 'X-Org': 'org-a' },
    })

    await client.get({
      headers: { 'X-Org': 'org-b' },
      url: '/secure',
    })

    expect(requests[0]?.headers.get('X-Org')).toBe('org-b')
  })

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

  it('preserves an explicit per-request Authorization header when provider returns nothing', async () => {
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
      accessTokenProvider: () => undefined,
      baseUrl: 'https://gateway.example.test',
      fetch,
    })

    await client.post({
      headers: { Authorization: 'Bearer logout-token' },
      url: '/secure/logout',
    })

    expect(requests[0]?.headers.get('Authorization')).toBe('Bearer logout-token')
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
