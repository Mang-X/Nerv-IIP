// @ts-nocheck
/**
 * NvUI per-app codemod — ADR 0020 Decision 5 (S2) / Linear MAN-435 / GitHub #789.
 *
 * Replaces the `@deprecated` old component names (`*Pro` / `Screen*` / `Mobile*` /
 * bare block/layout names) with the canonical `Nv*` brand names inside a single app,
 * in both the `<script>` (imports + identifier/type references, TS-AST accurate) and
 * the `<template>` (PascalCase component tags). It ALSO regenerates the app's
 * `nvui-legacy-imports.baseline.json` ratchet so the import-hygiene contract test
 * stays green with the reduced (ideally empty) legacy set.
 *
 * The old→new mapping is DERIVED FROM THE LIBRARY BARRELS (the `@deprecated ... as
 * Old` aliases paired with their canonical `Nv*` sibling in the same export block) —
 * the barrels are the single source of truth (ADR: "若映射表与库内实际有出入以库内
 * 为准"). Appendix A is cross-checked, not transcribed.
 *
 * Usage (run from anywhere; paths are resolved against the repo):
 *   node frontend/tools/nvui-codemod.mjs --app business-console            # dry-run
 *   node frontend/tools/nvui-codemod.mjs --app business-console --write    # apply
 *   node frontend/tools/nvui-codemod.mjs --print-map                       # derived map only
 *
 * Safe to re-run (idempotent): once an app has no old names, it is a no-op.
 */
import { readdirSync, readFileSync, writeFileSync, existsSync, statSync } from 'node:fs'
import { dirname, join, resolve, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'

const HERE = dirname(fileURLToPath(import.meta.url))
const FRONTEND = resolve(HERE, '..')
const REPO = resolve(FRONTEND, '..')

// Resolve vue/compiler-sfc + typescript through an app that actually has them
// installed (pnpm keeps deps local; the workspace root cannot resolve `vue`).
const appRequire = createRequire(resolve(FRONTEND, 'apps/business-console/package.json'))
const ts = appRequire('typescript')
const { parse: parseSfc } = appRequire('vue/compiler-sfc')

const UI = '@nerv-iip/ui'
const UI_MOBILE = '@nerv-iip/ui-mobile'

// ---------------------------------------------------------------------------
// 1. Derive old→new maps from the library barrels (per package).
// ---------------------------------------------------------------------------
function walk(dir, keep, out = []) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'node_modules' || e.name === 'dist') continue
    const full = join(dir, e.name)
    if (e.isDirectory()) walk(full, keep, out)
    else if (keep(e.name, full)) out.push(full)
  }
  return out
}

/**
 * Return old→new pairs for a barrel file, driven by the `@deprecated` JSDoc — the
 * single reliable signal across all three barrel idioms in this codebase:
 *   1. standard block:   `default as NvButton,` + `@deprecated … \`NvButton\`` `default as ButtonPro,`
 *   2. superseded block: `@deprecated Superseded by \`NvDataTable\`` `default as DataTable,` (no Nv sibling)
 *   3. const/type alias: `@deprecated Use \`nvFieldVariants\`` `export const fieldProVariants = …`
 * The canonical `Nv*`/`nv*` name is always named in backticks on/near the
 * `@deprecated` line; the deprecated old name is the next code identifier after it.
 */
function pairsFromBarrel(src) {
  const pairs = []
  const lines = src.split('\n')
  const isCommentLine = (t) =>
    t === '' || t.startsWith('*') || t.startsWith('//') || t.startsWith('/*') || t.startsWith('*/')
  for (let i = 0; i < lines.length; i++) {
    if (!/@deprecated/.test(lines[i])) continue
    // Accumulate comment text (this line + any continuation) to find the `Nv…` target.
    let commentText = lines[i]
    let j = i + 1
    for (; j < lines.length; j++) {
      const t = lines[j].trim()
      if (t === '') continue
      if (isCommentLine(t)) {
        commentText += ' ' + t
        continue
      }
      break // first code line — holds the deprecated old name
    }
    const nvm = /`((?:Nv|nv)[A-Za-z0-9]+)`/.exec(commentText)
    if (!nvm || j >= lines.length) continue
    const newName = nvm[1]
    const code = lines[j].trim()
    // old name forms on the code line
    let oldName = null
    let m
    if (
      (m = /^export\s+(?:const|type|interface|class|function|let|var)\s+([A-Za-z0-9_$]+)/.exec(
        code,
      ))
    )
      oldName = m[1]
    else if (
      (m = /^(?:type\s+)?(?:default|[A-Za-z0-9_$]+)\s+as\s+([A-Za-z0-9_$]+)\s*,?$/.exec(code))
    )
      oldName = m[1]
    else if ((m = /^(?:type\s+)?([A-Za-z0-9_$]+)\s*,?$/.exec(code))) oldName = m[1]
    if (oldName && oldName !== newName) pairs.push([oldName, newName])
  }
  return pairs
}

