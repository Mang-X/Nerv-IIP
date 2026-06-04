/**
 * Design System v2 — runtime theme controls (@nerv-iip/ui).
 *
 * Two independent, persisted dimensions:
 *   1. Colour mode: light / dark  → toggles the `.dark` class on <html>.
 *   2. Dynamic accent: writes `--brand` on <html> so the brand/emphasis colour
 *      can be re-selected at runtime without rebuilding tokens.
 *
 * FE-1 ships the MECHANISM + composables only. Switcher UI / nav controls live
 * in FE-2 / FE-3. Both apps call `initTheme()` once in main.ts before mount so
 * the persisted choice is applied before first paint.
 */
import { useStorage } from '@vueuse/core'
import { computed, watch } from 'vue'

export type ColorMode = 'light' | 'dark'

export const COLOR_MODE_STORAGE_KEY = 'nerv-iip-color-mode'
export const ACCENT_STORAGE_KEY = 'nerv-iip-accent'

/** Default brand accent — must match `--brand` in styles/theme.css. */
export const DEFAULT_ACCENT = 'oklch(0.55 0.18 255)'

/** Curated runtime accents (oklch) for a colour picker — emphasis hues only. */
export const ACCENT_PRESETS: Record<string, string> = {
  blue: 'oklch(0.55 0.18 255)',
  violet: 'oklch(0.55 0.2 295)',
  teal: 'oklch(0.6 0.12 195)',
  green: 'oklch(0.62 0.17 149)',
  amber: 'oklch(0.72 0.16 70)',
  rose: 'oklch(0.6 0.21 15)',
}

function hasDom(): boolean {
  return typeof document !== 'undefined'
}

function applyColorMode(mode: ColorMode): void {
  if (hasDom()) document.documentElement.classList.toggle('dark', mode === 'dark')
}

function applyAccent(value: string): void {
  if (hasDom()) document.documentElement.style.setProperty('--brand', value)
}

function preferredMode(): ColorMode {
  if (hasDom() && window.matchMedia?.('(prefers-color-scheme: dark)').matches) return 'dark'
  return 'light'
}

/**
 * Apply the persisted colour mode + accent immediately. Call once in main.ts
 * (outside a component) before mounting the app.
 */
export function initTheme(): void {
  if (!hasDom()) return
  const storedMode = localStorage.getItem(COLOR_MODE_STORAGE_KEY)
  const mode: ColorMode =
    storedMode === 'dark' || storedMode === 'light' ? storedMode : preferredMode()
  applyColorMode(mode)
  applyAccent(localStorage.getItem(ACCENT_STORAGE_KEY) ?? DEFAULT_ACCENT)
}

/** Reactive light/dark control, persisted across sessions. */
export function useColorMode() {
  const mode = useStorage<ColorMode>(COLOR_MODE_STORAGE_KEY, preferredMode())
  watch(mode, applyColorMode, { immediate: true })

  const isDark = computed(() => mode.value === 'dark')
  function setMode(next: ColorMode): void {
    mode.value = next
  }
  function toggle(): void {
    mode.value = mode.value === 'dark' ? 'light' : 'dark'
  }

  return { mode, isDark, setMode, toggle }
}

/** Reactive dynamic accent (`--brand`), persisted across sessions. */
export function useThemeAccent() {
  const accent = useStorage<string>(ACCENT_STORAGE_KEY, DEFAULT_ACCENT)
  watch(accent, applyAccent, { immediate: true })

  function setAccent(value: string): void {
    accent.value = value
  }
  function reset(): void {
    accent.value = DEFAULT_ACCENT
  }

  return { accent, setAccent, reset, presets: ACCENT_PRESETS }
}
