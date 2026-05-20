import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const currentDir = dirname(fileURLToPath(import.meta.url))
const cssPath = resolve(currentDir, '../../../apps/console/src/assets/main.css')

describe('design system token contract', () => {
  it('defines the Phase 8 Calm Control Plane token baseline', () => {
    const css = readFileSync(cssPath, 'utf8')

    expect(css).toContain('--primary: oklch(0.49 0.17 255);')
    expect(css).toContain('--primary-foreground: oklch(0.985 0 0);')
    expect(css).toContain('--ring: oklch(0.62 0.15 255);')
    expect(css).toContain('--accent: oklch(0.96 0.03 255);')
    expect(css).toContain('--accent-foreground: oklch(0.28 0.11 255);')
    expect(css).toContain('--sidebar-primary: var(--primary);')
    expect(css).toContain('--chart-1: oklch(0.58 0.16 255);')
    expect(css).toContain('--radius: 0.5rem;')
    expect(css).toContain('--legacy-color-page:')
    expect(css).toContain('@theme inline')
  })
})
