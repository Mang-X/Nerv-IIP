import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

// Design System v2 tokens live in the shared, single-source-of-truth theme file
// inside @nerv-iip/ui (consumed by BOTH apps). Guard the brand-critical values.
const themePath = resolve(dirname(fileURLToPath(import.meta.url)), 'styles/theme.css')
const css = readFileSync(themePath, 'utf8')

describe('design system v2 token contract', () => {
  it('keeps a neutral (black/white) primary as the light/dark baseline', () => {
    // `neutral` theme falls back to these baked-in values: near-black in light,
    // near-white in dark. The runtime theme picker overrides them via inline
    // styles on <html>, but the neutral baseline must stay correct per mode.
    expect.soft(css).toContain('--primary: oklch(0.205 0 0);')
    expect.soft(css).toContain('--primary-foreground: oklch(0.985 0 0);')
    // The old Calm Control Plane blue must never be the baked primary.
    expect.soft(css).not.toContain('--primary: oklch(0.49 0.17 255);')
  })

  it('bridges primary tokens to Tailwind so utilities resolve to var(--primary)', () => {
    // The mechanism that makes the primary colour runtime-themeable: `@theme
    // inline` maps each --color-* to its var(--*). Tailwind then inlines
    // `var(--primary)` into `.bg-primary` (rather than a frozen oklch literal),
    // so an inline `--primary` on <html> re-colours every primary utility live.
    // These bridges must exist for the theme picker to drive primary/ring/sidebar.
    expect.soft(css).toContain('@theme inline')
    expect.soft(css).toContain('--color-primary: var(--primary);')
    expect.soft(css).toContain('--color-primary-foreground: var(--primary-foreground);')
    expect.soft(css).toContain('--color-ring: var(--ring);')
    expect.soft(css).toContain('--color-sidebar-primary: var(--sidebar-primary);')
  })

  it('exposes a runtime-overridable brand accent', () => {
    expect.soft(css).toContain('--brand: oklch(0.54 0.16 256);')
    expect.soft(css).toContain('--brand-foreground:')
    expect.soft(css).toContain('--color-brand: var(--brand);')
    // chart-1 tracks the dynamic brand.
    expect.soft(css).toContain('--chart-1: var(--brand);')
  })

  it('defines success and warning semantic colours', () => {
    expect.soft(css).toContain('--success:')
    expect.soft(css).toContain('--success-foreground:')
    expect.soft(css).toContain('--warning:')
    expect.soft(css).toContain('--warning-foreground:')
    expect.soft(css).toContain('--color-success: var(--success);')
    expect.soft(css).toContain('--color-warning: var(--warning);')
  })

  it('provides an elevation scale (background ≠ card + shadow tokens)', () => {
    // Page canvas is a notch off the card surface — the dashboard-01 inset look.
    expect.soft(css).toContain('--background: oklch(0.985 0 0);')
    expect.soft(css).toContain('--card: oklch(1 0 0);')
    expect.soft(css).toContain('--shadow-sm:')
    expect.soft(css).toContain('--shadow-md:')
    expect.soft(css).toContain('--shadow-lg:')
  })

  it('ships a full dark theme override', () => {
    expect.soft(css).toContain('.dark {')
    expect.soft(css).toContain('--primary: oklch(0.922 0 0);')
    expect.soft(css).toContain('color-scheme: dark;')
  })

  it('keeps shared radius and Tailwind theme bridging', () => {
    expect.soft(css).toContain('--radius: 0.5rem;')
    expect.soft(css).toContain('@theme inline')
    expect.soft(css).toContain('@custom-variant dark')
  })
})
