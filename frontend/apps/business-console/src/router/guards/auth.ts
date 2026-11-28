import { resolveDomainId, sideNavForDomain } from '@/navigation'
import { useAuthStore } from '@/stores/auth'
import { createAuthGuard } from '@nerv-iip/auth'
import type { RouteMeta, Router } from 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    domainIndex?: boolean
  }
}

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
    if (!auth.isAuthenticated) return true

    const codes = auth.principal?.permissionCodes
    const allowed = hasRoutePermission(to.meta, codes ?? [])
    if (
      to.meta.domainIndex ||
      (!allowed && ['/erp', '/mes', '/planning', '/equipment'].includes(to.path.replace(/\/$/, '')))
    ) {
      for (const { items } of sideNavForDomain(resolveDomainId(to.path), codes)) {
        for (const { to: target } of items) {
          const page = router.resolve(target)
          if (
            page.path !== to.path &&
            page.matched.length &&
            hasRoutePermission(page.meta, codes ?? [])
          ) {
            return {
              path: page.path,
              query: { ...to.query, ...page.query },
              hash: page.hash || to.hash,
              replace: true,
            }
          }
        }
      }
    } else if (allowed) {
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
