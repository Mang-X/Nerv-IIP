import { describe, expect, it } from 'vitest'
import { createBusinessConsoleI18n, DEFAULT_LOCALE } from './index'

describe('business console i18n', () => {
  it('uses zh-CN as the default locale', () => {
    const i18n = createBusinessConsoleI18n()

    expect(i18n.global.locale.value).toBe(DEFAULT_LOCALE)
    expect(i18n.global.t('login.title')).toBe('登录')
  })

  it('keeps business navigation Chinese when en-US is requested', () => {
    const i18n = createBusinessConsoleI18n({ locale: 'en-US' })

    expect(i18n.global.t('nav.inventory')).toBe('库存')
    expect(i18n.global.t('routes.schedules')).toBe('MES 规则排程（过渡）')
  })
})
