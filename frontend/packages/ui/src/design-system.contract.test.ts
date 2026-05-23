import { existsSync, readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const workspaceRoot = findWorkspaceRoot(process.cwd())
const cssPath = resolve(workspaceRoot, 'apps/console/src/assets/main.css')
const css = readFileSync(cssPath, 'utf8')

describe('design system token contract', () => {
  it('defines the blue primary and focus tokens', () => {
    expect.soft(css).toContain('--primary: oklch(0.49 0.17 255);')
    expect.soft(css).toContain('--primary-foreground: oklch(0.985 0 0);')
    expect.soft(css).toContain('--ring: oklch(0.62 0.15 255);')
  })

  it('defines blue accent, sidebar, and chart orientation tokens', () => {
    expect.soft(css).toContain('--accent: oklch(0.96 0.03 255);')
    expect.soft(css).toContain('--accent-foreground: oklch(0.28 0.11 255);')
    expect.soft(css).toContain('--sidebar-primary: var(--primary);')
    expect.soft(css).toContain('--chart-1: oklch(0.58 0.16 255);')
  })

  it('keeps shared radius and Tailwind theme bridging', () => {
    expect.soft(css).toContain('--radius: 0.5rem;')
    expect.soft(css).toContain('@theme inline')
  })
})

function findWorkspaceRoot(start: string) {
  let current = start

  while (!existsSync(resolve(current, 'pnpm-workspace.yaml'))) {
    const parent = dirname(current)
    if (parent === current) {
      throw new Error(`Unable to locate frontend workspace root from ${start}.`)
    }

    current = parent
  }

  return current
}
