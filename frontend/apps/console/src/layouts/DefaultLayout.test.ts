import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
import { createConsoleI18n } from '@/i18n'
import { useAuthStore } from '@/stores/auth'
import DefaultLayout from './DefaultLayout.vue'

const AppShellTStub = defineComponent({
  name: 'AppShellT',
  props: {
    topDomains: { required: true, type: Array },
    currentDomainId: { required: false, type: String },
    sideNav: { required: false, type: Array },
    title: { required: true, type: String },
    user: { required: false, type: Object },
    signOutLabel: { required: false, type: String },
  },
  template: '<section><slot /></section>',
})

function mountLayout(plugins: unknown[]) {
  return mount(DefaultLayout, {
    global: {
      plugins: plugins as never,
      stubs: { AppShellT: AppShellTStub },
    },
    slots: { default: '<main>Console content</main>' },
  })
}

describe('DefaultLayout', () => {
  it('passes Vue Router route locations to the T-nav top domains', () => {
    const pinia = createPinia()
    useAuthStore(pinia).$patch({
      principal: { permissionCodes: ['apphub.instances.read', 'notifications.messages.read'] },
    })
    const wrapper = mountLayout([pinia, createConsoleI18n({ locale: 'en-US' })])

    const topDomains = wrapper.getComponent(AppShellTStub).props('topDomains') as Record<
      string,
      unknown
    >[]
    expect(topDomains[0]).toMatchObject({ id: 'instances', title: 'Instances', to: { path: '/' } })
    expect(topDomains[1]).toMatchObject({
      id: 'notifications',
      title: 'Notifications',
      to: { path: '/notifications' },
    })
    expect(topDomains[2]).toMatchObject({
      id: 'business',
      title: 'Business',
      to: { path: '/business' },
    })
    expect(topDomains).toHaveLength(3)
  })

  it('shows only readable IAM sections and opens the first one', async () => {
    const pinia = createPinia()
    useAuthStore(pinia).$patch({
      principal: { permissionCodes: ['iam.roles.read', 'iam.sessions.read'] },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/iam/roles', component: { template: '<div />' } }],
    })
    await router.push('/iam/roles')
    const wrapper = mountLayout([pinia, router, createConsoleI18n({ locale: 'en-US' })])
    const shell = wrapper.getComponent(AppShellTStub)

    expect(shell.props('topDomains')).toContainEqual(
      expect.objectContaining({
        id: 'iam',
        to: { path: '/iam/roles' },
      }),
    )
    expect(shell.props('sideNav')).toEqual([
      {
        items: [
          expect.objectContaining({ to: { path: '/iam/roles' } }),
          expect.objectContaining({ to: { path: '/iam/sessions' } }),
        ],
      },
    ])
  })

  it('uses a dedicated translated fallback for authenticated user display name', () => {
    const pinia = createPinia()
    const auth = useAuthStore(pinia)
    auth.$patch({
      principal: {
        principalType: 'user',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })

    const wrapper = mountLayout([pinia, createConsoleI18n({ locale: 'en-US' })])

    expect(wrapper.getComponent(AppShellTStub).props('user')).toMatchObject({
      name: 'Authenticated user',
    })
  })
})
