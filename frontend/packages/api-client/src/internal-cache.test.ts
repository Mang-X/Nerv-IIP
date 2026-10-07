import { afterEach, expect, it } from 'vitest'
import {
  configureApiClient,
  invalidateGatewayCacheScope,
  type InvalidateGatewayCacheScopeRequest,
} from './index'

afterEach(() => configureApiClient())

it('sends the explicit organization/environment through the stable generated SDK operation', async () => {
  const requests: Request[] = []
  configureApiClient({
    baseUrl: 'https://gateway.example.test',
    accessTokenProvider: () => 'scoped-cache-token',
    fetch: async (input, init) => {
      requests.push(new Request(input, init))
      return new Response(null, { status: 204 })
    },
  })
  const body: InvalidateGatewayCacheScopeRequest = {
    organizationId: 'org-001',
    environmentId: 'env-dev',
  }
  // The generated contract must reject a partial scope at compile time.
  // @ts-expect-error environmentId is required
  const partial: InvalidateGatewayCacheScopeRequest = { organizationId: 'org-001' }
  void partial
  const result = await invalidateGatewayCacheScope({ body })
  expect(result.response?.status).toBe(204)
  expect(requests).toHaveLength(1)
  expect(requests[0]!.url).toBe(
    'https://gateway.example.test/internal/gateway/cache/invalidate-scope',
  )
  expect(requests[0]!.method).toBe('POST')
  expect(requests[0]!.headers.get('Authorization')).toBe('Bearer scoped-cache-token')
  expect(await requests[0]!.json()).toEqual({ organizationId: 'org-001', environmentId: 'env-dev' })
})
