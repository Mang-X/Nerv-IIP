import type { Router } from 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    guestOnly?: boolean
    requiresAuth?: boolean
    title?: string
  }
}

export interface AuthRouteStore {
  isAuthenticated: boolean
  restoreSession: () => Promise<void>
  restoreStatus: 'idle' | 'restoring' | 'restored' | 'failed'
}

export interface AuthSessionController {
  clearSession: (reason: string) => void
}

export interface CreateAuthGuardOptions<TStore extends AuthRouteStore> {
  defaultRedirectPath?: string
  loginPath: string
  useAuthStore: () => TStore
}

export interface RedirectPathOptions {
  defaultRedirectPath?: string
  loginPath?: string
}

export interface UnauthorizedRedirectOptions {
  loginPath: string
}

export function createAuthGuard<TStore extends AuthRouteStore>(
  options: CreateAuthGuardOptions<TStore>,
) {
  const { defaultRedirectPath = '/', loginPath, useAuthStore } = options

  return function installAuthGuard(router: Router) {
    router.beforeEach(async (to) => {
      const auth = useAuthStore()

      if (auth.restoreStatus === 'idle') {
        await auth.restoreSession()
      }

      if (to.meta.requiresAuth && !auth.isAuthenticated) {
        return {
          path: loginPath,
          query: {
            redirect: to.fullPath,
          },
        }
      }

      if (to.meta.guestOnly && auth.isAuthenticated) {
        return sanitizeRedirectPath(to.query.redirect, {
          defaultRedirectPath,
          loginPath,
        })
      }

      return true
    })
  }
}

export function sanitizeRedirectPath(value: unknown, options: RedirectPathOptions = {}): string {
  const { defaultRedirectPath = '/', loginPath = '/login' } = options

  if (typeof value !== 'string') {
    return defaultRedirectPath
  }

  if (!value.startsWith('/') || value.startsWith('//')) {
    return defaultRedirectPath
  }

  if (
    value === loginPath ||
    value.startsWith(`${loginPath}?`) ||
    value.startsWith(`${loginPath}#`)
  ) {
    return defaultRedirectPath
  }

  return value
}

export function handleUnauthorizedRedirect(
  auth: AuthSessionController,
  router: Router,
  options: UnauthorizedRedirectOptions,
) {
  auth.clearSession('api-unauthorized')

  const currentRoute = router.currentRoute.value
  if (currentRoute.path === options.loginPath) {
    return
  }

  void router.push({
    path: options.loginPath,
    query: {
      redirect: currentRoute.fullPath,
    },
  })
}