function buildMap(files) {
  const map = new Map()
  for (const f of files) {
    for (const [oldN, newN] of pairsFromBarrel(readFileSync(f, 'utf8'))) {
      if (map.has(oldN) && map.get(oldN) !== newN) {
        throw new Error(`conflicting mapping for ${oldN}: ${map.get(oldN)} vs ${newN}`)
      }
      map.set(oldN, newN)
    }
  }
  return map
}

const uiBarrels = walk(resolve(FRONTEND, 'packages/ui/src/components'), (n) => n === 'index.ts')
const uiMap = buildMap(uiBarrels)
const mobileMap = buildMap([resolve(FRONTEND, 'packages/ui-mobile/src/index.ts')])

function mapFor(spec) {
  if (spec === UI) return uiMap
  if (spec === UI_MOBILE) return mobileMap
  return null
}

// ---------------------------------------------------------------------------
// 2. Cross-check derived maps against the frozen Appendix A arrays.
// ---------------------------------------------------------------------------
function crossCheck() {
  const testSrc = readFileSync(
    resolve(FRONTEND, 'packages/ui/src/nvui-naming.contract.test.ts'),
    'utf8',
  )
  const grab = (name) => {
    const m = new RegExp(`const ${name} = \\[([\\s\\S]*?)\\]`).exec(testSrc)
    if (!m) return []
    return [...m[1].matchAll(/'([^']+)'/g)].map((x) => x[1])
  }
  const frozenOld = new Set(grab('OLD_ALL'))
  const frozenNew = new Set(grab('NV_ALL'))
  const derivedOld = new Set(uiMap.keys())
  const derivedNew = new Set(uiMap.values())
  const diff = (a, b) => [...a].filter((x) => !b.has(x)).sort()
  return {
    oldOnlyInFrozen: diff(frozenOld, derivedOld),
    oldOnlyInDerived: diff(derivedOld, frozenOld),
    newOnlyInFrozen: diff(frozenNew, derivedNew),
    newOnlyInDerived: diff(derivedNew, frozenNew),
  }
}

// ---------------------------------------------------------------------------
// 3. Transform a single file.
// ---------------------------------------------------------------------------
// Files the codemod rewrites — includes tests (their mock/stub keys must track the
// renamed SFC tags). The NvUI import-hygiene guard itself is left alone: its old
// names are deliberate detector fixtures.
const isCodemodTarget = (n) => /\.(vue|ts|tsx)$/.test(n) && !n.endsWith('.d.ts')
const isGuardTest = (n) => n.endsWith('nvui-imports.contract.test.ts')
// The legacy-baseline ratchet only scans non-test source (mirrors the contract test's
// own `isSource`), so baseline regeneration must use the same predicate.
const isSourceNonTest = (n) =>
  /\.(vue|ts|tsx)$/.test(n) && !/\.(test|spec)\./.test(n) && !n.endsWith('.d.ts')
const isTestFile = (n) => /\.(test|spec)\./.test(n)

