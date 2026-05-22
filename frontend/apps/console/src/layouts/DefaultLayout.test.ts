import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
import { createConsoleI18n } from '@/i18n'
import DefaultLayout from './DefaultLayout.vue'

const AppShellStub = defineComponent({
  name: 'AppShell',
  props: {
    navItems: {
      required: true,
      type: Array,
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
  template: '<section><slot /></section>',
})

describe('DefaultLayout', () => {
  it('passes Vue Router route locations to AppShell navigation', () => {
    const wrapper = mount(DefaultLayout, {
      global: {
        plugins: [createPinia(), createConsoleI18n({ locale: 'en-US' })],
        stubs: {
          AppShell: AppShellStub,
        },
      },
      slots: {
        default: '<main>Console content</main>',
      },
    })

    const navItems = wrapper.getComponent(AppShellStub).props('navItems') as Record<string, unknown>[]
    expect(navItems[0]).toMatchObject({ title: 'Instances', to: { name: '/' } })
    expect(navItems[1]).toMatchObject({ title: 'Notifications', to: { path: '/notifications' } })
    expect(navItems[2]).toMatchObject({
      title: 'IAM',
      isActive: true,
      items: [
        { title: 'Users', to: { path: '/iam/users' } },
        { title: 'Roles', to: { path: '/iam/roles' } },
        { title: 'Sessions', to: { path: '/iam/sessions' } },
      ],
    })
  })
})
