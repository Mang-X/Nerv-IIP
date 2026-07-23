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
