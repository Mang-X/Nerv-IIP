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
  principalId: 'user-admin',
  principalType: 'user',
  loginName: 'admin',
  email: 'admin@nerv-iip.local',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-001',
  expiresAtUtc: '2030-05-18T08:05:00Z',
  principal,
}

describe('business console auth store', () => {
  beforeEach(() => {
    vi.useRealTimers()
    localStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('stores session under the business console storage key only', async () => {
    api.loginConsole.mockResolvedValue(session)
    const auth = useAuthStore()

    await auth.login('admin', 'Admin123!')

    expect(auth.isAuthenticated).toBe(true)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
    const stored = JSON.parse(
      localStorage.getItem('nerv-iip.business-console.auth') ?? '{}',
    ) as Record<string, unknown>
    expect(stored).toMatchObject({
      principal,
      refreshToken: 'refresh-token',
      sessionId: 'session-001',
    })
    expect(stored).not.toHaveProperty('accessToken')
  })

  it('restores a saved refresh token from business console storage', async () => {
    localStorage.setItem(
      'nerv-iip.business-console.auth',
      JSON.stringify({ refreshToken: 'refresh-token', sessionId: 'session-001', principal }),
    )
    api.refreshConsole.mockResolvedValue(session)
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(api.refreshConsole).toHaveBeenCalledWith({ refreshToken: 'refresh-token' })
    expect(auth.isAuthenticated).toBe(true)
  })

  it('clears local state without waiting for logout request', async () => {
    api.loginConsole.mockResolvedValue(session)
    api.logoutConsole.mockReturnValue(new Promise(() => undefined))
    const auth = useAuthStore()
    await auth.login('admin', 'Admin123!')

    await auth.logout()

    expect(api.logoutConsole).toHaveBeenCalledWith('access-token', { sessionId: 'session-001' })
    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.business-console.auth')).toBeNull()
  })
})
