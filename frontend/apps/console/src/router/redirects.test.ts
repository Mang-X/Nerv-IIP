import { describe, expect, it } from 'vitest'
import { sanitizeRedirectPath } from './redirects'

describe('sanitizeRedirectPath', () => {
  it('allows app-local paths', () => {
    expect(sanitizeRedirectPath('/operations/task-001?tab=audit')).toBe(
      '/operations/task-001?tab=audit',
    )
  })

  it('rejects external and malformed paths', () => {
    expect(sanitizeRedirectPath('//evil.test/x')).toBe('/')
    expect(sanitizeRedirectPath('https://evil.test/x')).toBe('/')
    expect(sanitizeRedirectPath('operations/task-001')).toBe('/')
  })

  it('rejects login redirect loops', () => {
    expect(sanitizeRedirectPath('/login')).toBe('/')
    expect(sanitizeRedirectPath('/login?redirect=/')).toBe('/')
  })
})
