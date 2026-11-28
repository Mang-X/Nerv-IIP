import { useAuthStore } from '@/stores/auth'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { installAuthGuard } from './auth'

describe('business console auth route guard', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  function createGuardedRouter() {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { template: '<div />' }, meta: { requiresAuth: true } },
        ...[
          ['/erp', { requiredPermissions: ['business.erp.procurement.read'] }],
          ['/erp/finance', { requiredPermissions: ['business.erp.finance.read'] }],
          [
            '/erp/procurement/purchase-orders',
            { requiredPermissions: ['business.erp.procurement.read'] },
          ],
          ['/wms', { domainIndex: true }],
          ['/wms/inbound', { requiredPermissions: ['business.wms.receipts.read'] }],
          ['/wms/picking', { requiredPermissions: ['business.wms.shipments.read'] }],
          ['/engineering', { domainIndex: true }],
          [
            '/engineering/routings',
            { requiredPermissions: ['business.engineering.routings.read'] },
          ],
        ].map(([path, meta]) => ({
          path: path as string,
          component: { template: '<div />' },
          meta: { ...(meta as object), requiresAuth: true },
        })),
        {
          path: '/inventory/availability',
          component: { template: '<div />' },
          meta: { requiresAuth: true, requiredPermissions: ['business.inventory.ledger.read'] },
        },
        {
          path: '/forbidden',
          component: { template: '<div />' },
          meta: { requiresAuth: true, title: '无权限' },
        },
        { path: '/login', component: { template: '<div />' }, meta: { guestOnly: true } },
      ],
    })
    installAuthGuard(router)
    return router
  }

  it('redirects unauthenticated users to login', async () => {
    const router = createGuardedRouter()

    await router.push('/')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.redirect).toBe('/')
  })

  it('redirects authenticated users away from login', async () => {
    const router = createGuardedRouter()
    const auth = useAuthStore()
    auth.$patch({
      accessToken: 'access-token',
      principal: {
        principalId: 'user-admin',
        principalType: 'user',
        loginName: 'admin',
        email: 'admin@nerv-iip.local',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionVersion: 1,
      },
    })

    await router.push('/login')

    expect(router.currentRoute.value.path).toBe('/')
  })

  it('redirects authenticated users away from routes without required permissions', async () => {
    const router = createGuardedRouter()
    const auth = useAuthStore()
    auth.$patch({
      accessToken: 'access-token',
      principal: {
        principalId: 'user-warehouse',
        principalType: 'user',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionVersion: 1,
        permissionCodes: ['business.wms.receipts.read'],
      },
    })

    await router.push('/inventory/availability')

    expect(router.currentRoute.value.path).toBe('/forbidden')
    expect(router.currentRoute.value.query.from).toBe('/inventory/availability')
  })

  it('allows authenticated users into routes with any matching required permission', async () => {
    const router = createGuardedRouter()
    const auth = useAuthStore()
    auth.$patch({
      accessToken: 'access-token',
      principal: {
        principalId: 'user-inventory',
        principalType: 'user',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionVersion: 1,
        permissionCodes: ['business.inventory.ledger.read'],
      },
    })

    await router.push('/inventory/availability')

    expect(router.currentRoute.value.path).toBe('/inventory/availability')
  })

  it.each([
    ['/wms', ['business.wms.shipments.read'], '/wms/picking'],
    ['/wms', ['business.wms.receipts.read', 'business.wms.shipments.read'], '/wms/inbound'],
    ['/engineering', ['business.engineering.routings.read'], '/engineering/routings'],
    ['/engineering', ['business.wms.shipments.read'], '/forbidden'],
  ])('opens the first permitted page for %s with %j', async (root, permissionCodes, expected) => {
    const router = createGuardedRouter()
    useAuthStore().$patch({
      accessToken: 'token',
      principal: { principalId: 'user', permissionCodes },
    })

    await router.push(root)

    expect(router.currentRoute.value.path).toBe(expected)
  })

  it.each(['/', '/login', '/erp'])(
    'opens finance from %s even with procurement reads',
    async (path) => {
      const router = createGuardedRouter()
      useAuthStore().$patch({
        accessToken: 'token',
        principal: {
          roleIds: ['role-erp-finance'],
          permissionCodes: ['business.erp.finance.read', 'business.erp.procurement.read'],
        },
      })

      await router.push(path)
      expect(router.currentRoute.value.path).toBe('/erp/finance')
      await router.push('/erp/procurement/purchase-orders')
      expect(router.currentRoute.value.path).toBe('/erp/procurement/purchase-orders')
      await router.push('/erp?keyword=PR-001')
      expect(router.currentRoute.value.path).toBe('/erp')
      expect(router.currentRoute.value.query.keyword).toBe('PR-001')
    },
  )

  it('sanitizes guest redirect targets', async () => {
    const router = createGuardedRouter()
    const auth = useAuthStore()
    auth.$patch({
      accessToken: 'access-token',
      principal: {
        principalId: 'user-admin',
      },
    })

    await router.push('/login?redirect=//evil.test/x')

    expect(router.currentRoute.value.path).toBe('/')
  })
})
