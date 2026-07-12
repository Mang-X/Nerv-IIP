// Cross-surface component family map (MAN-437 / #791).
//
// The design system ships FOUR surfaces (ADR 0020 four-surface map): PC 桌面,
// PDA 移动, 一体机 touch, 大屏 screen. A single UX concept (a button, a header, a
// data table) is realized once per surface with surface-native sizing/tokens —
// never "one component, two modes". This map records those correspondences so the
// per-page scene-availability badge (`<SceneBadge>`) can show, for any component,
// which surface layer owns it and link across to the counterpart on other surfaces.
//
// Only families with ≥2 surfaces are listed. A component page whose (surface, slug)
// is NOT found here is surface-exclusive — the badge shows its owning surface active
// and the other three as unavailable (no counterpart), which is the honest state.

export type Surface = 'desktop' | 'mobile' | 'touch' | 'screen'

export interface SurfaceMeta {
  /** URL segment under /components/ */
  seg: string
  /** short label shown in the badge chip */
  label: string
  /** the package / layer that owns this surface */
  pkg: string
}

// Badge chip order = the four-scene matrix order used in foundations/tokens.md
// (PC → mobile → touch → screen).
export const SURFACES: Surface[] = ['desktop', 'mobile', 'touch', 'screen']

export const SURFACE_META: Record<Surface, SurfaceMeta> = {
  desktop: { seg: 'desktop', label: '桌面 PC', pkg: '@nerv-iip/ui · pro/blocks/layout' },
  mobile: { seg: 'mobile', label: 'PDA 移动', pkg: '@nerv-iip/ui-mobile' },
  touch: { seg: 'touch', label: '一体机', pkg: '@nerv-iip/ui · touch/' },
  screen: { seg: 'screen', label: '大屏', pkg: '@nerv-iip/ui · screen/' },
}

export interface FamilyMember {
  slug: string
  /** canonical Nv* component name — shown as the chip tooltip on counterpart chips */
  name?: string
  /**
   * Link-only cross-reference: this family should LINK to the page, but the page's
   * OWN family (for its badge) lives elsewhere. Used when one component realizes two
   * concepts on different surfaces — e.g. `NvTabs` is the PC "标签页" AND the PC way to
   * do "分段切换" (touch/screen use a dedicated Segmented). `ref` members are excluded
   * from the reverse index so the shared page keeps its primary family.
   */
  ref?: boolean
}

export type Family = Partial<Record<Surface, FamilyMember>>

