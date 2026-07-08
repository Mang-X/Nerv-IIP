import { readdirSync, readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import * as ui from './index'

// ADR 0020 (NvUI) Appendix A — `Nv*` is the canonical brand name; the old name
// (`*Pro` / `Screen*` / bare) is kept only as a @deprecated alias this batch
// (#787). This contract freezes the "new + old coexist, zero-breakage" state
// and the "shadcn 原版 stay pure" invariant (Decision 4.4 item 5).

const srcDir = dirname(fileURLToPath(import.meta.url))
const read = (p: string) => readFileSync(resolve(srcDir, p), 'utf8')
const exported = ui as Record<string, unknown>

const NV_PRO = [
  'NvButton',
  'NvBadge',
  'NvCard',
  'NvMetricCard',
  'NvDataTable',
  'NvDataTablePagination',
  'NvDataTableToolbar',
  'NvDialog',
  'NvDialogContent',
  'NvDropdownMenu',
  'NvField',
  'NvSelect',
  'NvSelectItem',
  'NvSheet',
  'NvTabs',
  'NvTooltip',
  'NvStatusDot',
  'NvStatusBadge',
  'NvNotifierHost',
  'NvLoader',
  'NvRadioGroup',
  'NvNavigationMenu',
  'NvAreaChart',
  'NvLineChart',
  'NvDonutChart',
  'NvSidebarBrand',
  'NvCheckbox',
  'NvSwitch',
  'NvSlider',
  'NvTimeline',
  'NvKanban',
  'NvCommand',
  'NvDatePicker',
  'NvDescriptions',
  'NvFilterBar',
  'NvPopconfirm',
]
const NV_BLOCKS_LAYOUT = [
  'NvPageHeader',
  'NvSectionCard',
  'NvSectionCards',
  'NvToolbar',
  'NvRowActions',
  'NvThemePicker',
  'NvThemeToggle',
  'NvAppShellInset',
  'NvApp',
  'NvAppHeader',
  'NvContainer',
  'NvPage',
  'NvPageAside',
  'NvPageGrid',
  'NvPageColumns',
  'NvPageSection',
]
const NV_SCREEN_TOUCH = [
  'NvScreenPanel',
  'NvScrollBoard',
  'NvOeeHero',
  'NvRingGauge',
  'NvDigitalFlop',
  'NvSparkline',
  'NvScreenTrendChart',
  'NvScreenBarChart',
  'NvScreenDonut',
  'NvTaktGantt',
  'NvScreenStatusCard',
  'NvKpiBar',
  'NvAlarmTable',
  'NvScreenHeader',
  'NvTechFrame',
  'NvTitleBar',
  'NvScreenStatusLight',
  'NvScreenStatusTag',
  'NvScreenButton',
  'NvScreenPagination',
  'NvTouchButton',
  'NvStatTile',
  'NvQtyStepper',
  'NvTouchSegmented',
  'NvStationBar',
]
// Old names that must still resolve (business code compiles unchanged this batch).
const LEGACY_STILL_EXPORTED = [
  'ButtonPro',
  'DataTablePro',
  'DataTablePaginationPro',
  'App',
  'Page',
  'OeeHero',
  'TaktGantt',
  'TouchButton',
  'ScreenButton',
  'StatusDot',
  'StatusBadgePro',
  'PageHeader',
  'SectionCard',
  // §1.3 superseded blocks — kept @deprecated, no Nv name of their own:
  'DataTable',
  'DataTablePagination',
  'StatusBadge',
]

describe('NvUI canonical name contract (ADR 0020 Appendix A / #787)', () => {
  it('exports Nv* canonical names for the pro layer', () => {
    for (const n of NV_PRO) expect.soft(exported[n], `${n} should be exported`).toBeDefined()
  })

  it('exports Nv* canonical names for the blocks + layout layers', () => {
    for (const n of NV_BLOCKS_LAYOUT)
      expect.soft(exported[n], `${n} should be exported`).toBeDefined()
  })

  it('exports Nv* canonical names for the screen + touch layers', () => {
    for (const n of NV_SCREEN_TOUCH)
      expect.soft(exported[n], `${n} should be exported`).toBeDefined()
  })

  it('exposes a canonical nvFieldVariants aliasing the old fieldProVariants', () => {
    expect(exported.nvFieldVariants).toBeDefined()
    expect(exported.nvFieldVariants).toBe(exported.fieldProVariants)
  })

  it('keeps every old name exported for the zero-breakage transition', () => {
    for (const n of LEGACY_STILL_EXPORTED) {
      expect.soft(exported[n], `${n} old name must still resolve`).toBeDefined()
    }
  })

  it('leaves the shadcn 原版 exports untouched (still exported, un-prefixed)', () => {
    for (const n of ['Button', 'Badge', 'Table', 'Dialog', 'Input', 'Card', 'Select']) {
      expect.soft(exported[n], `原版 ${n} must stay exported`).toBeDefined()
    }
  })

  it('marks old aliases @deprecated and lands renamed derived types in source', () => {
    const btn = read('components/pro/button/index.ts')
    expect.soft(btn).toContain('NvButton')
    expect.soft(btn).toContain('@deprecated')

    const dt = read('components/pro/data-table/index.ts')
    expect.soft(dt, 'renamed pro type present').toContain('NvDataTableColumn')
    expect.soft(dt).toMatch(/@deprecated[\S\s]*?DataTablePro/)

    const field = read('components/pro/field/index.ts')
    expect.soft(field).toContain('nvFieldVariants')
    expect.soft(field).toContain('NvFieldVariants')

    const screen = read('components/screen/index.ts')
    expect.soft(screen, 'R3b scene-root rename').toContain('NvScreenTrendChart')
    expect.soft(screen).toContain('@deprecated')

    const blocksDt = read('components/blocks/data-table/index.ts')
    expect.soft(blocksDt, '§1.3 superseded → NvDataTable').toMatch(/@deprecated[\S\s]*?NvDataTable/)
  })
})

// ADR 0020 Decision 4.4 item 5 — machine-enforce "原版零改动": no Nv brand
// identifier, no `--nv-` token, no `nv-` cascade layer may leak into shadcn 原版.
describe('shadcn 原版 purity (components/ui/**)', () => {
  const uiDir = resolve(srcDir, 'components/ui')
  function walk(dir: string): string[] {
    const out: string[] = []
    for (const e of readdirSync(dir, { withFileTypes: true })) {
      const full = resolve(dir, e.name)
      if (e.isDirectory()) out.push(...walk(full))
      else if (/\.(vue|ts|css)$/.test(e.name) && !/\.(test|spec)\./.test(e.name)) out.push(full)
    }
    return out
  }
  const files = walk(uiDir)

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
