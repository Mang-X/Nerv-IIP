import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

/**
 * Escape a value for interpolation into an HTML string.
 *
 * The chart components hand unovis a tooltip *template string* that it renders
 * as raw HTML, so every interpolated point label, category, unit and colour is
 * an HTML sink — and those routinely carry server-sourced text. Internal to the
 * package (not re-exported from the barrel): NvUI owns its own HTML sinks, app
 * code should never be building markup by hand.
 */
export function escapeHtml(value: unknown): string {
  return String(value).replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c] as string,
  )
}

/**
 * Sanitise a value for interpolation into a CSS declaration — e.g. the chart
 * tooltips' inline `style="background:${…}"` swatch. escapeHtml alone stops the
 * value breaking out of the *attribute*, but not a `;`-separated extra
 * declaration: a hostile `red;position:fixed;inset:0;background:url(…)` colour
 * would still take effect inside the style string. Accept only a strict CSS
 * `<color>` — hex / a known colour function / a bare keyword / a `var()` ref —
 * and fall back to `currentColor` (harmless, still visible) for anything else.
 * `url(` is rejected outright since `background` would otherwise fetch it, and
 * quotes / angle brackets are forbidden so the result can't break out of the
 * `style="…"` attribute it lands in. This validates CSS *semantics*; callers
 * must STILL HTML-encode the result for the attribute context — the chart sinks
 * wrap it as `escapeHtml(cssColor(x))`. Internal to the package.
 */
export function cssColor(value: unknown): string {
  const s = String(value).trim()
  if (/url\(/i.test(s)) return 'currentColor'
  if (/^#[0-9a-f]{3,8}$/i.test(s)) return s
  if (/^[a-z]+$/i.test(s)) return s // named colours, currentColor, transparent
  // function forms: no ; { } (CSS-declaration breakers) and no " ' < > (HTML
  // attribute breakers) inside — a bare `rgb(0)" onmouseover="…` must not pass.
  if (/^(?:rgb|rgba|hsl|hsla|hwb|lab|lch|oklab|oklch|color|color-mix|var)\([^;{}"'<>]*\)$/i.test(s))
    return s
  return 'currentColor'
}
