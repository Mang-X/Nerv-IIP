import { describe, expect, expectTypeOf, it } from 'vitest'
import { client } from './generated/client.gen'
import type { ListConsoleInstancesData } from './generated/types.gen'

describe('generated API client contract', () => {
  it('defaults to a browser-relative base URL instead of the OpenAPI export server', () => {
    const config = client.getConfig()

    expect(config.baseUrl ?? '').toBe('')
    expect(config.baseUrl).not.toBe('http://127.0.0.1:58204')
  })

  it('keeps only tenant scope query parameters required for listing console instances', () => {
    type Query = ListConsoleInstancesData['query']

    expectTypeOf<Query>().toEqualTypeOf<{
      organizationId: string
      environmentId: string
      pageNumber?: number | null
      pageSize?: number | null
      search?: string | null
    }>()
  })
})
