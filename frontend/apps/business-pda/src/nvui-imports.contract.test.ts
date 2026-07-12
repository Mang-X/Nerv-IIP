import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * NvUI import-hygiene guard (ADR 0020 Decision 4.4 / #789 closeout).
 *
 * The component library is consumed ONLY through its stable package boundary — the
 * bare `@nerv-iip/ui` / `@nerv-iip/ui-mobile` specifiers, using the `Nv*` brand names.
 *
 *  1. **Hard bans** (always fail): deep imports into `@nerv-iip/ui|ui-mobile/*`
 *     (except the `file-preview` sub-entry), direct `reka-ui`, direct `shadcn-vue`.
 *  2. **Closeout invariant**: the codemod closeout (#789) removed every `@deprecated`
 *     old-name alias from the library barrels, so an old name is no longer importable
 *     at all — any attempt is now a hard typecheck error rather than a soft ratchet
 *     warning (the per-app `nvui-legacy-imports.baseline.json` is retired). This test
 *     asserts the library exposes zero `@deprecated` aliases, so a regression that
 *     re-introduces one fails here.
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

// Deprecated old names, derived from the `@deprecated` aliases in the library barrels.
// After the #789 closeout this set is empty — that is the invariant asserted below.
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
function modulesOf(src: string): string[] {
  const out: string[] = []
  let m: RegExpExecArray | null
  MODULE_RE.lastIndex = 0
  while ((m = MODULE_RE.exec(src))) out.push(m[1])
  return out
}

const files = walk(srcDir, isSource)

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

  // #789 closeout: the library exposes NO `@deprecated` old-name aliases, so an old
  // name (`NvButton`, `ScreenPanel`, `Badge` from ui-mobile, …) is no longer
  // importable — the old-name ratchet + per-app baseline are retired and typecheck is
  // the hard guard. This asserts the invariant so a regression re-adding an alias fails.
  it('the library exposes no @deprecated old-name aliases (closeout done)', () => {
    expect(
      [...UI_OLD, ...MOBILE_OLD].sort(),
      'closeout removed every @deprecated alias — an old-name import is now a typecheck error',
    ).toEqual([])
  })
})
