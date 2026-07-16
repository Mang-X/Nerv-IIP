import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import * as ui from './index'

/**
 * NvUI documentation-coverage ratchet (governance.md 新组件六件套 DoD 第 4/5/6 件).
 *
 * Every root `Nv*` component exported from the stable barrel must be
 * discoverable in the documentation system — i.e. its exact name appears in at
 * least one of:
 *   - `frontend/DESIGN/component-coverage.md` (four-surface matrix),
 *   - a decision doc under `frontend/DESIGN/components/`,
 *   - a docs-site page under `frontend/apps/design-system/docs/components/`.
 *
 * "Root" component: an exported Nv name that is not a part of another exported
 * Nv name (`NvCardHeader` is a part of `NvCard` and rides on its family doc).
 * The prefix heuristic is deliberately lenient — a part never fails the gate.
 *
 * KNOWN_UNDOCUMENTED is a shrink-only baseline: components shipped before this
 * gate existed. Adding a NEW component to it is forbidden — document it per the
 * six-piece DoD instead. Removing entries (after documenting) is the goal.
 */

const srcDir = dirname(fileURLToPath(import.meta.url))
const frontendRoot = resolve(srcDir, '../../..')

const NON_COMPONENT_EXPORTS = new Set(['NvUI'])

const exportedNv = Object.keys(ui)
  .filter((n) => /^Nv[A-Z]/.test(n))
  .filter((n) => !NON_COMPONENT_EXPORTS.has(n))

const nameSet = new Set(exportedNv)
const isPart = (name: string): boolean => {
  for (let i = name.length - 1; i > 2; i--) {
    if (!/[A-Z]/.test(name[i])) continue
    const prefix = name.slice(0, i)
    if (nameSet.has(prefix)) return true
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

const corpusFiles = [
  resolve(frontendRoot, 'DESIGN/component-coverage.md'),
  ...collectMd(resolve(frontendRoot, 'DESIGN/components')),
  ...collectMd(resolve(frontendRoot, 'apps/design-system/docs/components')),
]
const corpus = corpusFiles.map((f) => readFileSync(f, 'utf8')).join('\n')

const documented = (name: string): boolean => new RegExp(`\\b${name}\\b`).test(corpus)

// Shrink-only baseline — do NOT add new entries; document the component instead.
const KNOWN_UNDOCUMENTED = new Set<string>([])

describe('every root Nv component is documented (coverage matrix / DESIGN / docs site)', () => {
  it('finds no undocumented root component outside the baseline', () => {
    const missing = roots.filter((n) => !documented(n) && !KNOWN_UNDOCUMENTED.has(n))
    expect(
      missing,
      `undocumented root Nv components (fulfill governance.md 六件套 DoD; do not add to baseline):\n${missing.join('\n')}`,
    ).toEqual([])
  })

  it('keeps the baseline shrink-only (documented entries must be removed)', () => {
    const stale = [...KNOWN_UNDOCUMENTED].filter((n) => !nameSet.has(n) || documented(n))
    expect(
      stale,
      `baseline entries now documented or no longer exported — remove them:\n${stale.join('\n')}`,
    ).toEqual([])
  })
})
