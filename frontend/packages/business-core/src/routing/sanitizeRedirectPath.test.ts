import { describe, expect, it } from 'vitest'
import { sanitizeRedirectPath } from './sanitizeRedirectPath'

describe('sanitizeRedirectPath', () => {
  it('allows an internal path that starts with a single slash', () => {
    expect(sanitizeRedirectPath('/x')).toBe('/x')
    expect(sanitizeRedirectPath('/wms/inbound?from=scan')).toBe('/wms/inbound?from=scan')
  })

  it('rejects protocol-relative URLs (//host)', () => {
    expect(sanitizeRedirectPath('//evil.com')).toBe('/')
  })

  it('rejects backslash-prefixed paths (/\\host) browsers treat as protocol-relative', () => {
    expect(sanitizeRedirectPath('/\\evil.com')).toBe('/')
  })

  it('rejects absolute URLs', () => {
    expect(sanitizeRedirectPath('https://evil')).toBe('/')
    expect(sanitizeRedirectPath('http://evil.com/path')).toBe('/')
  })

  it('rejects paths that do not start with a slash', () => {
    expect(sanitizeRedirectPath('evil.com')).toBe('/')
    expect(sanitizeRedirectPath('')).toBe('/')
  })

  it('returns the default for non-string input', () => {
    expect(sanitizeRedirectPath(undefined)).toBe('/')
    expect(sanitizeRedirectPath(null)).toBe('/')
    expect(sanitizeRedirectPath(123)).toBe('/')
    expect(sanitizeRedirectPath(['/x'])).toBe('/')
  })
})
