import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  ConsoleAuthError,
  createAuthGuard,
  createAuthStore,
  configureAuthenticatedApiClient,
  createConsoleAuthApi,
  handleUnauthorizedRedirect,
  sanitizeRedirectPath,
} from './index'

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

const messages = {
  accountLocked: (lockoutUntilUtc?: string) => `Locked until ${lockoutUntilUtc ?? 'later'}.`,
  changePasswordFallback: 'Change password failed.',
  invalidCredentialsOrExpiredSession: 'Bad credentials.',
  loginFallback: 'Login failed.',
  refreshFallback: 'Refresh failed.',
  principalFallback: 'Principal failed.',
  remainingAttempts: (count: number) => `${count} attempts remaining.`,
  invalidSession: 'Invalid session.',
  loginFailed: 'Unable to sign in.',
  unknownUser: 'Unknown user',
}

function createClient() {
  return {
    changeConsolePassword: vi.fn(),
    getConsolePrincipal: vi.fn(),
    loginConsoleUser: vi.fn(),
    logoutConsoleSession: vi.fn(),
    refreshConsoleSession: vi.fn(),
  }
}

function createApi() {
  const client = createClient()
  return {
    client,
    api: createConsoleAuthApi({
      client,
      messages,
    }),
  }
}

describe('console auth api factory', () => {
  it('unwraps successful auth responses from an injected client', async () => {
    const { api, client } = createApi()
    client.loginConsoleUser.mockResolvedValue({ data: { success: true, data: session } })

    await expect(api.loginConsole({ loginName: 'admin', password: 'secret' })).resolves.toBe(
      session,
    )

    expect(client.loginConsoleUser).toHaveBeenCalledWith({
      body: { loginName: 'admin', password: 'secret' },
    })
  })

  it('uses injected copy and status when auth responses fail', async () => {
    const { api, client } = createApi()
    client.loginConsoleUser.mockResolvedValue({
      data: { success: false },
      response: new Response(null, { status: 401 }),
    })

    await expect(api.loginConsole({ loginName: 'admin', password: 'bad' })).rejects.toMatchObject({
      message: 'Bad credentials.',
      status: 401,
    } satisfies Partial<ConsoleAuthError>)
  })

  it('maps safe lockout metadata to a user-facing login error', async () => {
    const { api, client } = createApi()
    client.loginConsoleUser.mockResolvedValue({
      data: { success: false },
      response: new Response(null, {
        headers: {
          'X-Nerv-Iam-Lockout-Until-Utc': '2026-08-18T08:30:00.0000000+00:00',
          'X-Nerv-Iam-Login-Failure': 'iam-account-locked',
        },
        status: 401,
      }),
    })

    await expect(api.loginConsole({ loginName: 'admin', password: 'bad' })).rejects.toMatchObject({
      code: 'iam-account-locked',
      lockoutUntilUtc: '2026-08-18T08:30:00.0000000+00:00',
      message: 'Locked until 2026-08-18T08:30:00.0000000+00:00.',
      status: 401,
    } satisfies Partial<ConsoleAuthError>)
  })

  it('maps remaining attempts to a user-facing login warning', async () => {
    const { api, client } = createApi()
    client.loginConsoleUser.mockResolvedValue({
      data: { success: false },
      response: new Response(null, {
        headers: {
          'X-Nerv-Iam-Login-Failure': 'iam-invalid-credentials',
          'X-Nerv-Iam-Remaining-Attempts': '2',
        },
        status: 401,
      }),
    })

    await expect(api.loginConsole({ loginName: 'admin', password: 'bad' })).rejects.toMatchObject({
      code: 'iam-invalid-credentials',
      message: '2 attempts remaining.',
      remainingAttempts: 2,
      status: 401,
    } satisfies Partial<ConsoleAuthError>)
  })

  it('sends bearer auth for principal and logout calls', async () => {
    const { api, client } = createApi()
    client.getConsolePrincipal.mockResolvedValue({ data: { success: true, data: principal } })
    client.logoutConsoleSession.mockResolvedValue({})

    await expect(api.getConsoleMe('access-token')).resolves.toBe(principal)
    await api.logoutConsole('access-token', { sessionId: 'session-001' })

    expect(client.getConsolePrincipal).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer access-token' },
    })
    expect(client.logoutConsoleSession).toHaveBeenCalledWith({
      body: { sessionId: 'session-001' },
      headers: { Authorization: 'Bearer access-token' },
    })
  })
})

