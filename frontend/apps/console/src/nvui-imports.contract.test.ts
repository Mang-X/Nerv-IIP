import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * NvUI import-hygiene guard (ADR 0020 Decision 4.4 / issue #787 "四 app 合约守护铺满").
 *
 * This copy-adapts the business-console gold-standard contract so every app
 * consumes the component library ONLY through its stable package boundary —
 * the bare `@nerv-iip/ui` and `@nerv-iip/ui-mobile` specifiers. The single
 * concrete reason this exists: collaborating agents kept reaching past the
 * boundary (deep-importing shadcn 原版 / reka-ui) and mistaking 原版 primitives
 * for the brand layer. `Nv*` names + this guard remove that ambiguity.
 *
 * Hard-banned (fails the build):
 *   - deep imports into `@nerv-iip/ui/*` (except the `file-preview` sub-entry)
 *   - deep imports into `@nerv-iip/ui-mobile/*`
 *   - direct `reka-ui` imports (headless primitives live inside the library)
 *   - direct `shadcn-vue` imports
 *
 * Soft (warning only; the #789 codemod upgrades this to a hard allowlist error):
 *   - importing a @deprecated old component name (`*Pro`, legacy bare names).
 *     Old names stay usable this batch (zero-breakage) — see ADR 0020 S1/S2.
 */

const srcDir = dirname(fileURLToPath(import.meta.url))
const ALLOWED_UI_SUBPATHS = new Set(['file-preview'])

function collectSourceFiles(dir: string): string[] {
  const out: string[] = []
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name === 'dist') continue
    const full = join(dir, entry.name)
    if (entry.isDirectory()) {
      out.push(...collectSourceFiles(full))
    } else if (
      /\.(vue|tsx?)$/.test(entry.name) &&
      !entry.name.endsWith('.d.ts') &&
      !/\.(test|spec)\.[cm]?tsx?$/.test(entry.name)
    ) {
      out.push(full)
    }
  }
  return out
}

const files = collectSourceFiles(srcDir)
const MODULE_RE = /(?:from|import\(|\bimport)\s*['"]([^'"]+)['"]/g
const BARREL_CLAUSE_RE =
  /import\s+(?:[\w$]+\s*,\s*)?\{([^}]*)\}\s*from\s*['"]@nerv-iip\/ui(?:-mobile)?['"]/g

function modulesOf(src: string): string[] {
  const out: string[] = []
  let m: RegExpExecArray | null
  MODULE_RE.lastIndex = 0
  while ((m = MODULE_RE.exec(src))) out.push(m[1])
  return out
}

function deprecatedBarrelImports(src: string): string[] {
  const out: string[] = []
  let m: RegExpExecArray | null
  BARREL_CLAUSE_RE.lastIndex = 0
  while ((m = BARREL_CLAUSE_RE.exec(src))) {
    for (const raw of m[1].split(',')) {
      const name = raw
        .trim()
        .replace(/^type\s+/, '')
        .split(/\s+as\s+/)[0]
        .trim()
      // `*Pro` (pro layer) + old scene-prefixed names. New names are `Nv*`, so
      // `^(Screen|Mobile|Touch)[A-Z]` only ever matches deprecated old names.
      if (/Pro$/.test(name) || /^(Screen|Mobile|Touch)[A-Z]/.test(name)) out.push(name)
    }
  }
  return out
}

describe('NvUI import hygiene (stable package boundary)', () => {
  it('found app source files to guard', () => {
    expect(files.length).toBeGreaterThan(0)
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

  // Warning-form guard for @deprecated old names (ADR 0020 Decision 4.4 item 8).
  // Old names remain usable this batch; the #789 codemod flips this to an error.
  it('reports @deprecated old-name (*Pro) imports pending the #789 codemod', () => {
    const hits: string[] = []
    for (const file of files) {
      const names = deprecatedBarrelImports(readFileSync(file, 'utf8'))
      if (names.length) {
        hits.push(
          `${relative(srcDir, file).replace(/\\/g, '/')}: ${[...new Set(names)].join(', ')}`,
        )
      }
    }
    if (hits.length) {
      console.warn(
        `[NvUI] ${hits.length} file(s) still import @deprecated *Pro names — migrate to Nv* (#789):\n` +
          hits.join('\n'),
      )
    }
    expect(true).toBe(true)
  })
})
