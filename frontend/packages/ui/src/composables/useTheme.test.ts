import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'

import {
  ACCENT_STORAGE_KEY,
  DEFAULT_THEME,
  initTheme,
  NEUTRAL_THEME,
  THEME_PRESETS,
  THEME_STORAGE_KEY,
  useTheme,
} from './useTheme'

// The `vp test` runner's jsdom does not wire up a real Web Storage (Node's
// experimental localStorage needs --localstorage-file). Install a minimal
// in-memory shim so persistence + initTheme() migration paths are testable.
class MemoryStorage implements Storage {
  private map = new Map<string, string>()
  get length() {
    return this.map.size
  }
  clear(): void {
    this.map.clear()
  }
  getItem(key: string): string | null {
    return this.map.has(key) ? this.map.get(key)! : null
  }
  key(index: number): string | null {
    return Array.from(this.map.keys())[index] ?? null
  }
  removeItem(key: string): void {
    this.map.delete(key)
  }
  setItem(key: string, value: string): void {
    this.map.set(key, String(value))
  }
}

beforeAll(() => {
  if (typeof globalThis.localStorage === 'undefined') {
    const store = new MemoryStorage()
    Object.defineProperty(globalThis, 'localStorage', { value: store, configurable: true })
    Object.defineProperty(window, 'localStorage', { value: store, configurable: true })
  }
})

function htmlStyle() {
  return document.documentElement.style
}

beforeEach(() => {
  localStorage.clear()
  // Wipe any inline overrides between tests.
  document.documentElement.removeAttribute('style')
})

afterEach(() => {
  localStorage.clear()
  document.documentElement.removeAttribute('style')
})

describe('useTheme — runtime primary theming', () => {
  it('defaults to the neutral theme (no inline primary overrides)', () => {
    const { theme } = useTheme()
    expect(theme.value).toBe(DEFAULT_THEME)
    expect(DEFAULT_THEME).toBe(NEUTRAL_THEME)
    // Neutral must NOT pin a colour — it falls back to theme.css light/dark.
    expect(htmlStyle().getPropertyValue('--primary')).toBe('')
  })

  it('inlines primary / foreground / ring / sidebar-primary / nv-brand for a colour preset', async () => {
    const { setTheme } = useTheme()
    setTheme('blue')
    await nextTick()

    const blue = THEME_PRESETS.blue
    expect(htmlStyle().getPropertyValue('--primary')).toBe(blue.primary)
    expect(htmlStyle().getPropertyValue('--primary-foreground')).toBe(blue.foreground)
    expect(htmlStyle().getPropertyValue('--ring')).toBe(blue.primary)
    expect(htmlStyle().getPropertyValue('--sidebar-primary')).toBe(blue.primary)
    // ADR 0020 §3: the emphasis accent is namespaced `--nv-brand`; the theme.css
    // alias `--brand: var(--nv-brand)` keeps legacy `var(--brand)` refs tracking.
    expect(htmlStyle().getPropertyValue('--nv-brand')).toBe(blue.primary)
  })

  it('clears the inline overrides when switching back to neutral', async () => {
    const { setTheme } = useTheme()
    setTheme('green')
    await nextTick()
    expect(htmlStyle().getPropertyValue('--primary')).toBe(THEME_PRESETS.green.primary)

    setTheme(NEUTRAL_THEME)
    await nextTick()
    // Cleared → cascade falls back to theme.css :root/.dark values.
    expect(htmlStyle().getPropertyValue('--primary')).toBe('')
    expect(htmlStyle().getPropertyValue('--primary-foreground')).toBe('')
    expect(htmlStyle().getPropertyValue('--ring')).toBe('')
    expect(htmlStyle().getPropertyValue('--sidebar-primary')).toBe('')
  })

  it('persists the choice and re-applies it via initTheme()', async () => {
    const { setTheme } = useTheme()
    setTheme('rose')
    await nextTick()
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('rose')

    document.documentElement.removeAttribute('style')
    initTheme()
    expect(htmlStyle().getPropertyValue('--primary')).toBe(THEME_PRESETS.rose.primary)
  })

  it('migrates a legacy nerv-iip-accent value to the matching theme', () => {
    localStorage.setItem(ACCENT_STORAGE_KEY, THEME_PRESETS.violet.primary)
    initTheme()
    expect(htmlStyle().getPropertyValue('--primary')).toBe(THEME_PRESETS.violet.primary)
  })
})
