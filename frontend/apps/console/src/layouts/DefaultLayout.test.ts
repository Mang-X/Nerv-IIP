import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
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
        plugins: [createPinia()],
        stubs: {
          AppShell: AppShellStub,
        },
      },
      slots: {
        default: '<main>Console content</main>',
      },
    })

    expect(wrapper.getComponent(AppShellStub).props('navItems')).toEqual([
      { label: 'Instances', to: { name: '/' } },
      {
        label: 'IAM',
        children: [
          { label: 'Users', to: { name: '/iam/users/' } },
          { label: 'Roles', to: { name: '/iam/roles/' } },
          { label: 'Sessions', to: { name: '/iam/sessions/' } },
        ],
      },
    ])
  })
})
