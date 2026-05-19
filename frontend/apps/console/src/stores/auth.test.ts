import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
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
  expiresAtUtc: '2030-05-18T08:05:00Z',
  principal,
}

describe('auth store', () => {
  beforeEach(() => {
    vi.useRealTimers()
    localStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
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

  it('loads the principal after login when the auth response omits it', async () => {
    api.loginConsole.mockResolvedValue({ ...session, principal: undefined })
    api.getConsoleMe.mockResolvedValue(principal)
    const auth = useAuthStore()

    await auth.login('admin', 'Admin123!')

    expect(api.getConsoleMe).toHaveBeenCalledWith('access-token')
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.principal?.loginName).toBe('admin')
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

  it('loads the principal after restore when the refresh response omits it', async () => {
    localStorage.setItem(
      'nerv-iip.console.auth',
      JSON.stringify({ refreshToken: 'refresh-token', sessionId: 'session-001', principal }),
    )
    api.refreshConsole.mockResolvedValue({ ...session, principal: undefined })
    api.getConsoleMe.mockResolvedValue(principal)
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(api.getConsoleMe).toHaveBeenCalledWith('access-token')
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

  it('refreshes the session 60 seconds before the access token expires', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-05-18T08:00:00Z'))
    api.loginConsole.mockResolvedValue({ ...session, expiresAtUtc: '2026-05-18T08:05:00Z' })
    api.refreshConsole.mockResolvedValue({
      ...session,
      accessToken: 'refreshed-access-token',
      refreshToken: 'refreshed-refresh-token',
      expiresAtUtc: '2026-05-18T08:10:00Z',
    })
    const auth = useAuthStore()

    await auth.login('admin', 'Admin123!')

    await vi.advanceTimersByTimeAsync(239_999)
    expect(api.refreshConsole).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1)

    expect(api.refreshConsole).toHaveBeenCalledTimes(1)
    expect(auth.accessToken).toBe('refreshed-access-token')
    expect(auth.refreshToken).toBe('refreshed-refresh-token')
  })

  it('deduplicates concurrent refresh requests', async () => {
    api.loginConsole.mockResolvedValue(session)
    let resolveRefresh: (value: typeof session) => void = () => undefined
    api.refreshConsole.mockReturnValue(
      new Promise<typeof session>((resolve) => {
        resolveRefresh = resolve
      }),
    )
    const auth = useAuthStore()
    await auth.login('admin', 'Admin123!')

    const firstRefresh = auth.refreshSession()
    const secondRefresh = auth.refreshSession()

    expect(api.refreshConsole).toHaveBeenCalledTimes(1)

    resolveRefresh({ ...session, accessToken: 'refreshed-access-token' })
    await Promise.all([firstRefresh, secondRefresh])

    expect(auth.accessToken).toBe('refreshed-access-token')
  })

  it('does not restore the session when logout happens during an inflight refresh', async () => {
    api.loginConsole.mockResolvedValue(session)
    api.logoutConsole.mockResolvedValue(undefined)
    let resolveRefresh: (value: typeof session) => void = () => undefined
    api.refreshConsole.mockReturnValue(
      new Promise<typeof session>((resolve) => {
        resolveRefresh = resolve
      }),
    )
    const auth = useAuthStore()
    await auth.login('admin', 'Admin123!')

    const refresh = auth.refreshSession()
    await auth.logout()

    resolveRefresh({
      ...session,
      accessToken: 'refreshed-access-token',
      refreshToken: 'refreshed-refresh-token',
    })
    await refresh

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.accessToken).toBeUndefined()
    expect(auth.refreshToken).toBeUndefined()
    expect(auth.principal).toBeUndefined()
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })

  it('clears the session and notifies the session-expired handler when refresh fails', async () => {
    api.loginConsole.mockResolvedValue(session)
    api.refreshConsole.mockRejectedValue(new Error('expired'))
    const onSessionExpired = vi.fn()
    const auth = useAuthStore()
    auth.setSessionExpiredHandler(onSessionExpired)
    await auth.login('admin', 'Admin123!')

    await expect(auth.refreshSession()).rejects.toThrow('expired')

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
    expect(onSessionExpired).toHaveBeenCalledTimes(1)
  })

  it('clears local state without waiting for logout request', async () => {
    api.loginConsole.mockResolvedValue(session)
    const auth = useAuthStore()
    api.logoutConsole.mockReturnValue(new Promise(() => undefined))
    await auth.login('admin', 'Admin123!')

    await auth.logout()

    expect(api.logoutConsole).toHaveBeenCalledWith('access-token', { sessionId: 'session-001' })
    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })
})
