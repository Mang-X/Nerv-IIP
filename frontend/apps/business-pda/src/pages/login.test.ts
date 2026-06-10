import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const push = vi.fn()
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({ query: {} }),
}))

const login = vi.fn(async () => {})
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ login, isAuthenticated: false }),
}))

import LoginPage from './login.vue'

describe('PDA login page', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    login.mockClear()
  })

  it('logs in and navigates home on submit', async () => {
    const wrapper = mount(LoginPage)
    await wrapper.get('input[name="loginName"]').setValue('op01')
    await wrapper.get('input[name="password"]').setValue('pw')
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()

    expect(login).toHaveBeenCalledWith('op01', 'pw')
    expect(push).toHaveBeenCalledWith('/')
  })
})
