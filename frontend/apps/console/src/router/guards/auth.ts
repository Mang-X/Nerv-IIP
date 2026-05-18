import { useAuthStore } from '@/stores/auth'
import type { Router } from 'vue-router'

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
      const redirect = typeof to.query.redirect === 'string' ? to.query.redirect : '/'
      return redirect
    }

    return true
  })
}
