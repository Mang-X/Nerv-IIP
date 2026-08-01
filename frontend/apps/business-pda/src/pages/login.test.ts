import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const push = vi.fn()
const routeQuery = vi.hoisted(() => ({ value: {} as Record<string, string> }))
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => ({
    get query() {
      return routeQuery.value
    },
  }),
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
    login.mockReset()
    login.mockImplementation(async () => {})
    routeQuery.value = {}
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

  it('shows the error, stays on the page, and re-enables submit when login fails', async () => {
    login.mockRejectedValueOnce(new Error('账号密码错误或会话已过期。'))

    const wrapper = mount(LoginPage)
    await wrapper.get('input[name="loginName"]').setValue('op01')
    await wrapper.get('input[name="password"]').setValue('bad')
    await wrapper.get('form').trigger('submit.prevent')
    await flushPromises()

    expect(wrapper.text()).toContain('账号密码错误或会话已过期。')
    expect(push).not.toHaveBeenCalled()
    const submit = wrapper.get('button[type="submit"]')
    expect(submit.attributes('disabled')).toBeUndefined()
    expect(submit.text()).toBe('登录')
  })

  it.each([
    ['failed', '网络不可用，当前设备已退出，但服务器会话撤销失败'],
    ['timed-out', '服务器会话撤销超时，当前设备已安全退出'],
  ])('explains a %s remote logout outcome without blocking a new login', (status, message) => {
    routeQuery.value = { logout: status }

    const wrapper = mount(LoginPage)

    expect(wrapper.text()).toContain(message)
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeUndefined()
  })
})
