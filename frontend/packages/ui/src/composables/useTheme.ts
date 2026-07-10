/**
 * Design System v2 — runtime theme controls (@nerv-iip/ui).
 *
 * Two independent, persisted dimensions:
 *   1. Colour mode: light / dark  → toggles the `.dark` class on <html>.
 *   2. Theme colour: drives the app's PRIMARY interactive colour at runtime
 *      (buttons, checkboxes, radios, switches, focus rings, active sidebar item…).
 *
 * How the primary colour becomes runtime-themeable
 * ------------------------------------------------
 * `styles/theme.css` bridges design tokens to Tailwind via `@theme inline`, so
 * `--color-primary: var(--primary)` is INLINED into every primary utility — i.e.
 * compiled `.bg-primary { background-color: var(--primary) }` (verified in the
 * build, not a literal `oklch(...)`). Because `--primary` is a plain custom
 * property defined on `:root`/`.dark`, writing it (and friends) as an INLINE
 * style on <html> overrides the cascade and re-colours every primary utility
 * live — no rebuild, no `!important`.
 *
 * Two kinds of theme:
 *   - `neutral` (黑白): we DON'T hard-code a colour. We CLEAR the inline
 *     overrides so the page falls back to the near-black (light) / near-white
 *     (dark) values baked into theme.css — correct in each colour mode.
 *   - a colour preset: we inline `--primary` / `--primary-foreground` / `--ring`
 *     / `--sidebar-primary` (+ keep `--nv-brand` in sync) on <html>.
 *
 * Both apps call `initTheme()` once in main.ts before mount so the persisted
 * choice is applied before first paint.
 */
import { useStorage } from '@vueuse/core'
import { computed, watch } from 'vue'

export type ColorMode = 'light' | 'dark'

export const COLOR_MODE_STORAGE_KEY = 'nerv-iip-color-mode'
/** Current theme storage key. */
export const THEME_STORAGE_KEY = 'nerv-iip-theme'
/** Legacy key (FE-1 stored a raw `--brand` oklch string). Migrated on read. */
export const ACCENT_STORAGE_KEY = 'nerv-iip-accent'

/** The neutral (black/white) theme — no colour, follows light/dark automatically. */
export const NEUTRAL_THEME = 'neutral'

/** A themeable preset: the primary colour + a readable foreground over it. */
export interface ThemePreset {
  /** Primary fill / selected / ring colour (oklch). */
  primary: string
  /** Foreground over the primary fill (oklch). */
  foreground: string
}

/**
 * Curated theme presets. `neutral` is special-cased (no entry — it means
 * "clear overrides, fall back to theme.css"). The rest set a coloured primary.
 *
 * Foregrounds are near-white because every preset is a mid/dark chroma that
 * white text reads well on, in both light and dark mode.
 */
export const THEME_PRESETS: Record<string, ThemePreset> = {
  blue: { primary: 'oklch(0.55 0.18 255)', foreground: 'oklch(0.985 0 0)' },
  violet: { primary: 'oklch(0.55 0.2 295)', foreground: 'oklch(0.985 0 0)' },
  teal: { primary: 'oklch(0.6 0.12 195)', foreground: 'oklch(0.985 0 0)' },
  green: { primary: 'oklch(0.62 0.17 149)', foreground: 'oklch(0.985 0 0)' },
  amber: { primary: 'oklch(0.72 0.16 70)', foreground: 'oklch(0.205 0 0)' },
  rose: { primary: 'oklch(0.6 0.21 15)', foreground: 'oklch(0.985 0 0)' },
}

/** A theme is either `neutral` or one of the colour preset keys. */
export type ThemeName = typeof NEUTRAL_THEME | keyof typeof THEME_PRESETS

/** Default theme = neutral (the platform's black/white primary). */
export const DEFAULT_THEME: ThemeName = NEUTRAL_THEME

/**
 * Back-compat: FE-1 exposed `--brand` presets keyed by name. Kept so existing
 * imports of `ACCENT_PRESETS` / `DEFAULT_ACCENT` don't break.
 */
export const ACCENT_PRESETS: Record<string, string> = Object.fromEntries(
  Object.entries(THEME_PRESETS).map(([name, p]) => [name, p.primary]),
)
export const DEFAULT_ACCENT = THEME_PRESETS.blue.primary

/** Inline custom properties we set on <html> for a coloured theme.
 *  The emphasis accent is the namespaced `--nv-brand` (ADR 0020 §3); the
 *  one-cycle alias `--brand: var(--nv-brand)` in theme.css keeps legacy direct
 *  `var(--brand)` refs tracking the runtime override. */
