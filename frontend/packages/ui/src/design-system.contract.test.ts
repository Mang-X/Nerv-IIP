import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

// Design System v2 tokens live in the shared, single-source-of-truth theme file
// inside @nerv-iip/ui (consumed by ALL apps). Guard the brand-critical values AND
// the ADR 0020 §3/§4 scene-namespace + cascade-layer isolation contract.
const srcDir = dirname(fileURLToPath(import.meta.url))
const read = (rel: string) => readFileSync(resolve(srcDir, rel), 'utf8')

const themeCss = read('styles/theme.css')
const screenTokens = read('components/screen/tokens.css')
const overridesCss = read('styles/overrides.css')

// App main.css files live outside packages/ui: src → ui → packages → frontend.
const appCss = (app: string) => read(`../../../apps/${app}/src/assets/main.css`)
// Strip CSS block comments so structural assertions ignore prose that documents
// removed constructs (e.g. a comment noting the `revert-layer` hack is gone).
const stripComments = (css: string) => css.replace(/\/\*[\s\S]*?\*\//g, '')
const PRODUCT_APPS = ['business-console', 'business-pda', 'console', 'screen'] as const
const LAYER_ORDER =
  '@layer theme, nv-tokens, base, components, nv-components, utilities, nv-overrides, app;'

describe('design system v2 token contract', () => {
  it('keeps a neutral (black/white) primary as the light/dark baseline', () => {
    // `neutral` theme falls back to these baked-in values: near-black in light,
    // near-white in dark. The runtime theme picker overrides them via inline
    // styles on <html>, but the neutral baseline must stay correct per mode.
    expect.soft(themeCss).toContain('--primary: oklch(0.205 0 0);')
    expect.soft(themeCss).toContain('--primary-foreground: oklch(0.985 0 0);')
    // The old Calm Control Plane blue must never be the baked primary.
    expect.soft(themeCss).not.toContain('--primary: oklch(0.49 0.17 255);')
  })

  it('bridges primary tokens to Tailwind so utilities resolve to var(--primary)', () => {
    // `@theme inline` maps each --color-* to its var(--*). Tailwind then inlines
    // `var(--primary)` into `.bg-primary`, so an inline `--primary` on <html>
    // re-colours every primary utility live. These bridges must exist for the
    // theme picker to drive primary/ring/sidebar.
    expect.soft(themeCss).toContain('@theme inline')
    expect.soft(themeCss).toContain('--color-primary: var(--primary);')
    expect.soft(themeCss).toContain('--color-primary-foreground: var(--primary-foreground);')
    expect.soft(themeCss).toContain('--color-ring: var(--ring);')
    expect.soft(themeCss).toContain('--color-sidebar-primary: var(--sidebar-primary);')
  })

  it('exposes a runtime-overridable brand accent (namespaced --nv-brand)', () => {
    // ADR 0020 §3: the emphasis accent is namespaced `--nv-brand`; the runtime
    // picker sets it inline (see useTheme), the alias `--brand` re-points to it.
    expect.soft(themeCss).toContain('--nv-brand: oklch(0.54 0.16 256);')
    expect.soft(themeCss).toContain('--nv-brand-foreground:')
    expect.soft(themeCss).toContain('--color-brand: var(--nv-brand);')
    // chart-1 (contract name) tracks the dynamic brand via the --nv-* value.
    expect.soft(themeCss).toContain('--chart-1: var(--nv-brand);')
  })

  it('defines success and warning semantic colours (namespaced --nv-*)', () => {
    expect.soft(themeCss).toContain('--nv-success:')
    expect.soft(themeCss).toContain('--nv-success-foreground:')
    expect.soft(themeCss).toContain('--nv-warning:')
    expect.soft(themeCss).toContain('--nv-warning-foreground:')
    expect.soft(themeCss).toContain('--color-success: var(--nv-success);')
    expect.soft(themeCss).toContain('--color-warning: var(--nv-warning);')
  })

  it('provides an elevation scale (background ≠ card + namespaced shadow tokens)', () => {
    expect.soft(themeCss).toContain('--background: oklch(0.985 0 0);')
    expect.soft(themeCss).toContain('--card: oklch(1 0 0);')
    expect.soft(themeCss).toContain('--nv-shadow-sm:')
    expect.soft(themeCss).toContain('--nv-shadow-md:')
    expect.soft(themeCss).toContain('--nv-shadow-lg:')
    // Tailwind `shadow-*` utilities keep their bridge names (right value → --nv-*).
    expect.soft(themeCss).toContain('--shadow-sm: var(--nv-shadow-sm);')
  })

  it('ships a full dark theme override', () => {
    expect.soft(themeCss).toContain('.dark {')
    expect.soft(themeCss).toContain('--primary: oklch(0.922 0 0);')
    expect.soft(themeCss).toContain('color-scheme: dark;')
  })

  it('keeps shared radius and Tailwind theme bridging', () => {
    expect.soft(themeCss).toContain('--radius: 0.5rem;')
    expect.soft(themeCss).toContain('@theme inline')
    expect.soft(themeCss).toContain('@custom-variant dark')
  })
})

// ─── ADR 0020 §3 — scene token namespaces + one-cycle aliases ───────────────
describe('ADR 0020 §3 — token scene namespaces', () => {
  // Appendix B — the full --sb-* → --nv-scr-* screen table (30 tokens).
  const SCREEN = [
    'bg',
    'bg-accent',
    'panel-a',
    'panel-b',
    'line',
    'line-2',
    'divider',
    'cyan',
    'cyan-dim',
    'accent-from',
    'accent-to',
    'accent-fill',
    'accent-edge',
    'indigo',
    'green',
    'amber',
    'red',
    'text',
    'text-2',
    'muted',
    'faint',
    'highlight',
    'edge-gradient',
    'value-glow',
    'edge-glow',
    'glow',
    'sheen',
    'radius',
    'ease',
    'ease-emphasized',
  ]

  it('defines the full --nv-scr-* screen namespace (Appendix B, 30 tokens)', () => {
    for (const t of SCREEN) expect.soft(screenTokens).toMatch(new RegExp(`--nv-scr-${t}\\b`))
  })

  it('keeps one-cycle --sb-* aliases that var-chain to --nv-scr-* (§5 S1)', () => {
    for (const t of SCREEN) {
      expect.soft(screenTokens).toContain(`--sb-${t}: var(--nv-scr-${t});`)
    }
  })

  it('expresses cross-scene equal values as var chains, not literals (§3.2)', () => {
    // Motion values live ONCE in the shared Nv layer; the screen scene references
    // them instead of copying cubic-bezier literals.
    expect.soft(screenTokens).toContain('--nv-scr-ease: var(--nv-ease-out-quart);')
    expect.soft(screenTokens).toContain('--nv-scr-ease-emphasized: var(--nv-ease-out-expo);')
    // The screen scene sheet must NOT re-declare a raw ease cubic-bezier.
    expect.soft(screenTokens).not.toMatch(/--nv-scr-ease:\s*cubic-bezier/)
  })

  it('keeps semantic aliases pointing old names at the --nv-* values (§5 S1)', () => {
    expect.soft(themeCss).toContain('--brand: var(--nv-brand);')
    expect.soft(themeCss).toContain('--success: var(--nv-success);')
    expect.soft(themeCss).toContain('--ease-out-quart: var(--nv-ease-out-quart);')
    expect.soft(themeCss).toContain('--shadow-glow-brand: var(--nv-shadow-glow-brand);')
  })

  it('freezes the contract layer — those names are never prefixed', () => {
    for (const frozen of [
      '--background',
      '--foreground',
      '--primary',
      '--border',
      '--ring',
      '--radius',
    ]) {
      expect.soft(themeCss).not.toContain(`--nv-${frozen.slice(2)}:`)
    }
  })
})

// ─── ADR 0020 §4 — CSS cascade-layer isolation ─────────────────────────────
describe('ADR 0020 §4 — cascade-layer isolation', () => {
  it('declares the full global layer order first in every product app main.css', () => {
    for (const app of PRODUCT_APPS) {
      const css = appCss(app)
      // First non-comment statement must be the exact layer order.
      const firstStmt = css
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .trim()
        .split('\n')[0]
        .trim()
      expect.soft(firstStmt, `${app} main.css first statement`).toBe(LAYER_ORDER)
    }
  })

  it('wraps the library token table + reset + overlay motion in layers', () => {
    expect.soft(themeCss).toContain('@layer nv-tokens {')
    expect.soft(themeCss).toContain('@layer base {')
    expect.soft(themeCss).toContain('@layer nv-components {')
    // :root / .dark token declarations sit INSIDE nv-tokens (not unlayered).
    expect.soft(themeCss.indexOf('@layer nv-tokens {')).toBeLessThan(themeCss.indexOf(':root {'))
    expect.soft(screenTokens).toContain('@layer nv-tokens {')
    expect.soft(screenTokens).toContain('@layer nv-components {')
  })

  it('keeps overrides.css unlayered (host decides the layer at import point)', () => {
    // The file itself must not declare a layer — apps import it `layer(nv-overrides)`,
    // the docs import it plain. (ADR 0020 §4.3.)
    expect.soft(stripComments(overridesCss)).not.toContain('@layer')
    // Product apps import it into nv-overrides.
    for (const app of PRODUCT_APPS) {
      expect.soft(appCss(app)).toMatch(/overrides\.css'\s*layer\(nv-overrides\)/)
    }
  })

  const uiSrc = resolve(srcDir) // packages/ui/src
  const mobileSrc = resolve(srcDir, '../../ui-mobile/src')
  const vueFiles = (root: string): string[] => {
    const out: string[] = []
    const walk = (d: string) => {
      for (const e of readdirSync(d, { withFileTypes: true })) {
        if (e.name === 'node_modules' || e.name === 'dist') continue
        const p = join(d, e.name)
        if (e.isDirectory()) walk(p)
        else if (e.name.endsWith('.vue')) out.push(p)
      }
    }
    walk(root)
    return out
  }
  const norm = (p: string) => relative(uiSrc, p).replace(/\\/g, '/')
  // 原版 iff under components/ui/ but NOT the (self-owned) file-preview subsystem.
  const isVendor = (p: string) => {
    const r = norm(p)
    return r.startsWith('components/ui/') && !r.includes('/file-preview/')
  }
  const styleBodies = (src: string): string[] =>
    [...src.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style>/g)].map((m) => m[1])
  // True iff `body` (comments stripped) is EXACTLY one `@layer nv-components {…}`
  // block whose closing brace is the last token — i.e. no rule sits outside the
  // layer, and it is not an empty layer with sibling rules.
  const isFullyLayered = (body: string): boolean => {
    const s = body.replace(/\/\*[\s\S]*?\*\//g, '').trim()
    if (!s) return true // empty style block: nothing to layer
    if (!/^@layer\s+nv-components\s*\{/.test(s)) return false
    const open = s.indexOf('{')
    let depth = 0
    for (let i = open; i < s.length; i++) {
      if (s[i] === '{') depth++
      else if (s[i] === '}' && --depth === 0) return i === s.length - 1
    }
    return false
  }
  const allSfcs = [...vueFiles(resolve(uiSrc, 'components')), ...vueFiles(mobileSrc)]

  it('confines EVERY self-owned SFC style rule to @layer nv-components (§4.1)', () => {
    // Recursive, and asserts CONTAINMENT (every rule inside the layer) — not merely
    // that the marker appears. A rule added outside the wrapper, or an empty layer
    // with sibling rules, fails here.
    for (const file of allSfcs) {
      if (isVendor(file)) continue
      for (const body of styleBodies(readFileSync(file, 'utf8'))) {
        expect
          .soft(
            isFullyLayered(body),
            `self-owned ${norm(file)} — all rules must sit in @layer nv-components`,
          )
          .toBe(true)
      }
    }
  })

  it('keeps true原版 shadcn primitives free of Nv / @layer nv- / --nv- markers (§4.4.5)', () => {
    // file-preview/* is self-owned and excluded; the rest of components/ui/ is
    // pristine shadcn and must carry none of the three brand markers.
    for (const file of allSfcs) {
      if (!isVendor(file)) continue
      const src = readFileSync(file, 'utf8')
      expect.soft(src, `原版 ${norm(file)} @layer`).not.toContain('@layer nv-')
      expect.soft(src, `原版 ${norm(file)} --nv-`).not.toContain('--nv-')
      expect.soft(src, `原版 ${norm(file)} Nv*`).not.toMatch(/\bNv[A-Z]/)
    }
  })

  it('bans inline cubic-bezier literals in self-owned SFCs — curves come from Nv tokens only (§3.2)', () => {
    // Curves are defined ONCE in theme.css (--nv-ease-*); components reference the
    // token with no literal fallback. (theme.css/tokens.css are not .vue and hold
    // the single source, so they are outside this scan.)
    for (const file of allSfcs) {
      expect
        .soft(readFileSync(file, 'utf8'), `${norm(file)} inline cubic-bezier`)
        .not.toMatch(/cubic-bezier/)
    }
  })
})

// ─── ADR 0020 §4.2 — VitePress doc-site style isolation ────────────────────
describe('ADR 0020 §4.2 — VitePress isolation', () => {
  const ds = (rel: string) => read(`../../../apps/design-system/${rel}`)

  it('enables postcssIsolateStyles for base.css AND vp-doc.css', () => {
    const config = ds('docs/.vitepress/config.mts')
    expect.soft(config).toContain('postcssIsolateStyles')
    expect.soft(config).toMatch(/includeFiles:\s*\[[^\]]*base\\.css[^\]]*vp-doc\\.css/)
  })

  it('wraps every demo container root in vp-raw', () => {
    expect.soft(ds('docs/.vitepress/theme/Demo.vue')).toContain('ds-demo vp-raw')
    expect.soft(ds('docs/.vitepress/theme/ScreenDemo.vue')).toContain('ds-sd vp-raw')
    expect.soft(ds('docs/.vitepress/theme/MobileDoc.vue')).toContain('ds-mdoc-phone vp-raw')
  })

  it('drops the old .vp-doc counter-hacks and never revives revert-layer', () => {
    const style = stripComments(ds('docs/.vitepress/theme/style.css'))
    expect.soft(style).not.toMatch(/\.vp-doc\s*:is\(\.(sb|nv-scr)-tbl/)
    expect.soft(style).not.toContain('revert-layer')
    // Doc site pulls host overrides (glass) in unlayered so they win in-site.
    expect.soft(style).toContain("overrides.css'")
  })
})