/**
 * Rewrite one TS script fragment. Returns { code, localRenames, changed } where
 * localRenames maps a (non-aliased) local binding name → new name so the template
 * pass can rewrite the corresponding component tags.
 *
 * Test-file options (a barrel import in a test that spells an old name is renamed by
 * the normal import/ref passes above; these two only touch things those passes miss):
 *  - opts.keyStringMap: rename the export KEYS inside a `vi.mock('@nerv-iip/ui'|
 *    '@nerv-iip/ui-mobile', factory)` return object (the SFCs now `import { Nv* }`
 *    from the mocked module). Local stub *values* stay, so `{ DataTablePro: DataTable }`
 *    → `{ NvDataTable: DataTable }`. `global.stubs` keys, `findComponent({ name })`
 *    and stub `name:` fields are deliberately NOT touched — they bind to the component
 *    runtime `__name` (the unchanged `.vue` filename, e.g. `DataTablePaginationPro`).
 *  - opts.renameStrings: for static source-reader tests (readFileSync a migrated SFC
 *    then assert on it) rename every old-name string literal — e.g. goldStandardPages
 *    `REQUIRED_BLOCKS`, erp-business-flow `toContain('DataTablePro')`.
 */
function transformScript(code, fileLabel, opts = {}) {
  const keyStringMap = opts.keyStringMap ?? null
  const sf = ts.createSourceFile(fileLabel, code, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  /** @type {{start:number,end:number,text:string}[]} */
  const edits = []
  const localRenames = new Map() // local → new (non-aliased only)
  const declSpans = [] // import/export decl spans to skip in the identifier pass

  const rebuildClause = (node, moduleSpec) => {
    const m = mapFor(moduleSpec)
    if (!m) return
    // Handle import declarations
    if (ts.isImportDeclaration(node)) {
      const ic = node.importClause
      if (!ic || !ic.namedBindings || !ts.isNamedImports(ic.namedBindings)) return
      declSpans.push([node.getStart(sf), node.end])
      const typeOnlyDecl = !!ic.isTypeOnly
      const seen = new Set()
      const out = []
      let touched = false
      for (const el of ic.namedBindings.elements) {
        const imported = el.propertyName ? el.propertyName.text : el.name.text
        const local = el.name.text
        const isType = el.isTypeOnly
        const mapped = m.get(imported)
        if (mapped) {
          touched = true
          const newImported = mapped
          if (el.propertyName) {
            // aliased: keep local, only rename the imported side
            const key = `${isType}|${newImported} as ${local}`
            if (!seen.has(key)) {
              seen.add(key)
              out.push(`${isType ? 'type ' : ''}${newImported} as ${local}`)
            }
          } else {
            // non-aliased: local binding becomes the new name
            localRenames.set(local, newImported)
            const key = `${isType}|${newImported}`
            if (!seen.has(key)) {
              seen.add(key)
              out.push(`${isType ? 'type ' : ''}${newImported}`)
            }
          }
        } else {
          const key = el.propertyName ? `${isType}|${imported} as ${local}` : `${isType}|${local}`
          if (!seen.has(key)) {
            seen.add(key)
            out.push(
              `${isType ? 'type ' : ''}${el.propertyName ? `${imported} as ${local}` : local}`,
            )
          }
        }
      }
      if (!touched) {
        declSpans.pop()
        return
      }
      const nb = ic.namedBindings
      const inner = out.join(', ')
      edits.push({ start: nb.getStart(sf), end: nb.end, text: `{ ${inner} }` })
    }
    // Handle re-export declarations (export { … } from '…')
    if (ts.isExportDeclaration(node) && node.exportClause && ts.isNamedExports(node.exportClause)) {
      declSpans.push([node.getStart(sf), node.end])
      const seen = new Set()
      const out = []
      let touched = false
      for (const el of node.exportClause.elements) {
        const source = el.propertyName ? el.propertyName.text : el.name.text
        const exportedAs = el.name.text
        const isType = el.isTypeOnly
        const mapped = m.get(source)
        if (mapped) {
          touched = true
          const left = mapped
          const text = el.propertyName
            ? `${isType ? 'type ' : ''}${left} as ${exportedAs}`
            : `${isType ? 'type ' : ''}${left}`
          const key = `${isType}|${text}`
          if (!seen.has(key)) {
            seen.add(key)
            out.push(text)
          }
        } else {
          const text = el.propertyName
            ? `${isType ? 'type ' : ''}${source} as ${exportedAs}`
            : `${isType ? 'type ' : ''}${exportedAs}`
          const key = `${isType}|${text}`
          if (!seen.has(key)) {
            seen.add(key)
            out.push(text)
          }
        }
      }
      if (!touched) {
        declSpans.pop()
        return
      }
      edits.push({
        start: node.exportClause.getStart(sf),
        end: node.exportClause.end,
        text: `{ ${out.join(', ')} }`,
      })
    }
  }

  const visitImports = (node) => {
    if (ts.isImportDeclaration(node) && ts.isStringLiteral(node.moduleSpecifier)) {
      rebuildClause(node, node.moduleSpecifier.text)
    } else if (
      ts.isExportDeclaration(node) &&
      node.moduleSpecifier &&
      ts.isStringLiteral(node.moduleSpecifier)
    ) {
      rebuildClause(node, node.moduleSpecifier.text)
    }
    ts.forEachChild(node, visitImports)
  }
  visitImports(sf)

  // Identifier reference pass (body). Skip anything inside an import/export decl,
  // property-access member names, qualified-name right sides, and non-shorthand
  // object property keys.
  const inDecl = (pos) => declSpans.some(([s, e]) => pos >= s && pos < e)
  const visitRefs = (node) => {
    if (ts.isIdentifier(node) && localRenames.has(node.text)) {
      const p = node.parent
      let skip = inDecl(node.getStart(sf))
      if (!skip && p) {
        if (ts.isPropertyAccessExpression(p) && p.name === node) skip = true
        else if (ts.isQualifiedName(p) && p.right === node) skip = true
        else if (
          ts.isPropertyAssignment(p) &&
          p.name === node // key of `{ Key: value }` — not a reference
        )
          skip = true
        else if (ts.isImportSpecifier(p) || ts.isExportSpecifier(p)) skip = true
        else if (ts.isBindingElement(p) && p.propertyName === node) skip = true
      }
      if (!skip) {
        edits.push({ start: node.getStart(sf), end: node.end, text: localRenames.get(node.text) })
      }
    }
    ts.forEachChild(node, visitRefs)
  }
  visitRefs(sf)

  // Test-file-only pass: rename ONLY the module-export keys inside a
  // `vi.mock('@nerv-iip/ui'|'@nerv-iip/ui-mobile', factory)` return object — the SFCs
  // now `import { Nv* }` from the (mocked) barrel, so the factory keys must be Nv*.
  //
  // Everything else in a test that spells an old name binds to the component RUNTIME
  // identity (its `__name`, derived from the unchanged `.vue` filename): `global.stubs`
  // keys, `findComponent({ name })`, stub `name:` fields. Those must stay old — the
  // .vue files are NOT renamed by this codemod. (Source-text assertions that grep a
  // migrated SFC are handled separately.) Verified against items.test.ts (§ ADR reka trap).
  if (keyStringMap) {
    const mockObjects = new Set()
    const returnObjectsOf = (factory) => {
      const out = []
      if (!factory || (!ts.isArrowFunction(factory) && !ts.isFunctionExpression(factory)))
        return out
      const unwrap = (e) => {
        while (e && (ts.isParenthesizedExpression(e) || ts.isAwaitExpression(e))) e = e.expression
        return e
      }
      if (ts.isBlock(factory.body)) {
        const findReturns = (n) => {
          if (ts.isArrowFunction(n) || ts.isFunctionExpression(n) || ts.isFunctionDeclaration(n))
            return // don't cross into nested function scopes
          if (ts.isReturnStatement(n) && n.expression) {
            const e = unwrap(n.expression)
            if (ts.isObjectLiteralExpression(e)) out.push(e)
          }
          ts.forEachChild(n, findReturns)
        }
        ts.forEachChild(factory.body, findReturns)
      } else {
        const e = unwrap(factory.body)
        if (ts.isObjectLiteralExpression(e)) out.push(e)
      }
      return out
    }
    const collectMock = (node) => {
      if (
        ts.isCallExpression(node) &&
        ts.isPropertyAccessExpression(node.expression) &&
        node.expression.name.text === 'mock' &&
        ts.isIdentifier(node.expression.expression) &&
        (node.expression.expression.text === 'vi' ||
          node.expression.expression.text === 'vitest') &&
        node.arguments.length >= 2 &&
        ts.isStringLiteral(node.arguments[0]) &&
        (node.arguments[0].text === UI || node.arguments[0].text === UI_MOBILE)
      ) {
        for (const obj of returnObjectsOf(node.arguments[1])) mockObjects.add(obj)
      }
      ts.forEachChild(node, collectMock)
    }
    collectMock(sf)
    for (const obj of mockObjects) {
      for (const prop of obj.properties) {
        if (!ts.isPropertyAssignment(prop)) continue // skip spreads / shorthand (import refs handle those)
        const nm = prop.name
        if (ts.isIdentifier(nm) && keyStringMap.has(nm.text)) {
          edits.push({ start: nm.getStart(sf), end: nm.end, text: keyStringMap.get(nm.text) })
        } else if (ts.isStringLiteral(nm) && keyStringMap.has(nm.text)) {
          const q = code[nm.getStart(sf)]
          edits.push({
            start: nm.getStart(sf),
            end: nm.end,
            text: `${q}${keyStringMap.get(nm.text)}${q}`,
          })
        }
      }
    }
  }

  // Static source-reader tests (readFileSync a migrated SFC, then assert on component
  // names in the text — e.g. goldStandardPages REQUIRED_BLOCKS, erp-business-flow
  // toContain('DataTablePro')). These files don't mount, so there is no findComponent
  // /__name concern: every old-name string literal is a source-text expectation → Nv.
  if (keyStringMap && opts.renameStrings) {
    const visitStrings = (node) => {
      if (
        ts.isStringLiteral(node) &&
        keyStringMap.has(node.text) &&
        !(
          node.parent &&
          (ts.isImportDeclaration(node.parent) || ts.isExportDeclaration(node.parent))
        )
      ) {
        const q = code[node.getStart(sf)]
        edits.push({
          start: node.getStart(sf),
          end: node.end,
          text: `${q}${keyStringMap.get(node.text)}${q}`,
        })
      }
      ts.forEachChild(node, visitStrings)
    }
    visitStrings(sf)
  }

  if (edits.length === 0) return { code, localRenames, changed: false }
  edits.sort((a, b) => b.start - a.start)
  let out = code
  let lastStart = Infinity
  for (const e of edits) {
    if (e.end > lastStart) continue // guard against overlaps
    out = out.slice(0, e.start) + e.text + out.slice(e.end)
    lastStart = e.start
  }
  return { code: out, localRenames, changed: true }
}

const KEBAB = (n) =>
  n
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .replace(/([A-Z])([A-Z][a-z])/g, '$1-$2')
    .toLowerCase()

/** Rewrite PascalCase (and defensively kebab-case) component tags in a template. */
function transformTemplate(tpl, localRenames) {
  let out = tpl
  let changed = false
  for (const [oldName, newName] of localRenames) {
    const paBefore = out
    // <Old …>, </Old>, <Old/> — boundary so Old doesn't match OldSomething
    out = out.replace(
      new RegExp(`(</?)(${oldName})(?![A-Za-z0-9_-])`, 'g'),
      (_m, lead) => `${lead}${newName}`,
    )
    // kebab form (defensive; none observed in the 3 PC/PDA apps)
    const kOld = KEBAB(oldName)
    const kNew = KEBAB(newName)
    if (kOld !== oldName) {
      out = out.replace(
        new RegExp(`(</?)(${kOld})(?![a-z0-9-])`, 'g'),
        (_m, lead) => `${lead}${kNew}`,
      )
    }
    if (out !== paBefore) changed = true
  }
  return { code: out, changed }
}

/** Detect dynamic-component references (`:is` / `is=`) to a renamed local — needs eyes. */
function dynamicComponentWarnings(tpl, localRenames, rel) {
  const warns = []
  for (const oldName of localRenames.keys()) {
    const re = new RegExp(`\\bis=["'][^"']*\\b${oldName}\\b`, 'g')
    if (re.test(tpl)) warns.push(`${rel}: dynamic component tag references "${oldName}" — verify`)
  }
  return warns
}

function transformVue(source, rel) {
  const { descriptor, errors } = parseSfc(source, { filename: rel })
  if (errors && errors.length) {
    // Non-fatal parse warnings can occur; only bail on hard errors.
  }
  const edits = []
  const localRenames = new Map()
  const scriptBlocks = [descriptor.script, descriptor.scriptSetup].filter(Boolean)
  for (const blk of scriptBlocks) {
    const r = transformScript(blk.content, rel)
    for (const [k, v] of r.localRenames) localRenames.set(k, v)
    if (r.changed) {
      edits.push({ start: blk.loc.start.offset, end: blk.loc.end.offset, text: r.code })
    }
  }
  const warnings = []
  if (descriptor.template && localRenames.size) {
    const tpl = descriptor.template
    const r = transformTemplate(tpl.content, localRenames)
    warnings.push(...dynamicComponentWarnings(tpl.content, localRenames, rel))
    if (r.changed) {
      edits.push({ start: tpl.loc.start.offset, end: tpl.loc.end.offset, text: r.code })
    }
  }
  if (edits.length === 0) return { code: source, changed: false, warnings }
  edits.sort((a, b) => b.start - a.start)
  let out = source
  for (const e of edits) out = out.slice(0, e.start) + e.text + out.slice(e.end)
  return { code: out, changed: true, warnings }
}

function transformFile(absPath, rel, appMap) {
  const source = readFileSync(absPath, 'utf8')
  if (absPath.endsWith('.vue')) return transformVue(source, rel)
  // Test files: rename barrel imports/refs + vi.mock(barrel) export keys. Static
  // source-reader tests (readFileSync a migrated SFC) additionally get old-name string
  // literals renamed. Non-test .ts get imports+refs only.
  const opts = isTestFile(absPath)
    ? { keyStringMap: appMap, renameStrings: /readFileSync|readFile\s*\(/.test(source) }
    : {}
  const r = transformScript(source, rel, opts)
  return { code: r.code, changed: r.changed, warnings: [] }
}

// ---------------------------------------------------------------------------
// 4. Baseline regeneration (mirror of the contract test's detector).
// ---------------------------------------------------------------------------
function deriveDeprecatedSets() {
  const re = /@deprecated[^\n]*\n\s*(?:default|[A-Za-z0-9_]+)\s+as\s+([A-Za-z0-9_]+)/g
  const readSet = (files) => {
    const s = new Set()
    for (const f of files) {
      const src = readFileSync(f, 'utf8')
      let m
      re.lastIndex = 0
      while ((m = re.exec(src))) s.add(m[1])
    }
    return s
  }
  return {
    UI_OLD: readSet(uiBarrels),
    MOBILE_OLD: readSet([resolve(FRONTEND, 'packages/ui-mobile/src/index.ts')]),
  }
}

function deprecatedOldNamesIn(src, UI_OLD, MOBILE_OLD) {
  const found = new Set()
  const BARREL_CLAUSE_RE =
    /import\s+(?:[\w$]+\s*,\s*)?\{([^}]*)\}\s*from\s*['"](@nerv-iip\/ui(?:-mobile)?)['"]/g
  let m
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

function regenBaseline(appSrcDir, contentByRel) {
  const { UI_OLD, MOBILE_OLD } = deriveDeprecatedSets()
  const files = walk(appSrcDir, isSourceNonTest)
  const baseline = {}
  for (const f of files) {
    const rel = relative(appSrcDir, f).replace(/\\/g, '/')
    const src = contentByRel?.get(rel) ?? readFileSync(f, 'utf8')
    const names = [...deprecatedOldNamesIn(src, UI_OLD, MOBILE_OLD)].sort()
    if (names.length) baseline[rel] = names
  }
  return baseline
}

// ---------------------------------------------------------------------------
// 5. CLI
// ---------------------------------------------------------------------------
function main() {
  const args = process.argv.slice(2)
  const write = args.includes('--write')
  const printMap = args.includes('--print-map')
  const appIdx = args.indexOf('--app')
  const app = appIdx >= 0 ? args[appIdx + 1] : null

  const cc = crossCheck()
  // The frozen OLD_ALL in nvui-naming.contract.test.ts is built by a regex that only
  // captures `… as X` after @deprecated, so it can't see the bare-reexport derived
  // TYPE old-names nor the const/type variant aliases. The codemod DOES need them
  // (e.g. DataTableProColumn is a type used in ~90 files), so the derived map is a
  // deliberate superset here. Anything OUTSIDE this expected set is a real drift.
  const EXPECTED_DERIVED_EXTRA = new Set([
    'DataTableProAlign',
    'DataTableProColumn',
    'DataTableProDensity',
    'DataTableProFilterOption',
    'DataTableProFilters',
    'DataTableProSort',
    'FieldProVariants',
    'fieldProVariants',
  ])
  const unexpectedDerived = cc.oldOnlyInDerived.filter((x) => !EXPECTED_DERIVED_EXTRA.has(x))
  const realDrift =
    cc.oldOnlyInFrozen.length ||
    unexpectedDerived.length ||
    cc.newOnlyInFrozen.length ||
    cc.newOnlyInDerived.length
  console.log(`[map] derived ${uiMap.size} ui + ${mobileMap.size} ui-mobile old→new pairs`)
  if (realDrift) {
    console.log('[map] ⚠ UNEXPECTED derived (barrels) vs frozen Appendix A drift:')
    console.log('      old only in frozen  :', cc.oldOnlyInFrozen.join(', ') || '—')
    console.log('      old only in derived :', unexpectedDerived.join(', ') || '—')
    console.log('      new only in frozen  :', cc.newOnlyInFrozen.join(', ') || '—')
    console.log('      new only in derived :', cc.newOnlyInDerived.join(', ') || '—')
  } else {
    console.log('[map] ✓ derived map == frozen Appendix A (ui), plus expected type/const supers:')
    console.log('      ', cc.oldOnlyInDerived.join(', ') || '(none)')
  }

  if (printMap) {
    const rows = [...uiMap.entries(), ...mobileMap.entries()].sort()
    for (const [o, n] of rows) console.log(`  ${o}  →  ${n}`)
    return
  }
  if (!app) {
    console.log('\nSpecify --app <name>. Add --write to apply (default is dry-run).')
    return
  }

  const appSrcDir = resolve(FRONTEND, 'apps', app, 'src')
  if (!existsSync(appSrcDir)) {
    console.error(`app src not found: ${appSrcDir}`)
    process.exit(1)
  }
  // App map for the test-file key/string pass. PDA pulls from both barrels; the PC
  // apps + screen consume only @nerv-iip/ui, so use uiMap alone there (avoids e.g. a
  // 原版 `Badge` key being mis-mapped to the mobile NvMobileBadge).
  const appMap = new Map(uiMap)
  if (app === 'business-pda') for (const [k, v] of mobileMap) appMap.set(k, v)

  const files = walk(appSrcDir, isCodemodTarget).filter((f) => !isGuardTest(f))
  let changedCount = 0
  const allWarnings = []
  const changedFiles = []
  const contentByRel = new Map()
  for (const f of files) {
    const rel = relative(appSrcDir, f).replace(/\\/g, '/')
    let res
    try {
      res = transformFile(f, rel, appMap)
    } catch (err) {
      console.error(`[error] ${rel}: ${err.message}`)
      throw err
    }
    allWarnings.push(...res.warnings)
    contentByRel.set(rel, res.code)
    if (res.changed) {
      changedCount++
      changedFiles.push(rel)
      if (write) writeFileSync(f, res.code)
    }
  }

  // Post-codemod baseline reflects the transformed content (accurate in dry-run too).
  const baseline = regenBaseline(appSrcDir, contentByRel)
  const baselinePath = resolve(appSrcDir, 'nvui-legacy-imports.baseline.json')
  const baselineText = JSON.stringify(baseline, null, 2) + '\n'
  const baselineFileCount = Object.keys(baseline).length
  if (write && existsSync(baselinePath)) {
    writeFileSync(baselinePath, baselineText)
  }

  console.log(
    `\n[${app}] scanned ${files.length} files, ${write ? 'wrote' : 'would change'} ${changedCount}`,
  )
  console.log(
    `[${app}] post-codemod legacy baseline: ${baselineFileCount} file(s) with remaining old names`,
  )
  if (baselineFileCount) {
    for (const k of Object.keys(baseline))
      console.log(`   remaining: ${k} → ${baseline[k].join(', ')}`)
  }
  if (allWarnings.length) {
    console.log(`\n[${app}] ${allWarnings.length} manual-review warning(s):`)
    for (const w of allWarnings) console.log('   ⚠ ' + w)
  }
  if (!write) {
    console.log('\n(dry-run — re-run with --write to apply; baseline would be rewritten too)')
    console.log('changed files:')
    for (const f of changedFiles) console.log('   ' + f)
  }
}

main()
