import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import * as mobile from './index'

/**
 * NvUI documentation-coverage ratchet — ui-mobile mirror of
 * `packages/ui/src/nvui-doc-coverage.contract.test.ts` (rationale there).
 * Every root `Nv*` export must appear in the coverage matrix, a DESIGN
 * decision doc, or a docs-site page. KNOWN_UNDOCUMENTED is shrink-only.
 */

const srcDir = dirname(fileURLToPath(import.meta.url))
const frontendRoot = resolve(srcDir, '../../..')

const exportedNv = Object.keys(mobile).filter((n) => /^Nv[A-Z]/.test(n))

const nameSet = new Set(exportedNv)
const isPart = (name: string): boolean => {
  for (let i = name.length - 1; i > 2; i--) {
    if (!/[A-Z]/.test(name[i])) continue
    if (nameSet.has(name.slice(0, i))) return true
  }
  return false
}
const roots = exportedNv.filter((n) => !isPart(n))

function collectMd(dir: string): string[] {
  const out: string[] = []
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, e.name)
    if (e.isDirectory()) out.push(...collectMd(full))
    else if (e.name.endsWith('.md')) out.push(full)
  }
  return out
}

const corpus = [
  resolve(frontendRoot, 'DESIGN/component-coverage.md'),
  ...collectMd(resolve(frontendRoot, 'DESIGN/components')),
  ...collectMd(resolve(frontendRoot, 'apps/design-system/docs/components')),
]
  .map((f) => readFileSync(f, 'utf8'))
  .join('\n')

const documented = (name: string): boolean => new RegExp(`\\b${name}\\b`).test(corpus)

// Shrink-only baseline — do NOT add new entries; document the component instead.
const KNOWN_UNDOCUMENTED = new Set<string>([])

describe('every root Nv mobile component is documented', () => {
  it('finds no undocumented root component outside the baseline', () => {
    const missing = roots.filter((n) => !documented(n) && !KNOWN_UNDOCUMENTED.has(n))
    expect(
      missing,
      `undocumented root Nv components (fulfill governance.md 六件套 DoD; do not add to baseline):\n${missing.join('\n')}`,
    ).toEqual([])
  })

  it('keeps the baseline shrink-only', () => {
    const stale = [...KNOWN_UNDOCUMENTED].filter((n) => !nameSet.has(n) || documented(n))
    expect(stale, `baseline entries now documented or gone — remove:\n${stale.join('\n')}`).toEqual(
      [],
    )
  })
})
