import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import * as m from './index'

// ADR 0020 (NvUI) Appendix A6 — the FULL mobile old→new mapping, frozen. R2/R3
// clashes with the shadcn 原版 / PC素名 carry a `Mobile` root (Badge→NvMobileBadge,
// Tag→NvMobileTag, DropdownMenu→NvMobileDropdownMenu); mobile-native专名 go straight
// to `Nv*` (NvScanBar, NvCell, NvBottomSheet). The arrays below ARE the contract —
// checked bidirectionally against the barrel and for runtime resolution.

const srcDir = dirname(fileURLToPath(import.meta.url))
const exported = m as Record<string, unknown>

const NV_ALL = [
  'NvActionSheet',
  'NvAppShellMobile',
  'NvBottomSheet',
  'NvCell',
  'NvCellGroup',
  'NvFab',
  'NvInfiniteList',
  'NvListRow',
  'NvMobileAvatar',
  'NvMobileBadge',
  'NvMobileButton',
  'NvMobileCheckbox',
  'NvMobileCollapse',
  'NvMobileDatePicker',
  'NvMobileDialog',
  'NvMobileDivider',
  'NvMobileDropdownMenu',
  'NvMobileDropdownMenuItem',
  'NvMobileEmpty',
  'NvMobileErrorRetry',
  'NvMobileGrid',
  'NvMobileImage',
  'NvMobileInput',
  'NvMobileProgress',
  'NvMobileRadioGroup',
  'NvMobileRadioItem',
  'NvMobileRate',
  'NvMobileResult',
  'NvMobileSkeleton',
  'NvMobileSlider',
  'NvMobileSteps',
  'NvMobileSwitch',
  'NvMobileTabs',
  'NvMobileTag',
  'NvMobileToast',
  'NvNavBar',
  'NvNoticeBar',
  'NvNumberKeyboard',
  'NvPicker',
  'NvPullRefresh',
  'NvScanBar',
  'NvSearchBar',
  'NvStepper',
  'NvSwipeCell',
  'NvSwiper',
  'NvSwiperItem',
  'NvTabBar',
  'NvVirtualList',
]
const OLD_ALL = [
  'ActionSheet',
  'AppShellMobile',
  'Badge',
  'BottomSheet',
  'Cell',
  'CellGroup',
  'Collapse',
  'Divider',
  'DropdownMenu',
  'DropdownMenuItem',
  'Empty',
  'Fab',
  'InfiniteList',
  'ListRow',
  'MobileAvatar',
  'MobileButton',
  'MobileCheckbox',
  'MobileDatePicker',
  'MobileDialog',
  'MobileGrid',
  'MobileImage',
  'MobileInput',
  'MobileProgress',
  'MobileRadioGroup',
  'MobileRadioItem',
  'MobileSkeleton',
  'MobileSlider',
  'MobileSwitch',
  'MobileTabs',
  'MobileToast',
  'NavBar',
  'NoticeBar',
  'NumberKeyboard',
  'Picker',
  'PullRefresh',
  'Rate',
  'Result',
  'ScanBar',
  'SearchBar',
  'Stepper',
  'Steps',
  'SwipeCell',
  'Swiper',
  'SwiperItem',
  'TabBar',
  'Tag',
  'VirtualList',
]

const barrel = readFileSync(resolve(srcDir, 'index.ts'), 'utf8')
const liveNv = new Set<string>()
const liveOld = new Set<string>()
for (const mm of barrel.matchAll(/(?:default|[A-Za-z0-9_]+)\s+as\s+(Nv[A-Za-z0-9]+)/g))
  liveNv.add(mm[1])
for (const mm of barrel.matchAll(
  /@deprecated[^\n]*\n\s*(?:default|[A-Za-z0-9_]+)\s+as\s+([A-Za-z0-9_]+)/g,
))
  liveOld.add(mm[1])

describe('NvUI mobile Appendix A6 full-mapping freeze (@nerv-iip/ui-mobile / #787)', () => {
  it('barrel exports exactly the frozen Nv mobile canonical set (Appendix A6)', () => {
    expect([...liveNv].sort()).toEqual([...NV_ALL].sort())
  })

  it('barrel exposes NO @deprecated old-name aliases (codemod closeout / #789)', () => {
    expect([...liveOld].sort(), 'all @deprecated mobile aliases removed at closeout').toEqual([])
  })

  it('every Nv mobile canonical name actually resolves at runtime', () => {
    for (const n of NV_ALL) expect.soft(exported[n], `${n} should be exported`).toBeDefined()
  })

  it('every old name no longer resolves at runtime (closeout — hard error, not warning)', () => {
    for (const n of OLD_ALL)
      expect.soft(exported[n], `${n} old name must be gone after closeout`).toBeUndefined()
  })

  it('keeps the non-component exports unchanged', () => {
    expect.soft(exported.cn).toBeTypeOf('function')
    expect.soft(exported.MOBILE_OVERLAY_TARGET).toBeDefined()
  })

  it('removed the @deprecated mobile aliases from source; Nv canonicals stay', () => {
    expect.soft(barrel, 'Nv canonical stays').toContain('NvMobileBadge')
    expect.soft(barrel, 'Nv canonical stays').toContain('NvScanBar')
    expect.soft(barrel, 'no @deprecated alias left').not.toContain('@deprecated')
    expect.soft(barrel, 'old Badge alias removed').not.toMatch(/default as Badge\b/)
    expect.soft(barrel, 'old ScanBar alias removed').not.toMatch(/default as ScanBar\b/)
    expect.soft(barrel, 'old DropdownMenu alias removed').not.toMatch(/default as DropdownMenu\b/)
  })
})
