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
