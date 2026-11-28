import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { RouterView, createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'

import { installAuthGuard } from '@/router/guards/auth'
import { useAuthStore } from '@/stores/auth'
import BusinessStatusPage from './index.vue'

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  listConsoleInstances: vi.fn(async () => ({
    data: {
      success: true,
      data: {
        totalCount: 1,
        items: [
          {
            applicationKey: 'business-mes',
            instanceKey: 'mes-01',
            reportedStatus: 'running',
            healthStatus: 'healthy',
          },
        ],
      },
    },
  })),
}))

describe('Business status page', () => {
  async function mountAuthenticatedRoute() {
    const pinia = createPinia()
    const auth = useAuthStore(pinia)
    auth.$patch({
      accessToken: 'access-token',
      principal: {
        principalId: 'user-admin',
        principalType: 'user',
        loginName: 'admin',
        organizationId: 'org-business-test',
        environmentId: 'env-business-test',
      },
    })

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/business', component: BusinessStatusPage, meta: { requiresAuth: true } },
        { path: '/login', component: { template: '<div />' }, meta: { guestOnly: true } },
      ],
    })
    installAuthGuard(router)
    await router.push('/business')

    const wrapper = mount(RouterView, {
      global: {
        plugins: [pinia, router, PiniaColada],
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()
    return wrapper
  }

  it('renders the Business platform status after authenticated navigation', async () => {
    const wrapper = await mountAuthenticatedRoute()

    expect(wrapper.text()).toContain('业务平台状态')
    expect(wrapper.text()).toContain('org-business-test · env-business-test')
    expect(wrapper.text()).not.toContain('已交付')
  })

  it('does not leak engineering issue numbers into the UI', async () => {
    const wrapper = await mountAuthenticatedRoute()

    expect(wrapper.text()).not.toMatch(/#\d+/)
  })

  it('shows instance observations and marks unmatched services as unobserved', async () => {
    const wrapper = await mountAuthenticatedRoute()

    expect(wrapper.text()).toContain('仓储')
    expect(wrapper.text()).toContain('未观测')
    expect(wrapper.text()).toContain('运行中')
    expect(wrapper.text()).toContain('健康')
    expect(wrapper.get('a[href="/?instance=mes-01"]').text()).toBe('mes-01')
  })
})
