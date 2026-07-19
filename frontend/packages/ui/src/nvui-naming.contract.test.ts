import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import * as ui from './index'

// ADR 0020 (NvUI) Appendix A — the FULL old→new mapping, frozen. `Nv*` is the canonical
// brand name. The old names (`*Pro` / `Screen*` / bare) were `@deprecated` aliases in
// #787; the #789 closeout REMOVED every alias, so `NV_ALL` must still resolve while
// `OLD_ALL` must no longer resolve, and the barrels must expose zero `@deprecated`.

const srcDir = dirname(fileURLToPath(import.meta.url))
const read = (p: string) => readFileSync(resolve(srcDir, p), 'utf8')
const exported = ui as Record<string, unknown>
const transitionalPcIdentifier = /(?:^|[\s"'`.])ds-/m
const transitionalScreenIdentifier = /(?:^|[\s"'`.])sb-/m

// --- Frozen Appendix A (ui): 182 Nv names (incl. renamed derived types) + 174 old.
// (NvCombobox / NvSearchSelect added post-freeze by MAN-439 — new components, see ADR 0020 Appendix A.)
const NV_ALL = [
  'NvAlarmTable',
  'NvAlertDialog',
  'NvAlertDialogAction',
  'NvAlertDialogCancel',
  'NvAlertDialogContent',
  'NvAlertDialogDescription',
  'NvAlertDialogFooter',
  'NvAlertDialogHeader',
  'NvAlertDialogMedia',
  'NvAlertDialogTitle',
  'NvAlertDialogTrigger',
  'NvApp',
  'NvAppHeader',
  'NvAppShellInset',
  'NvAreaChart',
  'NvBadge',
  'NvBarChart',
  'NvBorderPanel',
  'NvButton',
  'NvCapsuleBar',
  'NvCard',
  'NvCardAction',
  'NvCardContent',
  'NvCardDescription',
  'NvCardFooter',
  'NvCardHeader',
  'NvCardTitle',
  'NvCarousel',
  'NvCheckbox',
  'NvCombobox',
  'NvCommand',
  'NvContainer',
  'NvDataTable',
  'NvDataTableAlign',
  'NvDataTableColumn',
  'NvDataTableDensity',
  'NvDataTableFilterOption',
  'NvDataTableFilters',
  'NvPagination',
  'NvDataTableSort',
  'NvDataTableToolbar',
  'NvDatePicker',
  'NvDateRangePicker',
  'NvDescriptions',
  'NvDialog',
  'NvDialogClose',
  'NvDialogContent',
  'NvDialogDescription',
  'NvDialogFooter',
  'NvDialogHeader',
  'NvDialogTitle',
  'NvDialogTrigger',
  'NvDigitalFlop',
  'NvDonutChart',
  'NvDropdownMenu',
  'NvDropdownMenuCheckboxItem',
  'NvDropdownMenuContent',
  'NvDropdownMenuGroup',
  'NvDropdownMenuItem',
  'NvDropdownMenuLabel',
  'NvDropdownMenuPortal',
  'NvDropdownMenuRadioGroup',
  'NvDropdownMenuRadioItem',
  'NvDropdownMenuSeparator',
  'NvDropdownMenuShortcut',
  'NvDropdownMenuSub',
  'NvDropdownMenuSubContent',
  'NvDropdownMenuSubTrigger',
  'NvDropdownMenuTrigger',
  'NvField',
  'NvFieldContent',
  'NvFieldDescription',
  'NvFieldError',
  'NvFieldGroup',
  'NvFieldLabel',
  'NvFieldLegend',
  'NvFieldSeparator',
  'NvFieldSet',
  'NvFieldTitle',
  'NvFieldVariants',
  'NvFilterBar',
  'NvFormSection',
  'NvGlowDivider',
  'NvInput',
  'NvKanban',
  'NvKpiBar',
  'NvLineChart',
  'NvLoader',
  'NvMetricCard',
  'NvMetricComparison',
  'NvNavigationMenu',
  'NvNavigationMenuContent',
  'NvNavigationMenuIndicator',
  'NvNavigationMenuItem',
  'NvNavigationMenuLink',
  'NvNavigationMenuList',
  'NvNavigationMenuTrigger',
  'NvNavigationMenuViewport',
  'NvNotifierHost',
  'NvOeeHero',
  'NvPage',
  'NvPageAside',
  'NvPageColumns',
  'NvPageGrid',
  'NvPageHeader',
  'NvPageSection',
  'NvPopconfirm',
  'NvQtyStepper',
  'NvRadioGroup',
  'NvRadioGroupItem',
  'NvRecordCard',
  'NvRingGauge',
  'NvRowActions',
  'NvScreenBarChart',
  'NvScreenButton',
  'NvScreenDonut',
  'NvScreenFreshness',
  'NvScreenHeader',
  'NvScreenInput',
  'NvScreenPagination',
  'NvScreenPanel',
  'NvScreenPareto',
  'NvScreenScaler',
  'NvScreenScrollArea',
  'NvScreenSearch',
  'NvScreenSegmented',
  'NvScreenSelect',
  'NvScreenStatusCard',
  'NvScreenStatusLight',
  'NvScreenStatusTag',
  'NvScreenSwitch',
  'NvScreenTable',
  'NvScreenTabs',
  'NvScreenTrendChart',
  'NvScrollBoard',
  'NvSearchSelect',
  'NvSectionCard',
  'NvSectionCards',
  'NvSelect',
  'NvSelectContent',
  'NvSelectGroup',
  'NvSelectItem',
  'NvSelectTrigger',
  'NvSelectValue',
  'NvSheet',
  'NvSheetClose',
  'NvSheetContent',
  'NvSheetDescription',
  'NvSheetFooter',
  'NvSheetHeader',
  'NvSheetTitle',
  'NvSheetTrigger',
  'NvSidebarBrand',
  'NvSidebarDot',
  'NvSidebarSub',
  'NvSidebarUser',
  'NvSlider',
  'NvSparkline',
  'NvStatTile',
  'NvStationBar',
  'NvStatusBadge',
  'NvStatusDot',
  'NvSwitch',
  'NvTabs',
  'NvTabsContent',
  'NvTabsList',
  'NvTabsTrigger',
  'NvTaktGantt',
  'NvTechFrame',
  'NvThemePicker',
  'NvThemeToggle',
  'NvTimePicker',
  'NvTimeline',
  'NvTitleBar',
  'NvToolbar',
  'NvTooltip',
  'NvTooltipContent',
  'NvTooltipProvider',
  'NvTooltipTrigger',
  'NvTouchButton',
  'NvTouchSegmented',
  'nvFieldVariants',
]
// Names exported as types only (not runtime-checkable via the namespace object).
const NV_TYPE_ONLY = new Set([
  'NvDataTableAlign',
  'NvDataTableColumn',
  'NvDataTableDensity',
  'NvDataTableFilterOption',
  'NvDataTableFilters',
  'NvDataTableSort',
  'NvFieldVariants',
])

// Live derivation from the barrels — MUST equal the frozen arrays (bidirectional).
function walk(dir: string, keep: (n: string) => boolean): string[] {
  const out: string[] = []
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const full = resolve(dir, e.name)
    if (e.isDirectory()) out.push(...walk(full, keep))
    else if (keep(e.name)) out.push(full)
  }
  return out
}
const barrels = walk(resolve(srcDir, 'components'), (n) => n === 'index.ts').map((f) =>
  readFileSync(f, 'utf8'),
)
const liveNv = new Set<string>()
const liveOld = new Set<string>()
for (const src of barrels) {
  for (const m of src.matchAll(/(?:default|[A-Za-z0-9_]+)\s+as\s+(Nv[A-Za-z0-9]+)/g))
    liveNv.add(m[1])
  for (const m of src.matchAll(/export\s+(?:const|type)\s+(Nv[A-Za-z0-9]+|nv[A-Za-z0-9]+)\b/g))
    liveNv.add(m[1])
  for (const m of src.matchAll(
    /@deprecated[^\n]*\n\s*(?:default|[A-Za-z0-9_]+)\s+as\s+([A-Za-z0-9_]+)/g,
  ))
    liveOld.add(m[1])
}

describe('NvUI Appendix A full-mapping freeze (@nerv-iip/ui / #787)', () => {
  it('recognizes transitional selector definitions as forbidden identifiers', () => {
    expect(transitionalPcIdentifier.test('.ds-card { color: red; }')).toBe(true)
    expect(transitionalScreenIdentifier.test('.sb-card { color: red; }')).toBe(true)
  })

  it('barrels export exactly the frozen Nv canonical set (Appendix A)', () => {
    expect([...liveNv].sort()).toEqual([...NV_ALL].sort())
  })

  it('barrels expose NO @deprecated old-name aliases (codemod closeout / #789)', () => {
    expect([...liveOld].sort(), 'all @deprecated aliases removed at closeout').toEqual([])
  })

  it('every Nv canonical name actually resolves at runtime (types excluded)', () => {
    for (const n of NV_ALL) {
      if (!NV_TYPE_ONLY.has(n)) expect.soft(exported[n], `${n} should be exported`).toBeDefined()
    }
  })

  it('exposes canonical nvFieldVariants without a transitional variant export', () => {
    expect(exported.nvFieldVariants).toBeDefined()
    expect(exported['field' + 'Pro' + 'Variants']).toBeUndefined()
  })

  it('leaves the shadcn 原版 exports untouched (still exported, un-prefixed)', () => {
    for (const n of ['Button', 'Badge', 'Table', 'Dialog', 'Input', 'Card', 'Select']) {
      expect.soft(exported[n], `原版 ${n} must stay exported`).toBeDefined()
    }
  })

  it('removed the @deprecated aliases from source and deleted the superseded blocks (§1.3)', () => {
    const btn = read('components/pc/button/index.ts')
    expect.soft(btn, 'Nv canonical stays').toContain('NvButton')
    expect.soft(btn, 'no @deprecated alias left').not.toContain('@deprecated')
    const dt = read('components/pc/data-table/index.ts')
    expect.soft(dt, 'renamed PC type stays').toContain('NvDataTableColumn')
    expect.soft(dt, 'no @deprecated alias left').not.toContain('@deprecated')
    // §1.3: superseded block components deleted; blocks barrel drops the data-table line.
    expect
      .soft(
        existsSync(resolve(srcDir, 'components/blocks/data-table')),
        'blocks/data-table deleted',
      )
      .toBe(false)
    expect
      .soft(read('components/blocks/index.ts'), 'blocks barrel drops data-table')
      .not.toContain("'./data-table'")
    expect
      .soft(read('components/blocks/status-badge/index.ts'), 'blocks StatusBadge component removed')
      .not.toContain('StatusBadge.vue')
  })

  it('closes the transitional pro source layer and obsolete table-specific pagination name', () => {
    expect(
      readdirSync(resolve(srcDir, 'components')).some(
        (entry) => entry === ['p', 'r', 'o'].join(''),
      ),
      'transitional PC directory must be removed',
    ).toBe(false)
    expect(existsSync(resolve(srcDir, 'components/pc')), 'components/pc must exist').toBe(true)
    expect(exported.NvPagination, 'canonical PC pagination export').toBeDefined()
    expect(exported['NvDataTable' + 'Pagination'], 'obsolete pagination export').toBeUndefined()
  })

  it('contains no transitional filenames, slots, selectors, or screen tokens in NvUI sources', () => {
    const roots = [srcDir, resolve(srcDir, '../../ui-mobile/src')]
    const files = roots.flatMap((root) =>
      walk(root, (name) => /\.(vue|ts|css)$/.test(name) && !name.includes('.contract.test.')),
    )

    const violations: string[] = []
    for (const file of files) {
      const normalized = file.replace(/\\/g, '/')
      const source = readFileSync(file, 'utf8')
      if (new RegExp('Pro' + '\\.vue$').test(normalized))
        violations.push(`${normalized}: transitional component filename`)
      if (new RegExp('data-slot=[\'"][^\'"]*-' + 'pro').test(source))
        violations.push(`${normalized}: transitional slot`)
      if (transitionalPcIdentifier.test(source))
        violations.push(`${normalized}: transitional PC identifier`)
      if (transitionalScreenIdentifier.test(source))
        violations.push(`${normalized}: transitional screen identifier`)
      if (source.includes('--' + 'sb-')) violations.push(`${normalized}: transitional screen token`)
    }

    expect(violations).toEqual([])
  })
})

// ADR 0020 Decision 4.4 item 5 — machine-enforce "原版零改动": no Nv brand
// identifier, no `--nv-` token, no `nv-` cascade layer may leak into shadcn 原版.
describe('shadcn 原版 purity (components/ui/**)', () => {
  const uiDir = resolve(srcDir, 'components/ui')
  // `file-preview/` lives under components/ui/ but is a CUSTOM Nerv composite (the one
  // allowed `@nerv-iip/ui/file-preview` sub-entry) that composes pro components — it is
  // not shadcn 原版, so it legitimately references NvSelect etc. and is excluded here.
  const files = walk(uiDir, (n) => /\.(vue|ts|css)$/.test(n) && !/\.(test|spec)\./.test(n)).filter(
    (f) => !f.replace(/\\/g, '/').includes('/file-preview/'),
  )

  it('finds原版 source files to guard', () => {
    expect(files.length).toBeGreaterThan(0)
  })

  for (const f of files) {
    const rel = f.slice(uiDir.length + 1).replace(/\\/g, '/')
    it(`${rel} carries no Nv*/--nv-/@layer nv- brand leakage`, () => {
      const src = readFileSync(f, 'utf8')
      expect.soft(src, 'no Nv* brand identifier').not.toMatch(/\bNv[A-Z]/)
      expect.soft(src, 'no --nv- token').not.toContain('--nv-')
      expect.soft(src, 'no nv- cascade layer').not.toMatch(/@layer\s+nv-/)
    })
  }
})
