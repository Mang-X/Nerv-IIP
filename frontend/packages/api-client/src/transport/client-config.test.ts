import { describe, expect, it } from 'vitest'
import { getApiBaseUrl } from './base-url'

describe('getApiBaseUrl', () => {
  it('uses explicit Vite environment value first', () => {
    expect(getApiBaseUrl({ VITE_NERV_IIP_API_BASE_URL: 'http://127.0.0.1:58204' } as unknown as ImportMetaEnv)).toBe('http://127.0.0.1:58204')
  })

  it('uses browser-relative API base URL when no explicit value is configured', () => {
    expect(getApiBaseUrl({} as unknown as ImportMetaEnv)).toBe('')
  })
})
