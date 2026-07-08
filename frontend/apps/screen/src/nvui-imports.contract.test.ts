import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * NvUI import-hygiene guard (ADR 0020 Decision 4.4 / issue #787 "四 app 合约守护铺满").
 *
 * Copy-adapted per app (companion to the business-console gold-standard contract).
 * The component library may be consumed ONLY through its stable package boundary —
 * the bare `@nerv-iip/ui` / `@nerv-iip/ui-mobile` specifiers, using the `Nv*` brand
 * names. Two enforcement layers:
 *
 *  1. **Hard bans** (always fail): deep imports into `@nerv-iip/ui|ui-mobile/*`
 *     (except the `file-preview` sub-entry), direct `reka-ui`, direct `shadcn-vue`.
 *  2. **@deprecated old-name ratchet**: old names stay usable this batch
 *     (zero-breakage — see ADR 0020 S1), but a NEW old-name import is BLOCKED. A
 *     file/name pair not present in the checked-in baseline
 *     (`nvui-legacy-imports.baseline.json`, the #789 migration work-list) fails the
 *     build. The deprecated set is derived from the library barrels themselves, so
 *     the guard can never drift from the actual `@deprecated` aliases; it is
 *     barrel-aware (`Badge`/`Empty`/`DropdownMenu` are deprecated only from
 *     `@nerv-iip/ui-mobile`; from `@nerv-iip/ui` they are original primitives).
 */

const srcDir = dirname(fileURLToPath(import.meta.url))
const frontendRoot = resolve(srcDir, '../../..')
const ALLOWED_UI_SUBPATHS = new Set(['file-preview'])

function walk(dir: string, keep: (name: string) => boolean): string[] {
  const out: string[] = []
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'node_modules' || e.name === 'dist') continue
    const full = join(dir, e.name)
    if (e.isDirectory()) out.push(...walk(full, keep))
    else if (keep(e.name)) out.push(full)
  }
  return out
}

const isSource = (n: string) =>
  /\.(vue|ts)$/.test(n) && !/\.(test|spec)\./.test(n) && !n.endsWith('.d.ts')

// Deprecated old component names, derived from the `@deprecated` aliases in the
// library barrels — single source of truth.
function deriveDeprecated(files: string[]): Set<string> {
  const s = new Set<string>()
  const re = /@deprecated[^\n]*\n\s*(?:default|[A-Za-z0-9_]+)\s+as\s+([A-Za-z0-9_]+)/g
  for (const f of files) {
    const src = readFileSync(f, 'utf8')
    let m: RegExpExecArray | null
    re.lastIndex = 0
    while ((m = re.exec(src))) s.add(m[1])
  }
  return s
}
const UI_OLD = deriveDeprecated(
  walk(resolve(frontendRoot, 'packages/ui/src/components'), (n) => n === 'index.ts'),
)
const MOBILE_OLD = deriveDeprecated([resolve(frontendRoot, 'packages/ui-mobile/src/index.ts')])

const MODULE_RE = /(?:from|import\(|\bimport)\s*['"]([^'"]+)['"]/g
const BARREL_CLAUSE_RE =
  /import\s+(?:[\w$]+\s*,\s*)?\{([^}]*)\}\s*from\s*['"](@nerv-iip\/ui(?:-mobile)?)['"]/g

function modulesOf(src: string): string[] {
  const out: string[] = []
  let m: RegExpExecArray | null
  MODULE_RE.lastIndex = 0
  while ((m = MODULE_RE.exec(src))) out.push(m[1])
  return out
}

function deprecatedOldNamesIn(src: string): Set<string> {
  const found = new Set<string>()
  let m: RegExpExecArray | null
  BARREL_CLAUSE_RE.lastIndex = 0
  while ((m = BARREL_CLAUSE_RE.exec(src))) {
    const set = m[2].endsWith('mobile') ? MOBILE_OLD : UI_OLD
    for (const raw of m[1].split(',')) {
      const name = raw
        .trim()
        .replace(/^type\s+/, '')
        .split(/\s+as\s+/)[0]
        .trim()
      if (set.has(name)) found.add(name)
    }
  }
  return found
}

const files = walk(srcDir, isSource)
const baseline: Record<string, string[]> = JSON.parse(
  readFileSync(join(srcDir, 'nvui-legacy-imports.baseline.json'), 'utf8'),
)

describe('NvUI import hygiene (stable package boundary)', () => {
  it('found app source files + a derived deprecated set to guard', () => {
    expect(files.length).toBeGreaterThan(0)
    expect(UI_OLD.size).toBeGreaterThan(0)
  })

  for (const file of files) {
    const rel = relative(srcDir, file).replace(/\\/g, '/')
    it(`${rel} imports NvUI only through the stable boundary`, () => {
      for (const spec of modulesOf(readFileSync(file, 'utf8'))) {
        const uiDeep = /^@nerv-iip\/ui\/(.+)$/.exec(spec)
        expect
          .soft(
            uiDeep ? !ALLOWED_UI_SUBPATHS.has(uiDeep[1]) : false,
            `deep import "${spec}" — use the bare @nerv-iip/ui barrel`,
          )
          .toBe(false)
        expect
          .soft(
            /^@nerv-iip\/ui-mobile\/.+$/.test(spec),
            `deep import "${spec}" — use the bare @nerv-iip/ui-mobile barrel`,
          )
          .toBe(false)
        expect
          .soft(
            /^reka-ui(\/|$)/.test(spec),
            `direct reka-ui import "${spec}" — headless primitives live inside @nerv-iip/ui`,
          )
          .toBe(false)
        expect
          .soft(
            /^shadcn-vue(\/|$)/.test(spec),
            `direct shadcn-vue import "${spec}" — use @nerv-iip/ui`,
          )
          .toBe(false)
      }
    })
  }

  // @deprecated old-name ratchet — NEW old-name imports FAIL; current usage is
  // grandfathered by the baseline (the #789 migration work-list, shrink-only).
  it('blocks NEW @deprecated old-name imports (ratchet vs baseline)', () => {
    const offenders: string[] = []
    for (const file of files) {
      const rel = relative(srcDir, file).replace(/\\/g, '/')
      const allowed = new Set(baseline[rel] ?? [])
      for (const name of deprecatedOldNamesIn(readFileSync(file, 'utf8'))) {
        if (!allowed.has(name)) offenders.push(`${rel}: ${name}`)
      }
    }
    expect(
      offenders,
      `New @deprecated old-name import(s) — use the Nv* brand names (ADR 0020 Appendix A / #789):\n${offenders.join('\n')}`,
    ).toEqual([])
  })

  // Proof the ratchet is a real block (not a warning): the detector flags old
  // names from the right barrel and leaves Nv* / shadcn 原版 primitives alone.
  it('actually intercepts a deprecated old-name import (not a warning)', () => {
    expect(UI_OLD.has('ButtonPro'), 'derived set covers pro old names').toBe(true)
    expect(UI_OLD.has('PageHeader'), 'derived set covers bare block old names').toBe(true)
    expect([
      ...deprecatedOldNamesIn("import { NvButton, ButtonPro } from '@nerv-iip/ui'"),
    ]).toContain('ButtonPro')
    expect([
      ...deprecatedOldNamesIn("import { NvMobileBadge, Badge } from '@nerv-iip/ui-mobile'"),
    ]).toContain('Badge')
    // Nv* brand names and @nerv-iip/ui original primitives are NOT flagged.
    expect([...deprecatedOldNamesIn("import { NvButton, Button } from '@nerv-iip/ui'")]).toEqual([])
  })
})
