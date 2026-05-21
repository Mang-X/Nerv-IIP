---
# Foundation — Nerv-IIP Console Design System
# "Calm Control Plane": professional, information-dense, blue-primary, zero decoration.
---

## Style Intent

A calm, information-dense enterprise control plane: cool-blue primary, neutral chrome, readable at a glance, never decorative.

---

## Colors

### Semantic tokens (all code must use these — never raw hex or raw Tailwind palette names)

| Token | Light value | Purpose |
|---|---|---|
| `--background` | `oklch(1 0 0)` | Page canvas |
| `--foreground` | `oklch(0.145 0 0)` | Body text |
| `--card` | `oklch(1 0 0)` | Card surfaces |
| `--muted` | `oklch(0.97 0 0)` | Subdued surface (table stripe, hover) |
| `--muted-foreground` | `oklch(0.556 0 0)` | Secondary text, placeholders |
| `--border` | `oklch(0.922 0 0)` | All borders |
| `--input` | `oklch(0.922 0 0)` | Input border |
| `--primary` | `oklch(0.49 0.17 255)` | Primary actions, active nav |
| `--primary-foreground` | `oklch(0.985 0 0)` | Text on primary |
| `--secondary` | `oklch(0.97 0 0)` | Secondary/ghost surface |
| `--secondary-foreground` | `oklch(0.205 0 0)` | Text on secondary |
| `--accent` | `oklch(0.96 0.03 255)` | Subtle blue surface (selected row, chip bg) |
| `--accent-foreground` | `oklch(0.28 0.11 255)` | Text on accent |
| `--destructive` | `oklch(0.577 0.245 27.325)` | Danger actions, error states |
| `--ring` | `oklch(0.62 0.15 255)` | Focus rings |
| `--sidebar` | `oklch(0.985 0 0)` | Sidebar background |

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

Single font stack: `ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif`

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
