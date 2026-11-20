import { useAuthStore } from '@/stores/auth'
import { createAuthGuard } from '@nerv-iip/auth'
import type { RouteMeta, Router } from 'vue-router'

const installBaseAuthGuard = createAuthGuard({
  loginPath: '/login',
  useAuthStore,
})

export function hasRoutePermission(meta: RouteMeta, permissionCodes: readonly string[]) {
  return (
    !meta.requiresAuth ||
    !meta.requiredPermissions?.length ||
    meta.requiredPermissions.some((permission) => permissionCodes.includes(permission))
  )
}

export function installAuthGuard(router: Router) {
  installBaseAuthGuard(router)

  router.beforeEach((to) => {
    const auth = useAuthStore()
    if (
      !auth.isAuthenticated ||
      hasRoutePermission(to.meta, auth.principal?.permissionCodes ?? [])
    ) {
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
