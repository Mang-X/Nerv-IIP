# Block Library v2 (FE-2)

Copy-rebuilt, reusable block components in `@nerv-iip/ui`
(`packages/ui/src/components/blocks/`). They compose the unchanged原版 shadcn-vue
primitives + FE-1 tokens. **They never edit a primitive.** Import everything from the
stable boundary `@nerv-iip/ui`; pages must use these instead of inlining layout.

Naming is deliberately distinct from原版 (`DataTable` ≠原版 `Table`,
`StatusBadge` ≠原版 `Badge`). All blocks support light/dark + the dynamic `--brand`
accent automatically (they only use semantic token classes).

Live gallery: `apps/business-console/src/pages/design-system/blocks.vue`
(`/design-system/blocks`) — renders all blocks together for visual review.

## AppShellInset

dashboard-01 `variant="inset"` floating-panel shell skeleton (nav is FE-3).

| Slot | Purpose |
|---|---|
| `sidebar-header` | Brand / logo row in the sidebar |
| `sidebar` | Sidebar nav content (`SidebarGroup` / `SidebarMenu` …) |
| `sidebar-footer` | User / footer area |
| `header` | Top bar content (next to the built-in `SidebarTrigger`) |
| _default_ | Main page content (padded, `gap-4 md:gap-6`) |

Prop: `collapsible` (`'offcanvas' | 'icon' | 'none'`, default `'icon'`).

## PageHeader

Compact breadcrumb-style header (breadcrumb-as-title + count + inline actions) —
replaces the old big-title + description block.

| Prop | Type | Notes |
|---|---|---|
| `title` | `string` | Rendered as the current (last) breadcrumb |
| `breadcrumbs` | `{ label, href? }[]` | Ancestors; use `#breadcrumbs` slot for SPA links |
| `count` | `number \| string` | Muted count next to the title |

Slots: `actions` (right-aligned), `breadcrumbs` (override the trail).

## SectionCard / SectionCards

Gradient KPI card (`bg-gradient-to-t from-primary/5 to-card`): description → big
`tabular-nums` value → trend Badge → footnote. `SectionCards` is the responsive grid.

`SectionCard` props: `description`, `value`, `trend?: { value, direction?: 'up'|'down'|'flat' }`,
`footnote?`, `hint?`. Trend colour: up→success, down→destructive, flat→muted.
`SectionCards` prop: `columns?` (2–4, default 4).

## Toolbar

Single-row filter/action bar. Built-in search (`v-model:search`) + `#filters` + `#actions`
slots. Props: `showSearch`, `searchPlaceholder`, `searchLabel`. Search grows; actions push right.

## DataTable + DataTablePagination

Column-config + cell-slot table (the standard for all stage-B list pages). Independent
pagination.

`DataTable` props: `columns: DataTableColumn[]`, `rows`, `rowKey` (field or fn),
`loading?`, `emptyMessage?`, `skeletonRows?`, `sort?` (`v-model:sort`), `clientSort?` (default true).

`DataTableColumn`: `{ key, header, align?, sortable?, width?, headerClass?, cellClass?, accessor? }`.

- Default cell renders `accessor?.(row) ?? row[key]`; override per column with the
  `#cell-<key>` slot (slot props: `{ row, value, column }`).
- Built-in loading skeleton, empty state (`#empty` slot), click-to-sort (asc → desc → off),
  hover, right-aligned numeric columns.
- A trailing actions column = a `{ key: 'actions', align: 'end' }` column + `#cell-actions` slot.

`DataTablePagination` props: `page` / `pageSize` (string) / `totalItems` / `pageSizeOptions?`
with `update:page` / `update:pageSize`.

## StatusBadge

Status-semantic badge: maps a raw status (`running`, `ready`, `blocked`, …) to a localized
label + tone via `resolveStatus()`. Tones render on the unmodified原版 Badge through token
classes (`success` / `warning` / `danger` = destructive / `info` = brand / `neutral`).
Props: `value`, `label?` (override), `tone?` (override).

## RowActions

Row-action `MoreHorizontal` ghost trigger + `DropdownMenu`; default slot holds the
`DropdownMenuItem`s. Props: `disabled?`, `label?`, `align?`, `contentClass?`.

## ThemeToggle / ThemePicker

`ThemeToggle` — light/dark toggle (uses `useColorMode`). `ThemePicker` — runtime accent
picker over `ACCENT_PRESETS` (uses `useThemeAccent`, writes `--brand`). Both are icon buttons
for the app header.

## Do NOT

- Do not inline these patterns into pages — import the block.
- Do not edit a原版 primitive to change a block's look; adjust the block or tokens.
- Do not give `DataTable` business/fetch logic — it renders rows + emits sort; the page owns data.
