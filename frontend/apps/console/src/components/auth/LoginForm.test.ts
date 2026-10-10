import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import { createConsoleI18n } from '@/i18n'
import LoginForm from './LoginForm.vue'

describe('LoginForm', () => {
  // Regression: #4296 的加载读屏名称须为中文，并保留 pending 状态行为。
  it('shows a Chinese loading name only while pending and keeps submit disabled', async () => {
    const wrapper = mountForm({ props: { pending: true } })
    expect(wrapper.get('[role="status"]').attributes('aria-label')).toBe('加载中')
    expect(wrapper.get('[role="status"]').attributes('data-icon')).toBe('inline-start')
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
    await wrapper.setProps({ pending: false })
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeUndefined()
  })

  function mountForm(options: Parameters<typeof mount>[1] = {}) {
    return mount(LoginForm, {
      ...options,
      global: {
        ...(options.global ?? {}),
        plugins: [createConsoleI18n(), ...(options.global?.plugins ?? [])],
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

  it('does not disclose seeded administrator credentials in rendered login surfaces', () => {
    const renderTarget = document.createElement('div')
    const bodyChildrenBefore = new Set(document.body.children)
    document.body.append(renderTarget)

    let zhWrapper: ReturnType<typeof mountForm> | undefined
    let enWrapper: ReturnType<typeof mount> | undefined

    try {
      zhWrapper = mountForm({ attachTo: renderTarget })
      enWrapper = mount(LoginForm, {
        attachTo: renderTarget,
        global: {
          plugins: [createConsoleI18n({ locale: 'en-US' })],
        },
      })

      const renderedBodyRoots = Array.from(document.body.children).filter(
        (element) => !bodyChildrenBefore.has(element) && element !== renderTarget,
      )
      const renderedBodyElements = renderedBodyRoots.flatMap((element) => [
        element,
        ...element.querySelectorAll('*'),
      ])

      for (const wrapper of [zhWrapper, enWrapper]) {
        const forbidden = /admin|管理员|seeded/i
        const renderedRoot = wrapper.element as HTMLElement
        const renderedElements = [
          renderedRoot,
          ...renderedRoot.querySelectorAll('*'),
          ...renderedBodyElements,
        ]
        const renderedAttributeValues = renderedElements.flatMap((element) =>
          Array.from(element.attributes, (attribute) => attribute.value),
        )
        const renderedText = [
          wrapper.text(),
          ...renderedBodyRoots.map((element) => element.textContent ?? ''),
        ].join(' ')

        expect(renderedText).not.toMatch(forbidden)
        for (const value of renderedAttributeValues) {
          expect(value).not.toMatch(forbidden)
        }

        for (const input of Array.from(renderedRoot.querySelectorAll('input'))) {
          const inputElement = input as HTMLInputElement

          expect(inputElement.value).not.toMatch(forbidden)
          expect(inputElement.placeholder).not.toMatch(forbidden)
          for (const attribute of ['aria-label', 'aria-description', 'title']) {
            expect(inputElement.getAttribute(attribute) ?? '').not.toMatch(forbidden)
          }
        }
      }
    } finally {
      zhWrapper?.unmount()
      enWrapper?.unmount()
      renderTarget.remove()
    }
  })
})
