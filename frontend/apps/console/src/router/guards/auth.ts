import { useAuthStore } from '@/stores/auth'
import type { Router } from 'vue-router'
import { sanitizeRedirectPath } from '../redirects'

declare module 'vue-router' {
  interface RouteMeta {
    guestOnly?: boolean
    requiresAuth?: boolean
    title?: string
  }
}

export function installAuthGuard(router: Router) {
  router.beforeEach(async (to) => {
    const auth = useAuthStore()

    if (auth.restoreStatus === 'idle') {
      await auth.restoreSession()
    }

    if (to.meta.requiresAuth && !auth.isAuthenticated) {
      return {
        path: '/login',
        query: {
          redirect: to.fullPath,
        },
      }
    }

    if (to.meta.guestOnly && auth.isAuthenticated) {
      return sanitizeRedirectPath(to.query.redirect)
    }

    return true
  })
}
