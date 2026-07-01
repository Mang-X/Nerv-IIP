import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import ForbiddenPage from './forbidden.vue'

const routeState = vi.hoisted(() => ({
  query: {} as Record<string, unknown>,
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  return {
    ...actual,
    useRoute: () => ({ query: routeState.query }),
    RouterLink: {
      props: ['to'],
      template: '<a :data-to="to"><slot /></a>',
    },
  }
})

describe('ForbiddenPage', () => {
  it('sanitizes the retry target before rendering the router link', () => {
    routeState.query = { from: '//evil.test/x' }

    const wrapper = mount(ForbiddenPage, {
      global: {
        stubs: {
          BusinessLayout: { template: '<main><slot /></main>' },
          Button: { template: '<button><slot /></button>' },
        },
      },
    })

    const retryLink = wrapper.findAll('a').find((link) => link.text() === '重试原页面')

    expect(retryLink?.attributes('data-to')).toBe('/')
  })
})
