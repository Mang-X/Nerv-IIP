import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

vi.mock('@/api/auth', () => ({
  loginConsole: vi.fn(async () => ({
    accessToken: 'tok-1',
    refreshToken: 'r-1',
    sessionId: 's-1',
    expiresAtUtc: new Date(Date.now() + 600_000).toISOString(),
    principal: { loginName: 'op01' },
  })),
  logoutConsole: vi.fn(async () => {}),
  refreshConsole: vi.fn(),
  getConsoleMe: vi.fn(),
}))

import { useAuthStore } from './auth'

describe('pda auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('is unauthenticated initially', () => {
    expect(useAuthStore().isAuthenticated).toBe(false)
  })

  it('authenticates and exposes the access token after login', async () => {
    const auth = useAuthStore()
    await auth.login('op01', 'pw')
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.accessToken).toBe('tok-1')
    expect(auth.displayName).toBe('op01')
  })

  it('clears the session on logout', async () => {
    const auth = useAuthStore()
    await auth.login('op01', 'pw')
    await auth.logout()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.accessToken).toBeUndefined()
  })
})
