import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { createBusinessConsoleI18n } from '@/i18n'
import { useAuthStore } from '@/stores/auth'
import BusinessLayout from './BusinessLayout.vue'

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ path: '/inventory/availability', meta: { title: 'routes.availability' } }),
    useRouter: () => ({ push: vi.fn() }),
  }
})

const AppShellStub = defineComponent({
  name: 'AppShell',
  props: {
    navItems: {
      required: true,
      type: Array,
    },
    navLabel: {
      required: false,
      type: String,
    },
    title: {
      required: true,
      type: String,
    },
    user: {
      required: false,
      type: Object,
    },
  },
  template: '<section><slot name="header" /><slot /></section>',
})

describe('BusinessLayout', () => {
  it('passes business domain navigation to AppShell', () => {
    const wrapper = mount(BusinessLayout, {
      global: {
        plugins: [createPinia(), createBusinessConsoleI18n({ locale: 'en-US' })],
        stubs: {
          AppShell: AppShellStub,
        },
      },
      slots: {
        default: '<main>Business content</main>',
      },
    })

    const navItems = wrapper.getComponent(AppShellStub).props('navItems') as Record<string, unknown>[]
    expect(wrapper.getComponent(AppShellStub).props('title')).toBe('Nerv-IIP Business')
    expect(wrapper.getComponent(AppShellStub).props('navLabel')).toBe('Business')
    expect(navItems).toMatchObject([
      { title: 'MasterData', items: [{ title: 'SKUs', to: { path: '/master-data/skus' } }] },
      {
        title: 'Inventory',
        isActive: true,
        items: [
          { title: 'Availability', to: { path: '/inventory/availability' } },
          { title: 'Movements', to: { path: '/inventory/movements' } },
          { title: 'Counts', to: { path: '/inventory/counts' } },
        ],
      },
      {
        title: 'Quality',
        items: [
          { title: 'Inspections', to: { path: '/quality/inspections' } },
          { title: 'NCRs', to: { path: '/quality/ncrs' } },
        ],
      },
      {
        title: 'MES',
        items: [
          { title: 'Work orders', to: { path: '/mes/work-orders' } },
          { title: 'Schedules', to: { path: '/mes/schedules' } },
        ],
      },
    ])
  })

  it('uses a fallback for authenticated user display name', () => {
    const pinia = createPinia()
    const auth = useAuthStore(pinia)
    auth.$patch({
      principal: {
        principalType: 'user',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })

    const wrapper = mount(BusinessLayout, {
      global: {
        plugins: [pinia, createBusinessConsoleI18n({ locale: 'en-US' })],
        stubs: {
          AppShell: AppShellStub,
        },
      },
    })

    expect(wrapper.getComponent(AppShellStub).props('user')).toMatchObject({
      name: 'Authenticated user',
    })
  })
})
