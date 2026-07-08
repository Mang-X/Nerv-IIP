import { dirname, resolve } from 'node:path'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import * as m from './index'

// ADR 0020 (NvUI) Appendix A6 — mobile canonical names. R2/R3 clashes with the
// shadcn 原版 / PC素名 carry a `Mobile` root (Badge→NvMobileBadge,
// Tag→NvMobileTag, DropdownMenu→NvMobileDropdownMenu); mobile-native专名 go
// straight to `Nv*` (NvScanBar, NvCell, NvBottomSheet). Old names stay exported
// as @deprecated aliases this batch (#787, zero-breakage).

const srcDir = dirname(fileURLToPath(import.meta.url))
const exported = m as Record<string, unknown>

const NV_MOBILE = [
  'NvAppShellMobile',
  'NvScanBar',
  'NvListRow',
  'NvBottomSheet',
  'NvMobileResult',
  'NvNavBar',
  'NvCell',
  'NvCellGroup',
  'NvTabBar',
  'NvNoticeBar',
  'NvMobileButton',
  'NvStepper',
  'NvMobileSwitch',
  'NvMobileInput',
  'NvMobileRadioGroup',
  'NvMobileRadioItem',
  'NvSearchBar',
  'NvMobileTabs',
  'NvMobileSteps',
  'NvMobileCheckbox',
  'NvMobileEmpty',
  'NvMobileBadge',
  'NvActionSheet',
  'NvMobileCollapse',
  'NvSwipeCell',
  'NvPicker',
  'NvPullRefresh',
  'NvInfiniteList',
  'NvVirtualList',
  'NvMobileDatePicker',
  'NvMobileDialog',
  'NvMobileGrid',
  'NvFab',
  'NvMobileToast',
  'NvMobileDivider',
  'NvMobileAvatar',
  'NvMobileTag',
  'NvMobileSkeleton',
  'NvMobileProgress',
  'NvMobileRate',
  'NvMobileSlider',
  'NvNumberKeyboard',
  'NvSwiper',
  'NvSwiperItem',
  'NvMobileImage',
  'NvMobileDropdownMenu',
  'NvMobileDropdownMenuItem',
]
const LEGACY_STILL_EXPORTED = [
  'AppShellMobile',
  'ScanBar',
  'Cell',
  'Badge',
  'Empty',
  'DropdownMenu',
  'DropdownMenuItem',
  'Tag',
  'Rate',
  'Steps',
  'Result',
  'MobileButton',
  'Picker',
  'BottomSheet',
]

describe('NvUI mobile canonical name contract (ADR 0020 A6 / #787)', () => {
  it('exports every Nv* mobile canonical name', () => {
    for (const n of NV_MOBILE) expect.soft(exported[n], `${n} should be exported`).toBeDefined()
  })

  it('keeps every old name exported for the zero-breakage transition', () => {
    for (const n of LEGACY_STILL_EXPORTED) {
      expect.soft(exported[n], `${n} old name must still resolve`).toBeDefined()
    }
  })

  it('keeps the non-component exports unchanged', () => {
    expect.soft(exported.cn).toBeTypeOf('function')
    expect.soft(exported.MOBILE_OVERLAY_TARGET).toBeDefined()
  })

  it('marks old aliases @deprecated and applies the Mobile-root R2/R3 rule in source', () => {
    const src = readFileSync(resolve(srcDir, 'index.ts'), 'utf8')
    expect.soft(src).toContain('@deprecated')
    // R2 clashes with 原版 → Mobile root
    expect.soft(src).toMatch(/NvMobileBadge[\S\s]*?default as Badge/)
    expect.soft(src).toMatch(/NvMobileDropdownMenu[\S\s]*?default as DropdownMenu/)
    // mobile-native专名 → straight Nv
    expect.soft(src).toMatch(/NvScanBar[\S\s]*?default as ScanBar/)
    expect.soft(src).toMatch(/NvCell[\S\s]*?default as Cell/)
  })
})
