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
