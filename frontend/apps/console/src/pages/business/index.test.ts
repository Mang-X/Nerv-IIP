import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { RouterView, createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it } from 'vitest'

import { installAuthGuard } from '@/router/guards/auth'
import { useAuthStore } from '@/stores/auth'
import BusinessStatusPage from './index.vue'

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
        plugins: [pinia, router],
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

  it('renders the Business MVP status after authenticated navigation', async () => {
    const wrapper = await mountAuthenticatedRoute()

    expect(wrapper.text()).toContain('Business MVP status')
    expect(wrapper.findAll('[data-test="business-service"]')).toHaveLength(12)
    expect(wrapper.text()).toContain('#77 Full-chain acceptance')
    expect(wrapper.text()).toContain('#78 Gantt/RFC archived')
  })

  it('lists the delivered business backend services', async () => {
    const wrapper = await mountAuthenticatedRoute()

    for (const serviceName of [
      'BusinessMasterData',
      'BusinessProductEngineering',
      'BusinessInventory',
      'BusinessQuality',
      'BusinessMES',
      'BusinessDemandPlanning',
      'BarcodeLabel',
      'BusinessApproval',
      'WMS',
      'BusinessIndustrialTelemetry',
      'BusinessMaintenance',
      'BusinessERP',
    ]) {
      expect(wrapper.text()).toContain(serviceName)
    }
  })
})
