import { useAuthStore } from '@/stores/auth'
import { createAuthGuard } from '@nerv-iip/auth'
import type { Router } from 'vue-router'

const installBaseAuthGuard = createAuthGuard({
  loginPath: '/login',
  useAuthStore,
})

export function installAuthGuard(router: Router) {
  installBaseAuthGuard(router)

  router.beforeEach((to) => {
    const auth = useAuthStore()
    const requiredPermissions = to.meta.requiredPermissions

    if (!to.meta.requiresAuth || !auth.isAuthenticated || !requiredPermissions?.length) {
      return true
    }

    const permissionCodes = auth.principal?.permissionCodes ?? []
    if (requiredPermissions.some((permission) => permissionCodes.includes(permission))) {
      return true
    }

    if (to.path === '/forbidden') {
      return true
    }

    return {
      path: '/forbidden',
      query: {
        from: to.fullPath,
      },
    }
  })
}
