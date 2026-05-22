import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import { createConsoleI18n } from '@/i18n'
import LoginForm from './LoginForm.vue'

describe('LoginForm', () => {
  function mountForm(options: Parameters<typeof mount>[1] = {}) {
    return mount(LoginForm, {
      ...options,
      global: {
        ...(options.global ?? {}),
        plugins: [
          createConsoleI18n(),
          ...(options.global?.plugins ?? []),
        ],
      },
    })
  }

  it('emits credentials when the form is submitted', async () => {
    const wrapper = mountForm()

    await wrapper.get('input[name="loginName"]').setValue(' admin ')
    await wrapper.get('input[name="password"]').setValue('Admin123!')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('submit')).toEqual([
      [
        {
          loginName: 'admin',
          password: 'Admin123!',
        },
      ],
    ])
  })

  it('disables inputs and submit while pending', () => {
    const wrapper = mountForm({ props: { pending: true } })

    expect(wrapper.get('input[name="loginName"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('input[name="password"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('renders an inline auth error', () => {
    const wrapper = mountForm({ props: { error: 'Invalid credentials.' } })

    expect(wrapper.get('[role="alert"]').text()).toContain('Invalid credentials.')
    expect(wrapper.get('input[name="loginName"]').attributes('aria-invalid')).toBe('true')
  })

  it('renders zh-CN labels by default', () => {
    const wrapper = mountForm()

    expect(wrapper.text()).toContain('登录')
    expect(wrapper.text()).toContain('登录名')
    expect(wrapper.text()).toContain('密码')
  })

  it('renders en-US labels when the locale is English', () => {
    const wrapper = mount(LoginForm, {
      global: {
        plugins: [createConsoleI18n({ locale: 'en-US' })],
      },
    })

    expect(wrapper.text()).toContain('Sign in')
    expect(wrapper.text()).toContain('Login name')
    expect(wrapper.text()).toContain('Password')
  })
})