describe('console change password api', () => {
  it('sends the current bearer and resolves on success', async () => {
    const { api, client } = createApi()
    client.changeConsolePassword.mockResolvedValue({
      response: new Response(null, { status: 204 }),
    })

    await api.changeConsolePassword('access-token', {
      currentPassword: 'Old123!',
      newPassword: 'New123!',
    })

    expect(client.changeConsolePassword).toHaveBeenCalledWith({
      body: { currentPassword: 'Old123!', newPassword: 'New123!' },
      headers: { Authorization: 'Bearer access-token' },
    })
  })

  it.each([
    ['Current password is invalid.', '当前密码不正确。'],
    ['Password must be at least 8 characters.', '新密码至少需要 8 个字符。'],
    ['Password was recently used.', '新密码不能与最近使用过的密码相同。'],
    ['Some future IAM rule.', 'Some future IAM rule.'],
  ])('shows the IAM rejection reason %s as %s', async (iamMessage, shown) => {
    const { api, client } = createApi()
    client.changeConsolePassword.mockResolvedValue({
      error: { message: iamMessage, code: 400 },
      response: new Response(null, { status: 400 }),
    })

    await expect(
      api.changeConsolePassword('access-token', { currentPassword: 'x', newPassword: 'y' }),
    ).rejects.toMatchObject({ message: shown, status: 400 } satisfies Partial<ConsoleAuthError>)
  })

  it('reports an expired session on 401', async () => {
    const { api, client } = createApi()
    client.changeConsolePassword.mockResolvedValue({
      response: new Response(null, { status: 401 }),
    })

    await expect(
      api.changeConsolePassword('access-token', { currentPassword: 'x', newPassword: 'y' }),
    ).rejects.toMatchObject({ message: 'Bad credentials.', status: 401 })
  })
})

