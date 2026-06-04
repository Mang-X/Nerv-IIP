---
# Foundation — Nerv-IIP Design System v2
# Black-primary, dashboard-01 baseline: professional, information-dense, light + dark, dynamic accent.
---

## Style Intent

An information-dense enterprise control plane aligned to shadcn **dashboard-01**:
near-black primary, neutral chrome, **light + dark first-class**, and a **runtime
dynamic accent** (brand blue used for emphasis only). Page canvas sits a notch
below card surfaces (inset floating panels) with a `--shadow-*` elevation scale.

Tokens are a single source of truth in `@nerv-iip/ui` — `packages/ui/src/styles/theme.css`
— imported by both `apps/console` and `apps/business-console`. Never duplicate token
values per app.

### Hard rules (epic #275 / FE-1 #276)

1. `--primary` is **near-black** (`oklch(0.205 0 0)`), not the retired blue.
2. Brand blue lives in `--brand` and is **emphasis only** + **runtime-overridable**.
3. Light and dark are both shipped (`.dark` override in `theme.css`).
4. **Never edit原版 shadcn-vue components.** They are re-pulled verbatim from the
   official `reka-nova` registry and may be overwritten on re-pull. Any customization
   is a copy-rebuilt component (FE-2), never an edit to a primitive.

---

## Colors

### Semantic tokens (all code must use these — never raw hex or raw Tailwind palette names)

| Token | Light value | Dark value | Purpose |
|---|---|---|---|
| `--background` | `oklch(0.985 0 0)` | `oklch(0.145 0 0)` | Page canvas (below cards) |
| `--foreground` | `oklch(0.145 0 0)` | `oklch(0.985 0 0)` | Body text |
| `--card` | `oklch(1 0 0)` | `oklch(0.205 0 0)` | Card / inset panel surface |
| `--muted` | `oklch(0.97 0 0)` | `oklch(0.269 0 0)` | Subdued surface (table stripe, hover) |
| `--muted-foreground` | `oklch(0.556 0 0)` | `oklch(0.708 0 0)` | Secondary text, placeholders |
| `--border` | `oklch(0.922 0 0)` | `oklch(1 0 0 / 10%)` | All borders |
| `--primary` | `oklch(0.205 0 0)` | `oklch(0.922 0 0)` | Primary actions, active nav (near-black) |
| `--primary-foreground` | `oklch(0.985 0 0)` | `oklch(0.205 0 0)` | Text on primary |
| `--secondary` | `oklch(0.97 0 0)` | `oklch(0.269 0 0)` | Secondary/ghost surface |
| `--accent` | `oklch(0.97 0 0)` | `oklch(0.269 0 0)` | Neutral hover surface (selected row, chip bg) |
| `--brand` | `oklch(0.55 0.18 255)` | `oklch(0.62 0.17 255)` | **Dynamic** emphasis accent (links, charts, focus) |
| `--destructive` | `oklch(0.577 0.245 27.325)` | `oklch(0.704 0.191 22.216)` | Danger actions, error states |
| `--success` | `oklch(0.62 0.17 149)` | `oklch(0.7 0.16 150)` | Healthy / enabled state |
| `--warning` | `oklch(0.75 0.15 75)` | `oklch(0.8 0.15 80)` | Degraded / at-risk state |
| `--ring` | `oklch(0.708 0 0)` | `oklch(0.556 0 0)` | Focus rings |
| `--sidebar` | `oklch(0.985 0 0)` | `oklch(0.205 0 0)` | Sidebar background |

Use the matching Tailwind utilities: `bg-brand`, `text-brand`, `bg-success`,
`text-warning`, etc. (mapped via `@theme inline`). Elevation utilities `shadow-xs`,
`shadow-sm`, `shadow-md`, `shadow-lg` are driven by `--shadow-*` tokens.

### Dynamic accent + colour mode

`@nerv-iip/ui` exposes the runtime mechanism (composables only — switcher UI is FE-2/FE-3):

- `useColorMode()` → `{ mode, isDark, toggle, setMode }`, toggles `.dark` on `<html>`, persisted.
- `useThemeAccent()` → `{ accent, setAccent, reset, presets }`, rewrites `--brand` at runtime, persisted.
- `initTheme()` → call once in `main.ts` before mount to apply the persisted choice before first paint.

### Status semantic (Badge variants, NOT raw Tailwind palette classes)

