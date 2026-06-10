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

import { refreshConsole } from '@/api/auth'
import { useAuthStore } from './auth'

const STORAGE_KEY = 'nerv-iip.business-pda.auth'

describe('pda auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.mocked(refreshConsole).mockReset()
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

  it('restores a valid stored session via refresh', async () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ refreshToken: 'r-old', sessionId: 's-old' }),
    )
    vi.mocked(refreshConsole).mockResolvedValue({
      accessToken: 'tok-2',
      refreshToken: 'r-2',
      sessionId: 's-2',
      principal: { loginName: 'op01' },
    })

    const auth = useAuthStore()
    await auth.restoreSession()

    expect(refreshConsole).toHaveBeenCalledWith({ refreshToken: 'r-old' })
    expect(auth.restoreStatus).toBe('restored')
    expect(auth.accessToken).toBe('tok-2')
    expect(auth.isAuthenticated).toBe(true)
  })

  it('clears the session when refresh fails during restore', async () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ refreshToken: 'r-old', sessionId: 's-old' }),
    )
    vi.mocked(refreshConsole).mockRejectedValue(new Error('refresh failed'))

    const auth = useAuthStore()
    await auth.restoreSession()

    expect(auth.restoreStatus).toBe('failed')
    expect(auth.accessToken).toBeUndefined()
    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('does not call refresh when the stored blob lacks a refresh token', async () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ sessionId: 's-old' }))

    const auth = useAuthStore()
    await auth.restoreSession()

    expect(refreshConsole).not.toHaveBeenCalled()
    expect(auth.restoreStatus).toBe('failed')
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })

  it('guards restoreSession against concurrent re-entry', async () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ refreshToken: 'r-old', sessionId: 's-old' }),
    )
    vi.mocked(refreshConsole).mockResolvedValue({
      accessToken: 'tok-2',
      refreshToken: 'r-2',
      sessionId: 's-2',
      principal: { loginName: 'op01' },
    })

    const auth = useAuthStore()
    const first = auth.restoreSession()
    // Second call while the first is in flight must short-circuit.
    await auth.restoreSession()
    await first

    expect(refreshConsole).toHaveBeenCalledTimes(1)
    expect(auth.restoreStatus).toBe('restored')
  })
})
