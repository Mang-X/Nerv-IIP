# Design

Visual system for the Nerv-IIP design system. Source of truth for tokens is
`frontend/packages/ui/src/styles/theme.css`; never hard-code values, reference
semantic tokens. Live docs: the VitePress site under
`frontend/apps/design-system/docs`.

## Theme

Dark-first industrial console (near-black base), with a fully supported light
mode via the `.dark` class on `<html>`. Restrained Vercel/Linear craft: low
chroma surfaces, hairline borders, one dynamic accent. Color strategy:
**restrained** — tinted neutrals + a single brand accent.

## Color (OKLCH semantic tokens)

- Surfaces (dark): `--background` `oklch(0.145 0 0)` → `--card` / `--popover`
  `oklch(0.205 0 0)` → `--muted` `oklch(0.269 0 0)`. Light mode mirrors with high-L values.
- Text: `--foreground` (near-white in dark), `--muted-foreground`, plus
  high-contrast `--*-strong` variants for emphasis.
- Brand: `--brand` is a **runtime dynamic accent** written to `<html>` (default
  `oklch(0.54 0.16 256)` blue); `--brand-strong` / `--brand-foreground`.
  Presets are a 12-hue equal-lightness OKLCH wheel.
- Semantic: `--success` `--warning` `--destructive` (de-saturated, fixed; do not
  participate in the dynamic accent). Lines: `--border` `--ring`.
- Rule: gray-on-tinted body text is banned; bump toward ink. Body ≥4.5:1.

## Typography

- Family: Inter (sans) as base; mono for codes (WO-/WC-) and tabular numbers.
  ≤3 families.
- Hierarchy via scale + weight (semibold headings, regular body). Display
  letter-spacing ≥ -0.04em; hero clamp max ≤6rem. `text-wrap: balance` on headings.
- Numbers use `tabular-nums`. No all-caps body; uppercase only for short labels.

## Motion

Easing tokens in theme.css: `--ease-out-quart` (general), `--ease-out-expo`
(slide/snap/spring), `--ease-in-out-quart` (indicators). No bounce/elastic.
Shared rubber-band drag curve `abs(x)^0.92*0.7` across SwipeCell / BottomSheet /
PullRefresh. Touch feedback prefers background/opacity over `transform: scale`
(no layout shift). Every animation has a `prefers-reduced-motion` fallback.

## Components

Three layers, all token-driven, **never editing原版** shadcn/reka:
- `@nerv-iip/ui` — desktop/PC: shadcn/reka base + **Pro** copy-rebuilt premium
  components (Button/Input/Select/DataTable/Descriptions/Timeline/Tabs/Dialog/
  Popconfirm/Tooltip/charts…), **blocks** (app-shell, page-header, section-card,
  toolbar, data-table), **layout** (Container/Page/PageGrid/PageColumns/
  PageSection), **touch** (StationBar/StatTile/QtyStepper for kiosks).
- `@nerv-iip/ui-mobile` — PDA: native-feel controls with gestures (swipe-cell,
  pull-refresh, bottom-sheet drag-dismiss), safe-area + ≥44px targets, glass overlays.
- Cards top out at 12–16px radius; hairline `0 0 0 1px var(--border)` ring +
  faint inset top highlight, not border+heavy-shadow. Overlays: subtle glass
  (translucent + backdrop-blur) only.

## Layout

12-col-friendly grids; `Container` constrains width with responsive gutters;
`Page` does a 10-col content+asides grid. Flex for 1D, Grid for 2D. Semantic
z-index scale (dropdown → sticky → modal-backdrop → modal → toast → tooltip).
