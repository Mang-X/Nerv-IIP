import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { createConsoleI18n } from '@/i18n'
import LoginPage from './login.vue'

const api = vi.hoisted(() => ({
  getConsoleMe: vi.fn(),
  loginConsole: vi.fn(),
  logoutConsole: vi.fn(),
  refreshConsole: vi.fn(),
}))

const router = vi.hoisted(() => ({
  push: vi.fn(),
}))

vi.mock('@/api/auth', () => api)

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()

  return {
    ...actual,
    useRoute: () => ({ query: {} }),
    useRouter: () => router,
  }
})

describe('Login page', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('shows auth failure inline without surfacing an app error', async () => {
    api.loginConsole.mockRejectedValue(new Error('Invalid credentials or expired session.'))
    const appError = vi.fn()
    const wrapper = mount(LoginPage, {
      global: {
        config: {
          errorHandler: appError,
        },
        plugins: [createPinia(), createConsoleI18n({ locale: 'en-US' })],
      },
    })

    await wrapper.get('input[name="loginName"]').setValue('admin')
    await wrapper.get('input[name="password"]').setValue('wrong-password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain(
      'Invalid credentials or expired session.',
    )
    expect(appError).not.toHaveBeenCalled()
    expect(router.push).not.toHaveBeenCalled()
  })
})
