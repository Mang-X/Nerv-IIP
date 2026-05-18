import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from './auth'

const api = vi.hoisted(() => ({
  getConsoleMe: vi.fn(),
  loginConsole: vi.fn(),
  logoutConsole: vi.fn(),
  refreshConsole: vi.fn(),
}))

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
  expiresAtUtc: '2026-05-18T08:00:00Z',
  principal,
}

describe('auth store', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('stores session after login', async () => {
    api.loginConsole.mockResolvedValue(session)
    const auth = useAuthStore()

    await auth.login('admin', 'Admin123!')

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.accessToken).toBe('access-token')
    expect(auth.principal?.loginName).toBe('admin')
    const stored = JSON.parse(localStorage.getItem('nerv-iip.console.auth') ?? '{}') as Record<
      string,
      unknown
    >
    expect(stored).toMatchObject({
      principal,
      refreshToken: 'refresh-token',
      sessionId: 'session-001',
    })
    expect(stored).not.toHaveProperty('accessToken')
  })

  it('clears state after login failure', async () => {
    api.loginConsole.mockRejectedValue(new Error('Invalid credentials.'))
    const auth = useAuthStore()

    await expect(auth.login('admin', 'wrong')).rejects.toThrow('Invalid credentials.')

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })

  it('rejects incomplete auth responses', async () => {
    api.loginConsole.mockResolvedValue({ ...session, accessToken: undefined })
    const auth = useAuthStore()

    await expect(auth.login('admin', 'Admin123!')).rejects.toThrow(
      'Authentication service returned an invalid session.',
    )

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })

  it('restores a saved refresh token', async () => {
    localStorage.setItem(
      'nerv-iip.console.auth',
      JSON.stringify({ refreshToken: 'refresh-token', sessionId: 'session-001', principal }),
    )
    api.refreshConsole.mockResolvedValue(session)
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(api.refreshConsole).toHaveBeenCalledWith({ refreshToken: 'refresh-token' })
    expect(auth.isAuthenticated).toBe(true)
  })

  it('clears storage for structurally invalid saved sessions', async () => {
    localStorage.setItem(
      'nerv-iip.console.auth',
      JSON.stringify({ refreshToken: 123, sessionId: {} }),
    )
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(api.refreshConsole).not.toHaveBeenCalled()
    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })

  it('clears storage when restore fails', async () => {
    localStorage.setItem(
      'nerv-iip.console.auth',
      JSON.stringify({ refreshToken: 'bad-token', sessionId: 'session-001', principal }),
    )
    api.refreshConsole.mockRejectedValue(new Error('expired'))
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })

  it('clears local state even when logout request fails', async () => {
    api.loginConsole.mockResolvedValue(session)
    api.logoutConsole.mockRejectedValue(new Error('network'))
    const auth = useAuthStore()
    await auth.login('admin', 'Admin123!')

    await auth.logout()

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })
})
