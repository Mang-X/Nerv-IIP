import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import App from './App.vue'

describe('App', () => {
  it('renders the routed console content', () => {
    const wrapper = mount(App, {
      global: {
        stubs: {
          RouterView: {
            template: '<main data-testid="console-route">Console route</main>',
          },
        },
      },
    })

    expect(wrapper.get('[data-testid="console-route"]').text()).toBe('Console route')
  })
})
