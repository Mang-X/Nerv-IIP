import { useAuthStore } from '@/stores/auth'
import { createAuthGuard } from '@nerv-iip/auth'
import type { Router } from 'vue-router'
import { canAccess, firstConsolePage } from '../access'

const installSessionGuard = createAuthGuard({
  loginPath: '/login',
  useAuthStore,
})

export function installAuthGuard(router: Router) {
  installSessionGuard(router)
  router.beforeEach((to) => {
    const auth = useAuthStore()
    if (!auth.isAuthenticated || !to.meta.requiredPermissions) return true

    const permissions = auth.principal?.permissionCodes
    if (canAccess(permissions, to.meta.requiredPermissions)) return true

    return to.path === '/' ? firstConsolePage(permissions) : '/forbidden'
  })
}