| Intent | Badge variant | Do NOT use |
|---|---|---|
| Active / Enabled | `success` | `border-emerald-200 bg-emerald-50 …` |
| Inactive / Disabled | `secondary` | manual gray classes |
| Error / Danger | `destructive` | `text-red-*` |
| Warning | `warning` | `text-amber-*`, `text-yellow-*` |
| Info / System | `default` (primary) | `text-blue-*` |

### Do's and Don'ts

- **DO** use `bg-primary`, `text-foreground`, `border-border` etc. (Tailwind v4 token utilities).
- **DO NOT** use raw palette names: `bg-blue-600`, `text-gray-500`, `border-zinc-200`.
- **DO NOT** use raw hex values anywhere in `.vue` files.
- **DO NOT** use `--legacy-color-*` tokens in any new component. They exist only for the two old console pages (`InstanceTable`, `InstanceDetailPanel`) that are pending migration.
- **DO NOT** use `emerald-*` or `amber-*` directly — use `Badge` with `success`/`warning` variant.

---

## Typography

Mixed Latin + CJK stack, both **self-hosted** (bundled by Vite — no `fonts.googleapis.com`
/ CDN request at runtime). Imported once in `packages/ui/src/styles/theme.css`.

- **Latin / digits → Inter Variable** (`@fontsource-variable/inter`). Crisp digits keep
  dense data tables readable.
- **Chinese → MiSans** (`misans` npm package, Xiaomi, Apache-2.0, free commercial use).
  Generated `styles/misans.css` remaps MiSans' optical weights to standard CSS weights
  (400/500/600/700) so `font-normal/medium/semibold/bold` map correctly, and the woff2 is
  `unicode-range` subsetted (≈100 chunks/weight) so a page only fetches the glyphs it shows.

Full stack (`--font-sans`):
`'Inter Variable', 'MiSans', ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif`

> Inter leads so Latin/digits render in Inter; Chinese falls through to MiSans.
> Regenerate `styles/misans.css` after bumping `misans` — see `DESIGN/governance.md` › Fonts.

| Scale | Tailwind class | Usage |
|---|---|---|
| Page title | `text-2xl font-semibold tracking-tight` | `<h1>` level, page header |
| Section title | `text-lg font-semibold` | Card titles, dialog titles |
| Body | `text-sm` | Default table/form content |
| Caption / muted | `text-sm text-muted-foreground` | IDs, timestamps, hints |
| Mono | `font-mono text-xs text-muted-foreground` | UUIDs, permission codes, technical values |
| Label | `text-sm font-medium` | Form labels, column headers |

- **DO NOT** write custom `font-size` in scoped CSS. Use the scale above.
- **DO NOT** use `text-base` (16px is too large for dense data tables).

---

## Spacing & Layout

- Base unit: `4px` (Tailwind `1` = `4px`).
- Page gutter: `p-6` (`24px`) for page content areas.
- Card padding: `p-6` header + content, or use `CardHeader`/`CardContent` primitives.
- Stack gap inside forms: `gap-4` between fields.
- Stack gap inside toolbar: `gap-3`.
- Table density: default (do not add extra `py-*` to `TableCell`).

Grid layout is `flex`-based; use CSS Grid only for multi-column form layouts (`grid grid-cols-2 gap-4`).

---

## Motion

- Duration: `150ms` for micro-interactions (hover, focus), `200ms` for overlays.
- Easing: `ease-out` for enter, `ease-in` for exit.
- Respect `prefers-reduced-motion`: shadcn-vue components already do this via `tw-animate-css`.
- **DO NOT** add custom `transition-*` classes; shadcn components own their transitions.

---

## Iconography

Library: **lucide-vue-next** (already a peer dep).

| Context | Size | Tailwind class |
|---|---|---|
| Inline with text | 16px | `size-4` |
| Button icon | 16px | `size-4` |
| Empty state illustration | 48px | `size-12` |
| Navigation item | 20px | `size-5` |

- Import only the icons you use per-file: `import { SearchIcon } from 'lucide-vue-next'`.
- Always add `aria-hidden="true"` to decorative icons.
- Always add `aria-label` to icon-only buttons.

---

## Border Radius

| Token | Value | Usage |
|---|---|---|
| `--radius-sm` | `0.25rem` | Inputs, badges |
| `--radius-md` | `0.375rem` | Buttons |
| `--radius-lg` / `--radius` | `0.5rem` | Cards, dialogs, tables, panels |
| `--radius-xl` | `0.625rem` | Large modal overlays |

Use `rounded-lg` for cards/panels, `rounded-md` for buttons, `rounded-4xl` for badges (handled by the component itself).