const PRIMARY_PROPS = [
  '--primary',
  '--primary-foreground',
  '--ring',
  '--sidebar-primary',
  '--nv-brand',
] as const

function hasDom(): boolean {
  return typeof document !== 'undefined'
}

function isThemeName(value: string | null): value is ThemeName {
  return value === NEUTRAL_THEME || (value != null && value in THEME_PRESETS)
}

/**
 * Migrate a legacy `nerv-iip-accent` value (a raw `--brand` oklch string) to a
 * theme name. Matches it back to a preset by primary colour; falls back to the
 * default theme if it can't be resolved.
 */
function migrateLegacyAccent(): ThemeName | null {
  if (!hasDom()) return null
  const legacy = localStorage.getItem(ACCENT_STORAGE_KEY)
  if (!legacy) return null
  const match = Object.entries(THEME_PRESETS).find(([, p]) => p.primary === legacy)
  return match ? (match[0] as ThemeName) : DEFAULT_THEME
}

function readStoredTheme(): ThemeName {
  if (!hasDom()) return DEFAULT_THEME
  const stored = localStorage.getItem(THEME_STORAGE_KEY)
  if (isThemeName(stored)) return stored
  return migrateLegacyAccent() ?? DEFAULT_THEME
}

function applyColorMode(mode: ColorMode): void {
  if (hasDom()) document.documentElement.classList.toggle('dark', mode === 'dark')
}

/**
 * Drive the primary interactive colour. `neutral` clears the inline overrides
 * (falls back to theme.css light/dark near-black/near-white); a colour preset
 * inlines the primary family + brand on <html>.
 */
function applyTheme(theme: ThemeName): void {
  if (!hasDom()) return
  const style = document.documentElement.style
  if (theme === NEUTRAL_THEME) {
    for (const prop of PRIMARY_PROPS) style.removeProperty(prop)
    return
  }
  const preset = THEME_PRESETS[theme]
  style.setProperty('--primary', preset.primary)
  style.setProperty('--primary-foreground', preset.foreground)
  style.setProperty('--ring', preset.primary)
  style.setProperty('--sidebar-primary', preset.primary)
  // Keep the emphasis accent (`--nv-brand`, used by e.g. active sidebar
  // highlight, chart-1, and the `--brand` alias) in sync with the theme colour.
  style.setProperty('--nv-brand', preset.primary)
}

function preferredMode(): ColorMode {
  if (hasDom() && window.matchMedia?.('(prefers-color-scheme: dark)').matches) return 'dark'
  return 'light'
}

/**
 * Apply the persisted colour mode + theme immediately. Call once in main.ts
 * (outside a component) before mounting the app.
 */
export function initTheme(): void {
  if (!hasDom()) return
  const storedMode = localStorage.getItem(COLOR_MODE_STORAGE_KEY)
  const mode: ColorMode =
    storedMode === 'dark' || storedMode === 'light' ? storedMode : preferredMode()
  applyColorMode(mode)
  applyTheme(readStoredTheme())
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

/**
 * Reactive theme colour, persisted across sessions. Drives the primary
 * interactive colour of the whole app (neutral or a colour preset).
 */
export function useTheme() {
  const theme = useStorage<ThemeName>(THEME_STORAGE_KEY, readStoredTheme())
  watch(theme, applyTheme, { immediate: true })

  const isNeutral = computed(() => theme.value === NEUTRAL_THEME)
  function setTheme(next: ThemeName): void {
    theme.value = next
  }
  function reset(): void {
    theme.value = DEFAULT_THEME
  }

  return {
    theme,
    isNeutral,
    setTheme,
    reset,
    presets: THEME_PRESETS,
    neutral: NEUTRAL_THEME,
  }
}

/**
 * @deprecated Use {@link useTheme}. Kept as a thin back-compat shim so prior
 * imports keep working; `accent` now reflects the selected theme's primary
 * colour string (or the default accent when neutral).
 */
export function useThemeAccent() {
  const { theme, setTheme, reset, presets } = useTheme()
  const accent = computed(() =>
    theme.value === NEUTRAL_THEME ? DEFAULT_ACCENT : THEME_PRESETS[theme.value].primary,
  )
  function setAccent(value: string): void {
    const match = Object.entries(THEME_PRESETS).find(([, p]) => p.primary === value)
    if (match) setTheme(match[0] as ThemeName)
  }
  return { accent, setAccent, reset, presets: ACCENT_PRESETS, _presets: presets }
}
