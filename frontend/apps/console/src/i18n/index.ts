import { createI18n } from 'vue-i18n'
import { messages } from './messages'

export const DEFAULT_LOCALE = 'zh-CN'
export const SUPPORTED_LOCALES = ['zh-CN', 'en-US'] as const

export type SupportedLocale = typeof SUPPORTED_LOCALES[number]

export function isSupportedLocale(locale: string): locale is SupportedLocale {
  return SUPPORTED_LOCALES.includes(locale as SupportedLocale)
}

export function normalizeLocale(locale?: string): SupportedLocale {
  if (!locale) return DEFAULT_LOCALE

  if (isSupportedLocale(locale)) return locale

  const language = locale.split('-')[0]
  return SUPPORTED_LOCALES.find((supportedLocale) => supportedLocale.startsWith(`${language}-`))
    ?? DEFAULT_LOCALE
}

export function createConsoleI18n(options: { locale?: string } = {}) {
  return createI18n({
    legacy: false,
    locale: normalizeLocale(options.locale),
    fallbackLocale: DEFAULT_LOCALE,
    messages,
  })
}

export const i18n = createConsoleI18n()

export function getCurrentLocale(): SupportedLocale {
  return normalizeLocale(i18n.global.locale.value)
}

export function translate(key: string): string {
  return i18n.global.te(key) ? i18n.global.t(key) : key
}
