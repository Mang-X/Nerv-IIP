---
# Foundation — Nerv-IIP Design System v2
# Black-primary, dashboard-01 baseline: professional, information-dense, light + dark, dynamic accent.
---

## Style Intent

An information-dense enterprise control plane aligned to shadcn **dashboard-01**:
near-black primary, neutral chrome, **light + dark first-class**, and a **runtime
dynamic accent** (brand blue for emphasis + the page-level primary CTA). Page
canvas sits a notch below card surfaces (inset floating panels) with a
`--shadow-*` elevation scale. 气质与设计原则的权威定位：
`packages/ui/src/components/pc/product.md`（骨架=成熟 B端范式 / 基调=冷静工业
高密度 / 触感=产品级精致）。

Tokens are a single source of truth in `@nerv-iip/ui` — `packages/ui/src/styles/theme.css`
— imported by both `apps/console` and `apps/business-console`. Never duplicate token
values per app.

### Hard rules (epic #275 / FE-1 #276)

1. `--primary` is **near-black** (`oklch(0.205 0 0)`), not the retired blue.
2. Brand blue lives in `--nv-brand` (legacy alias `--brand`), is
   **runtime-overridable**, and is used for emphasis (links, focus, charts,
   selected states) **plus the page-level primary CTA**
   (`NvButton variant="brand"`, one per page/toolbar — owner 裁决 2026-07-16).
3. Light and dark are both shipped (`.dark` override in `theme.css`).
4. **Never edit原版 shadcn-vue components.** They are re-pulled verbatim from the
   official `reka-nova` registry and may be overwritten on re-pull. Any customization
   is a copy-rebuilt component (FE-2), never an edit to a primitive.

---

## Colors

### Semantic tokens (all code must use these — never raw hex or raw Tailwind palette names)

| Token                  | Light value                 | Dark value                  | Purpose                                            |
| ---------------------- | --------------------------- | --------------------------- | -------------------------------------------------- |
| `--background`         | `oklch(0.985 0 0)`          | `oklch(0.145 0 0)`          | Page canvas (below cards)                          |
| `--foreground`         | `oklch(0.145 0 0)`          | `oklch(0.985 0 0)`          | Body text                                          |
| `--card`               | `oklch(1 0 0)`              | `oklch(0.205 0 0)`          | Card / inset panel surface                         |
| `--muted`              | `oklch(0.97 0 0)`           | `oklch(0.269 0 0)`          | Subdued surface (table stripe, hover)              |
| `--muted-foreground`   | `oklch(0.556 0 0)`          | `oklch(0.708 0 0)`          | Secondary text, placeholders                       |
| `--border`             | `oklch(0.922 0 0)`          | `oklch(1 0 0 / 10%)`        | All borders                                        |
| `--primary`            | `oklch(0.205 0 0)`          | `oklch(0.922 0 0)`          | Primary actions, active nav (near-black)           |
| `--primary-foreground` | `oklch(0.985 0 0)`          | `oklch(0.205 0 0)`          | Text on primary                                    |
| `--secondary`          | `oklch(0.97 0 0)`           | `oklch(0.269 0 0)`          | Secondary/ghost surface                            |
| `--accent`             | `oklch(0.97 0 0)`           | `oklch(0.269 0 0)`          | Neutral hover surface (selected row, chip bg)      |
| `--nv-brand`           | `oklch(0.55 0.18 255)`      | `oklch(0.62 0.17 255)`      | **Dynamic** emphasis accent (links, charts, focus) |
| `--destructive`        | `oklch(0.577 0.245 27.325)` | `oklch(0.704 0.191 22.216)` | Danger actions, error states                       |
| `--nv-success`         | `oklch(0.62 0.17 149)`      | `oklch(0.7 0.16 150)`       | Healthy / enabled state                            |
| `--nv-warning`         | `oklch(0.75 0.15 75)`       | `oklch(0.8 0.15 80)`        | Degraded / at-risk state                           |
| `--ring`               | `oklch(0.708 0 0)`          | `oklch(0.556 0 0)`          | Focus rings                                        |
| `--sidebar`            | `oklch(0.985 0 0)`          | `oklch(0.205 0 0)`          | Sidebar background                                 |

Use the matching Tailwind utilities: `bg-brand`, `text-brand`, `bg-success`,
`text-warning`, etc. (mapped via `@theme inline`). Elevation utilities `shadow-xs`,
`shadow-sm`, `shadow-md`, `shadow-lg` are driven by `--shadow-*` tokens.

### Dynamic accent + colour mode

`@nerv-iip/ui` exposes the runtime mechanism (composables only — switcher UI is FE-2/FE-3):

- `useColorMode()` → `{ mode, isDark, toggle, setMode }`, toggles `.dark` on `<html>`, persisted.
- `useThemeAccent()` → `{ accent, setAccent, reset, presets }`, rewrites `--brand` at runtime, persisted.
- `initTheme()` → call once in `main.ts` before mount to apply the persisted choice before first paint.

### Status semantic (`NvStatusBadge` / `NvStatusDot` `tone`, NOT raw Tailwind palette classes)

状态呈现统一走 `NvStatusBadge`（胶囊，自带 `NvStatusDot`）或 `NvStatusDot`（行内点），
`tone` 五档；传后端状态字符串给 `value` 可自动解析 tone + 中文标签。

