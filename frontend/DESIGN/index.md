---
# Nerv-IIP Console — Design System
# AI coding agents: read this file first on any UI task.
---

## System Summary

A calm, professional enterprise control plane built on **Vue 3 + Tailwind CSS v4 + shadcn-vue (reka-nova style)**. All UI primitives live in `packages/ui` and are consumed via the `@nerv-iip/ui` barrel export. Application code never imports from deep paths like `packages/ui/src/components/ui/*`. The primary color is a cool blue (`oklch(0.49 0.17 255)`), there is no decoration, and the UI is always information-dense.

---

## Component Quick Reference

| Component | Export | Use when | Do NOT use when |
|---|---|---|---|
| `Button` | `@nerv-iip/ui` | Any clickable action | Navigating to another route (use `RouterLink`) |
| `Badge` | `@nerv-iip/ui` | Status labels, category chips | Long text > 3 words |
| `Card` + parts | `@nerv-iip/ui` | Grouped content sections, form cards | Wrapping a data table |
| `Table` + parts | `@nerv-iip/ui` | Tabular entity lists | Single-item detail views |
| `TableEmpty` | `@nerv-iip/ui` | Zero-results state inside Table | Standalone empty states |
| `Empty` + parts | `@nerv-iip/ui` | Full-section empty state with illustration | Inside a data table |
| `Alert` + parts | `@nerv-iip/ui` | Inline persistent errors/notices | Transient feedback (use toast) |
| `Dialog` + parts | `@nerv-iip/ui` | Create/Edit entity forms | Destructive confirmations (use AlertDialog) |
| `AlertDialog` + parts | `@nerv-iip/ui` | Confirm irreversible actions (delete, disable) | Informational prompts |
| `Field` + parts | `@nerv-iip/ui` | Form fields with label + validation | Simple inline inputs without labels |
| `Input` | `@nerv-iip/ui` | Text entry | Selecting from a fixed list (use Select) |
| `Select` + parts | `@nerv-iip/ui` | Fixed-option selection | Searching through large option sets (use Combobox) |
| `Checkbox` | `@nerv-iip/ui` | Multi-select, permission toggles | Exclusive single-choice (use Select) |
| `Avatar` + parts | `@nerv-iip/ui` | User identity display | Generic icons |
| `DropdownMenu` + parts | `@nerv-iip/ui` | Contextual row actions, topbar user menu | Primary navigation |
| `Pagination` + parts | `@nerv-iip/ui` | Server-side paginated lists (via IamPagination wrapper) | Client-side filtered lists |
| `Skeleton` | `@nerv-iip/ui` | Initial data load placeholder | Refresh over existing data (use Spinner) |
| `Spinner` | `@nerv-iip/ui` | Button/inline loading indicator | Full-section initial load (use Skeleton) |
| `Separator` | `@nerv-iip/ui` | Visual section dividers | Layout spacing (use `gap-*`) |
| `Toaster` / `toast` | `@nerv-iip/ui` | Transient success/error feedback | Persistent errors (use Alert) |
| `Breadcrumb` + parts | `@nerv-iip/ui` | Deep hierarchy navigation (plant → line → device) | Flat single-level pages |
| `Tooltip` + parts | `@nerv-iip/ui` | Icon-only button labels, status descriptions | Long-form help text (use Popover) |
| `Sidebar` + parts | `@nerv-iip/ui` | App shell collapsible sidebar layout | — (only used by `AppShell`) |

### Not yet installed — install as needed

See `components/install-backlog.md` for full list and install commands.

| Component | When you need it |
|---|---|
| `Tabs` | Detail pages with multiple content sections |
| `Sheet` | Slide-in detail panel (replaces InstanceDetailPanel) |
| `Popover` | Date pickers, advanced filter panels |
| `Progress` | Operation task %, batch progress bars |
| `Collapsible` | Timeline entries, config section groups (available via `reka-ui` directly) |
| `ScrollArea` | Constrained-height scrollable lists |
| `Command` | Combobox / searchable Select for large datasets |
| `Calendar` | Date/range pickers for scheduling and telemetry |
| `Toggle` / `ToggleGroup` | View mode switches, filter pill groups |

---

## Pattern Quick Reference

| Scenario | Pattern | File |
|---|---|---|
| Authentication / sign in | Login Page | `patterns/pages/login-page.md` |
| CRUD list page with search/filter | List Page | `patterns/pages/list-page.md` |
| Inline entity creation | Create Dialog | `patterns/flows/create-dialog.md` |
| Confirm destructive action | Confirm Destroy | `patterns/flows/confirm-destroy.md` |
| App chrome (sidebar + topbar) | App Shell | `patterns/blocks/app-shell.md` |
| Page heading with title + description | Page Header | `patterns/blocks/page-header.md` |
| Search + filter + primary action bar | Toolbar | `patterns/blocks/toolbar.md` |
| Data table with loading/empty states | Data Table | `patterns/blocks/data-table.md` |
| Paginated table footer | Pagination Bar | `patterns/blocks/pagination-bar.md` |

---

## Rules All AI Agents Must Follow

1. **Import boundary**: always import from `@nerv-iip/ui`, never from deep paths.
2. **No raw palette classes**: `bg-blue-600`, `text-gray-500`, `border-zinc-*` are forbidden. Use semantic utilities (`bg-primary`, `text-muted-foreground`, `border-border`).
3. **No raw hex in templates**: use token utilities.
4. **No `--legacy-color-*` in new components**.
5. **Badge variants for status**: use `success`/`warning`/`destructive`/`secondary` — never handcraft colors.
6. **AlertDialog for destructive confirms**: never use `window.confirm` or a plain `Dialog`.
7. **`<script setup lang="ts">`** with Composition API — Options API is not used.
8. **Icon rules**: `size-4` default, `aria-hidden="true"` on decorative, `aria-label` on icon-only buttons.
9. **New shadcn components**: install via CLI → export from `index.ts` → write spec in `DESIGN/components/`.
10. **Scoped CSS exception**: only the login page (`login.vue`) uses `<style scoped>` for the fluid `clamp()` heading. All other new components use Tailwind utilities only.
