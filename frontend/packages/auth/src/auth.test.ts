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
  invalidCredentialsOrExpiredSession: 'Bad credentials.',
  loginFallback: 'Login failed.',
  refreshFallback: 'Refresh failed.',
  principalFallback: 'Principal failed.',
  invalidSession: 'Invalid session.',
  loginFailed: 'Unable to sign in.',
  unknownUser: 'Unknown user',
}

function createClient() {
  return {
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
