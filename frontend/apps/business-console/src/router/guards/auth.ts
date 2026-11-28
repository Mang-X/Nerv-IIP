import { BUSINESS_DOMAINS, resolveBusinessEntry, resolveDomainId } from '@/navigation'
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

    const { permissionCodes: codes, roleIds } = auth.principal ?? {}
    const canOpen = (path: string) => {
      const page = router.resolve(path)
      return page.matched.length > 0 && hasRoutePermission(page.meta, codes ?? [])
    }
    const allowed = hasRoutePermission(to.meta, codes ?? [])
    if (to.path === '/') {
      const entry = resolveBusinessEntry(roleIds, codes, undefined, canOpen)
      if (entry === '/') return true
      return entry ? { path: entry, replace: true } : { path: '/forbidden' }
    }
    const path = to.path.replace(/\/$/, '')
    // 采购申请页同时承担经营域根入口。带单据检索条件的跳转仍指向采购申请。
    if (
      allowed &&
      path === '/erp' &&
      typeof to.query.keyword === 'string' &&
      to.query.keyword.trim()
    ) {
      return true
    }
    const isDomainRoot = BUSINESS_DOMAINS.some(({ id }) => path === `/${id}`)
    if (to.meta.domainIndex || isDomainRoot) {
      const entry = resolveBusinessEntry(roleIds, codes, resolveDomainId(path), canOpen)
      if (entry === path && allowed) return true
      if (entry) return { path: entry, query: to.query, hash: to.hash, replace: true }
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
