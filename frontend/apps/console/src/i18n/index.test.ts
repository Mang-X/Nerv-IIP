import { describe, expect, it } from 'vitest'
import { createConsoleI18n, DEFAULT_LOCALE } from './index'

describe('console i18n', () => {
  it('uses zh-CN as the default locale', () => {
    const i18n = createConsoleI18n()

    expect(i18n.global.locale.value).toBe(DEFAULT_LOCALE)
    expect(i18n.global.t('login.title')).toBe('登录')
  })

  it('renders English base messages when locale is en-US', () => {
    const i18n = createConsoleI18n({ locale: 'en-US' })

    expect(i18n.global.t('login.title')).toBe('Sign in')
    expect(i18n.global.t('nav.instances')).toBe('Instances')
  })
})