describe('auth store factory', () => {
  beforeEach(() => {
    vi.useRealTimers()
    localStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('uses injected store id, storage key, api, and localized labels', async () => {
    const { api, client } = createApi()
    client.loginConsoleUser.mockResolvedValue({
      data: { success: true, data: { ...session, principal: undefined } },
    })
    client.getConsolePrincipal.mockResolvedValue({ data: { success: true, data: principal } })
    const useAuthStore = createAuthStore({
      api,
      messages,
      storageKey: 'nerv-iip.test.auth',
      storeId: 'test-auth',
    })

    const auth = useAuthStore()

    expect(auth.$id).toBe('test-auth')
    expect(auth.displayName).toBe('Unknown user')

    await auth.login('admin', 'secret')

    expect(client.getConsolePrincipal).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer access-token' },
    })
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.displayName).toBe('admin')
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
    expect(JSON.parse(localStorage.getItem('nerv-iip.test.auth') ?? '{}')).toMatchObject({
      principal,
      refreshToken: 'refresh-token',
      sessionId: 'session-001',
    })
  })

  it('restores, deduplicates refreshes, notifies refresh expiry, and logs out without waiting', async () => {
    const { api, client } = createApi()
    const useAuthStore = createAuthStore({
      api,
      messages,
      storageKey: 'nerv-iip.test.auth',
      storeId: 'test-auth',
    })
    localStorage.setItem(
      'nerv-iip.test.auth',
      JSON.stringify({ principal, refreshToken: 'stored-refresh', sessionId: 'session-001' }),
    )
    client.refreshConsoleSession.mockResolvedValueOnce({ data: { success: true, data: session } })

    const auth = useAuthStore()
    await auth.restoreSession()

    expect(client.refreshConsoleSession).toHaveBeenCalledWith({
      body: { refreshToken: 'stored-refresh' },
    })
    expect(auth.isAuthenticated).toBe(true)

    client.refreshConsoleSession.mockReset()
    let rejectRefresh: (error: Error) => void = () => undefined
    client.refreshConsoleSession.mockReturnValue(
      new Promise((_, reject) => {
        rejectRefresh = reject
      }),
    )
    const onSessionExpired = vi.fn()
    auth.setSessionExpiredHandler(onSessionExpired)

    const firstRefresh = auth.refreshSession()
    const secondRefresh = auth.refreshSession()

    expect(client.refreshConsoleSession).toHaveBeenCalledTimes(1)

    rejectRefresh(new Error('expired'))
    await expect(firstRefresh).rejects.toThrow('expired')
    await expect(secondRefresh).rejects.toThrow('expired')

    expect(auth.isAuthenticated).toBe(false)
    expect(onSessionExpired).toHaveBeenCalledWith('refresh-failed')

    client.loginConsoleUser.mockResolvedValue({ data: { success: true, data: session } })
    client.logoutConsoleSession.mockReturnValue(new Promise(() => undefined))

    await auth.login('admin', 'secret')
    await auth.logout()

    expect(client.logoutConsoleSession).toHaveBeenCalledWith({
      body: { sessionId: 'session-001' },
      headers: { Authorization: 'Bearer access-token' },
    })
    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.test.auth')).toBeNull()
  })

  it('tracks the password-change flag and keeps the current session after changing password', async () => {
    const { api, client } = createApi()
    const useAuthStore = createAuthStore({
      api,
      messages,
      storageKey: 'nerv-iip.test.auth',
      storeId: 'test-auth',
    })
    client.loginConsoleUser.mockResolvedValue({
      data: { success: true, data: { ...session, passwordChangeRequired: true } },
    })
    client.changeConsolePassword.mockResolvedValue({
      response: new Response(null, { status: 204 }),
    })
    client.refreshConsoleSession.mockResolvedValue({
      data: {
        success: true,
        data: {
          ...session,
          accessToken: 'rotated-access-token',
          refreshToken: 'rotated-refresh-token',
          passwordChangeRequired: false,
        },
      },
    })
    const auth = useAuthStore()

    await auth.login('admin', 'Reset123!')
    expect(auth.passwordChangeRequired).toBe(true)

    await auth.changePassword('Reset123!', 'Changed123!')

    expect(client.changeConsolePassword).toHaveBeenCalledWith({
      body: { currentPassword: 'Reset123!', newPassword: 'Changed123!' },
      headers: { Authorization: 'Bearer access-token' },
    })
    expect(client.refreshConsoleSession).toHaveBeenCalledWith({
      body: { refreshToken: 'refresh-token' },
    })
    expect(auth.passwordChangeRequired).toBe(false)
    expect(auth.accessToken).toBe('rotated-access-token')
    expect(auth.isAuthenticated).toBe(true)
  })

  it('keeps the password-change flag when a full page reload restores the session', async () => {
    const { api, client } = createApi()
    const useAuthStore = createAuthStore({
      api,
      messages,
      storageKey: 'nerv-iip.test.auth',
      storeId: 'test-auth',
    })
    localStorage.setItem(
      'nerv-iip.test.auth',
      JSON.stringify({ principal, refreshToken: 'stored-refresh', sessionId: 'session-001' }),
    )
    client.refreshConsoleSession.mockResolvedValue({
      data: { success: true, data: { ...session, passwordChangeRequired: true } },
    })
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.passwordChangeRequired).toBe(true)
  })

  it('keeps the session and flag when the password change is rejected', async () => {
    const { api, client } = createApi()
    const useAuthStore = createAuthStore({
      api,
      messages,
      storageKey: 'nerv-iip.test.auth',
      storeId: 'test-auth',
    })
    client.loginConsoleUser.mockResolvedValue({
      data: { success: true, data: { ...session, passwordChangeRequired: true } },
    })
    client.changeConsolePassword.mockResolvedValue({
      error: { message: 'Current password is invalid.' },
      response: new Response(null, { status: 400 }),
    })
    const auth = useAuthStore()
    await auth.login('admin', 'Reset123!')

    await expect(auth.changePassword('wrong', 'Changed123!')).rejects.toThrow('当前密码不正确。')

    expect(client.refreshConsoleSession).not.toHaveBeenCalled()
    expect(auth.passwordChangeRequired).toBe(true)
    expect(auth.isAuthenticated).toBe(true)
  })

  it('offers an explicit bounded logout that reports revoke success, failure, and timeout', async () => {
    const { api, client } = createApi()
    const useAuthStore = createAuthStore({
      api,
      messages,
      storageKey: 'nerv-iip.test.auth',
      storeId: 'test-auth',
    })
    client.loginConsoleUser.mockResolvedValue({ data: { success: true, data: session } })
    const auth = useAuthStore()

    client.logoutConsoleSession.mockResolvedValueOnce({})
    await auth.login('admin', 'secret')
    await expect(auth.logoutAndRevoke({ timeoutMs: 20 })).resolves.toEqual({ status: 'revoked' })
    expect(auth.isAuthenticated).toBe(false)

    client.logoutConsoleSession.mockRejectedValueOnce(new TypeError('offline'))
    await auth.login('admin', 'secret')
    await expect(auth.logoutAndRevoke({ timeoutMs: 20 })).resolves.toEqual({ status: 'failed' })
    expect(auth.isAuthenticated).toBe(false)

    client.logoutConsoleSession.mockReturnValueOnce(new Promise(() => undefined))
    await auth.login('admin', 'secret')
    await expect(auth.logoutAndRevoke({ timeoutMs: 1 })).resolves.toEqual({ status: 'timed-out' })
    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.test.auth')).toBeNull()
  })
})

