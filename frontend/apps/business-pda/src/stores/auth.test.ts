import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from './auth'

const api = vi.hoisted(() => {
  const consoleAuthApi = {
    getConsoleMe: vi.fn(),
    loginConsole: vi.fn(),
    logoutConsole: vi.fn(),
    refreshConsole: vi.fn(),
  }
  return {
    ...consoleAuthApi,
    consoleAuthApi,
  }
})

vi.mock('@/api/auth', () => api)

const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'operator01',
  email: 'operator01@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

const STORAGE_KEY = 'nerv-iip.business-pda.auth'

describe('pda auth store', () => {
  beforeEach(() => {
    vi.useRealTimers()
    localStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('stores the session under the business-pda storage key only', async () => {
    api.loginConsole.mockResolvedValue(session)
    const auth = useAuthStore()

    await auth.login('operator01', 'pw')

    expect(auth.isAuthenticated).toBe(true)
    const stored = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Record<string, unknown>
    expect(stored).toMatchObject({
      principal,
      refreshToken: 'refresh-token',
      sessionId: 'session-1',
    })
    expect(stored).not.toHaveProperty('accessToken')
  })

  it('restores a saved refresh token from business-pda storage', async () => {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ refreshToken: 'refresh-token', sessionId: 'session-1', principal }),
    )
    api.refreshConsole.mockResolvedValue(session)
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(api.refreshConsole).toHaveBeenCalledWith({ refreshToken: 'refresh-token' })
    expect(auth.restoreStatus).toBe('restored')
    expect(auth.isAuthenticated).toBe(true)
  })

  it('clears local state without waiting for logout request', async () => {
    api.loginConsole.mockResolvedValue(session)
    api.logoutConsole.mockReturnValue(new Promise(() => undefined))
    const auth = useAuthStore()
    await auth.login('operator01', 'pw')

    await auth.logout()

    expect(api.logoutConsole).toHaveBeenCalledWith('access-token', { sessionId: 'session-1' })
    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull()
  })
})
