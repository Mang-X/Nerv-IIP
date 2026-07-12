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

// --- Frozen Appendix A (ui): 181 Nv names (incl. renamed derived types) + 174 old.
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
  'NvDataTablePagination',
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
const OLD_ALL = [
  'AlarmTable',
  'AlertDialogPro',
  'AlertDialogProAction',
  'AlertDialogProCancel',
  'AlertDialogProContent',
  'AlertDialogProDescription',
  'AlertDialogProFooter',
  'AlertDialogProHeader',
  'AlertDialogProMedia',
  'AlertDialogProTitle',
  'AlertDialogProTrigger',
  'App',
  'AppHeader',
  'AppShellInset',
  'AreaChartPro',
  'BadgePro',
  'BarChartPro',
  'BorderPanel',
  'ButtonPro',
  'CapsuleBar',
  'CardPro',
  'CardProAction',
  'CardProContent',
  'CardProDescription',
  'CardProFooter',
  'CardProHeader',
  'CardProTitle',
  'CarouselPro',
  'CheckboxPro',
  'CommandPro',
  'Container',
  'DataTable',
  'DataTablePagination',
  'DataTablePaginationPro',
  'DataTablePro',
  'DataTableToolbarPro',
  'DatePickerPro',
  'DateRangePickerPro',
  'DescriptionsPro',
  'DialogPro',
  'DialogProClose',
  'DialogProContent',
  'DialogProDescription',
  'DialogProFooter',
  'DialogProHeader',
  'DialogProTitle',
  'DialogProTrigger',
  'DigitalFlop',
  'DonutChartPro',
  'DropdownMenuPro',
  'DropdownMenuProCheckboxItem',
  'DropdownMenuProContent',
  'DropdownMenuProGroup',
  'DropdownMenuProItem',
  'DropdownMenuProLabel',
  'DropdownMenuProPortal',
  'DropdownMenuProRadioGroup',
  'DropdownMenuProRadioItem',
  'DropdownMenuProSeparator',
  'DropdownMenuProShortcut',
  'DropdownMenuProSub',
  'DropdownMenuProSubContent',
  'DropdownMenuProSubTrigger',
  'DropdownMenuProTrigger',
  'FieldPro',
  'FieldProContent',
  'FieldProDescription',
  'FieldProError',
  'FieldProGroup',
  'FieldProLabel',
  'FieldProLegend',
  'FieldProSeparator',
  'FieldProSet',
  'FieldProTitle',
  'FilterBarPro',
  'FormSectionPro',
  'GlowDivider',
  'InputPro',
  'KanbanPro',
  'KpiBar',
  'LineChartPro',
  'Loader',
  'MetricCardPro',
  'MetricComparisonPro',
  'NavigationMenuPro',
  'NavigationMenuProContent',
  'NavigationMenuProIndicator',
  'NavigationMenuProItem',
  'NavigationMenuProLink',
  'NavigationMenuProList',
  'NavigationMenuProTrigger',
  'NavigationMenuProViewport',
  'NotifierHost',
  'OeeHero',
  'Page',
  'PageAside',
  'PageColumns',
  'PageGrid',
  'PageHeader',
  'PageSection',
  'PopconfirmPro',
  'QtyStepper',
  'RadioGroupPro',
  'RadioGroupProItem',
  'RecordCardPro',
  'RingGauge',
  'RowActions',
  'ScreenBarChart',
  'ScreenButton',
  'ScreenDonut',
  'ScreenHeader',
  'ScreenInput',
  'ScreenPagination',
  'ScreenPanel',
  'ScreenPareto',
  'ScreenScaler',
  'ScreenScrollArea',
  'ScreenSearch',
  'ScreenSegmented',
  'ScreenSelect',
  'ScreenSwitch',
  'ScreenTable',
  'ScreenTabs',
  'ScrollBoard',
  'SectionCard',
  'SectionCards',
  'SelectPro',
  'SelectProContent',
  'SelectProGroup',
  'SelectProItem',
  'SelectProTrigger',
  'SelectProValue',
  'SheetPro',
  'SheetProClose',
  'SheetProContent',
  'SheetProDescription',
  'SheetProFooter',
  'SheetProHeader',
  'SheetProTitle',
  'SheetProTrigger',
  'SidebarProBrand',
  'SidebarProDot',
  'SidebarProSub',
  'SidebarProUser',
  'SliderPro',
  'Sparkline',
  'StatTile',
  'StationBar',
  'StatusBadge',
  'StatusBadgePro',
  'StatusCard',
  'StatusDot',
  'StatusLight',
  'StatusTag',
  'SwitchPro',
  'TabsPro',
  'TabsProContent',
  'TabsProList',
  'TabsProTrigger',
  'TaktGantt',
  'TechFrame',
  'ThemePicker',
  'ThemeToggle',
  'TimePickerPro',
  'TimelinePro',
  'TitleBar',
  'Toolbar',
  'TooltipPro',
  'TooltipProContent',
  'TooltipProProvider',
  'TooltipProTrigger',
  'TouchButton',
  'TouchSegmented',
  'TrendChart',
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

  it('every old name no longer resolves at runtime (closeout — hard error, not warning)', () => {
    for (const n of OLD_ALL)
      expect.soft(exported[n], `${n} old name must be gone after closeout`).toBeUndefined()
  })

  it('exposes canonical nvFieldVariants; the old fieldProVariants is gone', () => {
    expect(exported.nvFieldVariants).toBeDefined()
    expect(exported.fieldProVariants).toBeUndefined()
  })

  it('leaves the shadcn 原版 exports untouched (still exported, un-prefixed)', () => {
    for (const n of ['Button', 'Badge', 'Table', 'Dialog', 'Input', 'Card', 'Select']) {
      expect.soft(exported[n], `原版 ${n} must stay exported`).toBeDefined()
    }
  })

  it('removed the @deprecated aliases from source and deleted the superseded blocks (§1.3)', () => {
    const btn = read('components/pro/button/index.ts')
    expect.soft(btn, 'Nv canonical stays').toContain('NvButton')
    expect.soft(btn, 'no @deprecated alias left').not.toContain('@deprecated')
    // The `.vue` filenames are unchanged (`from './ButtonPro.vue'`), so assert only the
    // deprecated EXPORT alias (`as ButtonPro`) is gone — not the filename substring.
    expect.soft(btn, 'old ButtonPro export alias removed').not.toMatch(/\bas ButtonPro\b/)
    const dt = read('components/pro/data-table/index.ts')
    expect.soft(dt, 'renamed pro type stays').toContain('NvDataTableColumn')
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