// Each entry = one UX concept across surfaces. (surface, slug) must be unique
// across the whole list so the reverse index below has no collisions.
export const FAMILIES: Family[] = [
  {
    desktop: { slug: 'button', name: 'NvButton' },
    mobile: { slug: 'button', name: 'NvMobileButton' },
    touch: { slug: 'touch-button', name: 'NvTouchButton' },
    screen: { slug: 'screen-button', name: 'NvScreenButton' },
  },
  {
    desktop: { slug: 'app', name: 'NvAppHeader' },
    mobile: { slug: 'nav-bar', name: 'NvNavBar' },
    touch: { slug: 'station-bar', name: 'NvStationBar' },
    screen: { slug: 'screen-header', name: 'NvScreenHeader' },
  },
  {
    desktop: { slug: 'input', name: 'NvInput' },
    mobile: { slug: 'input', name: 'NvMobileInput' },
    screen: { slug: 'screen-input', name: 'NvScreenInput' },
  },
  {
    desktop: { slug: 'switch', name: 'NvSwitch' },
    mobile: { slug: 'switch', name: 'NvMobileSwitch' },
    screen: { slug: 'screen-switch', name: 'NvScreenSwitch' },
  },
  {
    // 标签页 / 分段切换 is one cross-surface concept split across two components:
    // Tabs on PC/mobile, a dedicated Segmented on touch, and BOTH on screen. Each
    // component keeps its own page/family; the two families cross-link via `ref` so
    // every page shows all four surfaces (touch's counterpart is the Segmented control).
    desktop: { slug: 'tabs', name: 'NvTabs' },
    mobile: { slug: 'tabs', name: 'NvMobileTabs' },
    touch: { slug: 'touch-segmented', name: 'NvTouchSegmented', ref: true },
    screen: { slug: 'screen-tabs', name: 'NvScreenTabs' },
  },
  {
    desktop: { slug: 'select', name: 'NvSelect' },
    mobile: { slug: 'picker', name: 'NvPicker' },
    screen: { slug: 'screen-select', name: 'NvScreenSelect' },
  },
  {
    desktop: { slug: 'badge', name: 'NvBadge' },
    mobile: { slug: 'badge', name: 'NvMobileBadge' },
  },
  {
    desktop: { slug: 'data-table', name: 'NvDataTable' },
    screen: { slug: 'screen-table', name: 'NvScreenTable' },
  },
  {
    desktop: { slug: 'checkbox', name: 'NvCheckbox' },
    mobile: { slug: 'checkbox', name: 'NvMobileCheckbox' },
  },
  {
    desktop: { slug: 'radio', name: 'NvRadioGroup' },
    mobile: { slug: 'radio', name: 'NvMobileRadioGroup' },
  },
  {
    desktop: { slug: 'slider', name: 'NvSlider' },
    mobile: { slug: 'slider', name: 'NvMobileSlider' },
  },
  {
    desktop: { slug: 'date-picker', name: 'NvDatePicker' },
    mobile: { slug: 'date-picker', name: 'NvMobileDatePicker' },
  },
  {
    desktop: { slug: 'dialog', name: 'NvDialog' },
    mobile: { slug: 'dialog', name: 'NvMobileDialog' },
  },
  {
    desktop: { slug: 'dropdown-menu', name: 'NvDropdownMenu' },
    mobile: { slug: 'dropdown-menu', name: 'NvMobileDropdownMenu' },
  },
  {
    desktop: { slug: 'sheet', name: 'NvSheet' },
    mobile: { slug: 'bottom-sheet', name: 'NvBottomSheet' },
  },
  {
    desktop: { slug: 'command', name: 'NvCommand' },
    mobile: { slug: 'search-bar', name: 'NvSearchBar' },
    screen: { slug: 'screen-search', name: 'NvScreenSearch' },
  },
  {
    // Dedicated segmented control (touch/screen). PC/mobile realize 分段切换 with Tabs,
    // linked here via `ref` (Tabs' own family is 标签页 above) so these pages no longer
    // show PC/mobile as "暂无对应件".
    desktop: { slug: 'tabs', name: 'NvTabs', ref: true },
    mobile: { slug: 'tabs', name: 'NvMobileTabs', ref: true },
    touch: { slug: 'touch-segmented', name: 'NvTouchSegmented' },
    screen: { slug: 'screen-segmented', name: 'NvScreenSegmented' },
  },
  {
    mobile: { slug: 'stepper', name: 'NvStepper' },
    touch: { slug: 'qty-stepper', name: 'NvQtyStepper' },
  },
  {
    desktop: { slug: 'dashboard', name: 'NvAppShellInset' },
    mobile: { slug: 'app-shell-mobile', name: 'NvAppShellMobile' },
  },
  {
    mobile: { slug: 'tag', name: 'NvMobileTag' },
    screen: { slug: 'status-tag', name: 'NvScreenStatusTag' },
  },
  {
    desktop: { slug: 'status', name: 'NvStatusDot' },
    screen: { slug: 'status-light', name: 'NvScreenStatusLight' },
  },
  {
    touch: { slug: 'stat-tile', name: 'NvStatTile' },
    screen: { slug: 'kpi-bar', name: 'NvKpiBar' },
  },
  {
    desktop: { slug: 'chart', name: 'NvBarChart' },
    screen: { slug: 'screen-bar-chart', name: 'NvScreenBarChart' },
  },
  {
    desktop: { slug: 'carousel', name: 'NvCarousel' },
    mobile: { slug: 'swiper', name: 'NvSwiper' },
  },
  {
    mobile: { slug: 'divider', name: 'NvMobileDivider' },
    screen: { slug: 'glow-divider', name: 'NvGlowDivider' },
  },
  {
    desktop: { slug: 'notify', name: 'NvNotifierHost' },
    mobile: { slug: 'toast', name: 'NvMobileToast' },
  },
  {
    desktop: { slug: 'card', name: 'NvCard' },
    screen: { slug: 'screen-panel', name: 'NvScreenPanel' },
  },
]

// Pages that live under /components/<surface>/ but are NOT a single component
// (surface landing, full-example gallery, the touch board showcase). The badge
// skips these.
const NON_COMPONENT_SLUGS = new Set(['index', 'gallery', 'overview', 'board'])

const reverse = new Map<string, Family>()
for (const fam of FAMILIES) {
  for (const s of SURFACES) {
    const m = fam[s]
    // Skip `ref` (link-only) members: the page belongs to its primary family, not
    // this one, so it must not be overwritten in the page→family reverse index.
    if (m && !m.ref) reverse.set(`${s}/${m.slug}`, fam)
  }
}

export interface BadgeResolution {
  surface: Surface
  family: Family | null
}

/**
 * Resolve a VitePress `relativePath` (e.g. `components/desktop/button.md`) to the
 * owning surface and cross-surface family. Returns null for non-component pages.
 */
export function resolveScene(relativePath: string): BadgeResolution | null {
  const m = /^components\/(desktop|mobile|touch|screen)\/(.+?)(?:\.md)?$/.exec(relativePath)
  if (!m) return null
  const surface = m[1] as Surface
  const slug = m[2].replace(/\/index$/, 'index')
  if (NON_COMPONENT_SLUGS.has(slug)) return null
  return { surface, family: reverse.get(`${surface}/${slug}`) ?? null }
}