describe('auth route helpers', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  it('sanitizes redirect paths without depending on an app module', () => {
    expect(sanitizeRedirectPath('/operations/task-001?tab=audit')).toBe(
      '/operations/task-001?tab=audit',
    )
    expect(sanitizeRedirectPath('//evil.test/x')).toBe('/')
    expect(sanitizeRedirectPath('https://evil.test/x')).toBe('/')
    expect(sanitizeRedirectPath('/login?redirect=/')).toBe('/')
  })

  it('installs an auth guard with injected store and login path', async () => {
    const { api } = createApi()
    const useAuthStore = createAuthStore({
      api,
      messages,
      storageKey: 'nerv-iip.test.auth',
      storeId: 'test-auth',
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { template: '<div />' }, meta: { requiresAuth: true } },
        { path: '/login', component: { template: '<div />' }, meta: { guestOnly: true } },
      ],
    })

    createAuthGuard({
      loginPath: '/login',
      useAuthStore,
    })(router)

    await router.push('/')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.redirect).toBe('/')

    const auth = useAuthStore()
    auth.$patch({ accessToken: 'access-token', principal })

    await router.push('/login?redirect=//evil.test/x')

    expect(router.currentRoute.value.path).toBe('/')
  })

  it('confines a user who must change password to the change-password page', async () => {
    const { api } = createApi()
    const useAuthStore = createAuthStore({
      api,
      messages,
      storageKey: 'nerv-iip.test.auth',
      storeId: 'test-auth',
    })
    const page = { template: '<div />' }
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: page, meta: { requiresAuth: true } },
        { path: '/iam/users', component: page, meta: { requiresAuth: true } },
        { path: '/change-password', component: page, meta: { requiresAuth: true } },
        { path: '/login', component: page, meta: { guestOnly: true } },
      ],
    })
    createAuthGuard({ loginPath: '/login', useAuthStore })(router)
    const auth = useAuthStore()
    auth.$patch({ accessToken: 'access-token', principal, passwordChangeRequired: true })

    await router.push('/iam/users')
    expect(router.currentRoute.value.path).toBe('/change-password')

    await router.push('/login')
    expect(router.currentRoute.value.path).toBe('/change-password')

    auth.$patch({ passwordChangeRequired: false })
    await router.push('/iam/users')
    expect(router.currentRoute.value.path).toBe('/iam/users')

    await router.push('/change-password')
    expect(router.currentRoute.value.path).toBe('/change-password')
  })

  it('clears auth and redirects unauthorized users to the injected login path', () => {
    const auth = {
      clearSession: vi.fn(),
    }
    const router = {
      currentRoute: {
        value: {
          fullPath: '/operations/task-001',
          path: '/operations/task-001',
        },
      },
      push: vi.fn(),
    } as unknown as Router

    handleUnauthorizedRedirect(auth, router, { loginPath: '/login' })

    expect(auth.clearSession).toHaveBeenCalledWith('api-unauthorized')
    expect(router.push).toHaveBeenCalledWith({
      path: '/login',
      query: { redirect: '/operations/task-001' },
    })
  })
})

describe('authenticated api client bootstrap', () => {
  it('wires session expiry and 401 handling through injected adapters', () => {
    const auth = {
      accessToken: 'access-token',
      clearSession: vi.fn(),
      setSessionExpiredHandler: vi.fn(),
    }
    const router = {
      currentRoute: {
        value: {
          fullPath: '/operations/task-001',
          path: '/operations/task-001',
        },
      },
      push: vi.fn(),
    } as unknown as Router
    const configureApiClient = vi.fn()

    configureAuthenticatedApiClient({
      auth,
      configureApiClient,
      localeProvider: () => 'zh-CN',
      loginPath: '/login',
      router,
    })

    expect(auth.setSessionExpiredHandler).toHaveBeenCalledWith(expect.any(Function))
    expect(configureApiClient).toHaveBeenCalledWith({
      accessTokenProvider: expect.any(Function),
      localeProvider: expect.any(Function),
      onUnauthorized: expect.any(Function),
    })

    const options = configureApiClient.mock.calls[0]?.[0]
    expect(options.accessTokenProvider()).toBe('access-token')
    expect(options.localeProvider()).toBe('zh-CN')

    options.onUnauthorized()

    expect(router.push).toHaveBeenCalledWith({
      path: '/login',
      query: { redirect: '/operations/task-001' },
    })
  })
})
