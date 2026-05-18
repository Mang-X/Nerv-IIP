import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import LoginForm from './LoginForm.vue'

describe('LoginForm', () => {
  it('emits credentials when the form is submitted', async () => {
    const wrapper = mount(LoginForm)

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
    const wrapper = mount(LoginForm, { props: { pending: true } })

    expect(wrapper.get('input[name="loginName"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('input[name="password"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('renders an inline auth error', () => {
    const wrapper = mount(LoginForm, { props: { error: 'Invalid credentials.' } })

    expect(wrapper.get('[role="alert"]').text()).toContain('Invalid credentials.')
    expect(wrapper.get('input[name="loginName"]').attributes('aria-invalid')).toBe('true')
  })
})