| Intent                        | `tone`    | Do NOT use                                          |
| ----------------------------- | --------- | --------------------------------------------------- |
| Active / Healthy / Enabled    | `success` | `border-emerald-200 bg-emerald-50 …`                |
| Warning / Degraded            | `warning` | `text-amber-*`, `text-yellow-*`                     |
| Error / Danger / Alarm        | `danger`  | `text-red-*`, `destructive`（不是本组件的 tone 名） |
| Info / In-progress            | `info`    | `text-blue-*`                                       |
| Inactive / Disabled / Unknown | `neutral` | manual gray classes                                 |

### Do's and Don'ts

- **DO** use `bg-primary`, `text-foreground`, `border-border` etc. (Tailwind v4 token utilities).
- **DO NOT** use raw palette names: `bg-blue-600`, `text-gray-500`, `border-zinc-200`.
- **DO NOT** use raw hex values anywhere in `.vue` files.
- **DO NOT** use `--legacy-color-*` tokens in any new component. They exist only for the two old console pages (`InstanceTable`, `InstanceDetailPanel`) that are pending migration.
- **DO NOT** use `emerald-*` or `amber-*` directly — use `NvStatusBadge` with the
  matching `tone`.

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

| Scale           | Tailwind class                            | Usage                                     |
| --------------- | ----------------------------------------- | ----------------------------------------- |
| Page title      | `text-2xl font-semibold tracking-tight`   | `<h1>` level, page header                 |
| Section title   | `text-lg font-semibold`                   | Card titles, dialog titles                |
| Body            | `text-sm`                                 | Default table/form content                |
| Caption / muted | `text-sm text-muted-foreground`           | IDs, timestamps, hints                    |
| Mono            | `font-mono text-xs text-muted-foreground` | UUIDs, permission codes, technical values |
| Label           | `text-sm font-medium`                     | Form labels, column headers               |

- **DO NOT** write custom `font-size` in scoped CSS. Use the scale above.
- **DO NOT** use `text-base` (16px is too large for dense data tables).

---

## Spacing & Layout

- Base unit: `4px` (Tailwind `1` = `4px`).
- Page gutter: `p-6` (`24px`) for page content areas.
- Card padding: `p-6` header + content, or use `NvCardHeader`/`NvCardContent`.
- Stack gap inside forms: `gap-4` between fields.
- Stack gap inside toolbar: `gap-3`.
- Table density: default (do not add extra `py-*` to `TableCell`).

Grid layout is `flex`-based; use CSS Grid only for multi-column form layouts (`grid grid-cols-2 gap-4`).

---

## Motion

动效规范的唯一来源是 `DESIGN/motion-interaction.md`（缓动/时长令牌 `--nv-ease-*` /
`--nv-duration-*`、必备交互状态、reduced-motion 降级、提交前自检清单）。本文件不再
重复参数，避免两处漂移。速记：动效传达状态而非装饰；原版 shadcn 组件自带过渡，
不要给它们叠加自定义 `transition-*`。

---

## Data Typography 数据排版（B端数据页的隐形一致性）

制造业控制台里数字是主角，以下口径全端统一（现网既有约定的成文化）：

| 规则        | 写法                                                                                                      |
| ----------- | --------------------------------------------------------------------------------------------------------- |
| 数值列      | 表格内**右对齐** + `tabular-nums`（`NvDataTable` 列用 `align: 'right'`）                                  |
| 数量精度    | 最多 3 位小数：`value.toLocaleString(undefined, { maximumFractionDigits: 3 })`                            |
| 数量 + 单位 | 数值后接**空格 + UOM 码**：`128.5 kg`；单位不并入数字色/粗细                                              |
| KPI 大数    | `tabular-nums tracking-tight`（`NvMetricCard`/`NvStatTile` 已内置）                                       |
| 空值        | 统一 `—`（em dash）；不用 `0`、空串、`N/A` 混用——`0` 是数据，`—` 是没有数据                               |
| 编号/技术值 | `font-mono text-xs text-muted-foreground`（单号、批次、UUID、权限码）                                     |
| 时间        | 表格/详情用**绝对时间**（本地时区、到分钟）；相对时间（"3 分钟前"）只用于事件/通知流，且 tooltip 给绝对值 |
| 超长文本    | `truncate` + `NvTooltip` 全文；不许撑破列宽                                                               |

---

## Iconography

Library: **@lucide/vue** (already a peer dep).

| Context                  | Size | Tailwind class |
| ------------------------ | ---- | -------------- |
| Inline with text         | 16px | `size-4`       |
| Button icon              | 16px | `size-4`       |
| Empty state illustration | 48px | `size-12`      |
| Navigation item          | 20px | `size-5`       |

- Import only the icons you use per-file: `import { SearchIcon } from '@lucide/vue'`.
- Always add `aria-hidden="true"` to decorative icons.
- Always add `aria-label` to icon-only buttons.

---

## Border Radius

| Token                      | Value      | Usage                          |
| -------------------------- | ---------- | ------------------------------ |
| `--radius-sm`              | `0.25rem`  | Inputs, badges                 |
| `--radius-md`              | `0.375rem` | Buttons                        |
| `--radius-lg` / `--radius` | `0.5rem`   | Cards, dialogs, tables, panels |
| `--radius-xl`              | `0.625rem` | Large modal overlays           |

Use `rounded-lg` for cards/panels, `rounded-md` for buttons, `rounded-4xl` for badges (handled by the component itself).
