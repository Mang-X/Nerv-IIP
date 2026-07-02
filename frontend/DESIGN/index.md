---
# Nerv-IIP Console — Design System
# AI coding agents: read this file first on any UI task.
---

## System Summary

A calm, professional enterprise control plane built on **Vue 3 + Tailwind CSS v4 + shadcn-vue (reka-nova style)**. All UI primitives live in `packages/ui` and are consumed via the `@nerv-iip/ui` barrel export. Application code never imports from deep paths like `packages/ui/src/components/ui/*`. The primary color is a cool blue (`oklch(0.49 0.17 255)`), there is no decoration, and the UI is always information-dense.

---

## User-Facing Copy Rule

Business pages are product surfaces for planners, operators, inspectors, warehouse users, buyers, accountants and managers. They are not implementation notes, test panels, seed-data viewers or PR acceptance evidence.

Visible page copy must help the user decide, act, or understand a business state. Do not put development, validation or scaffolding language in headings, descriptions, empty states, captions, badges, table summaries, form help, toasts or menus.

Forbidden examples in product UI:

1. `样例数据`, `内置样例`, `用于验证`, `便于联动测试`, `当前页面`, `demo`, `mock`, `fallback`, `seed`.
2. Technical ownership or gateway language such as `业务网关契约`, `接口`, `API`, `operationId`, `source service`, `organization`, `environment`, `context`.
3. Scenario disclaimers such as `汽车减振器制造场景下...用于验证...`. Industry context can shape the data and labels, but the page should speak as the user's live business system.

Allowed alternatives:

1. Use concise business nouns: `销售订单`, `采购订单`, `生产计划`, `工单`, `物料`, `工艺路线`, `应收`, `应付`, `成本归集`.
2. Use operational summaries: `今日待排产订单`, `待齐套工单`, `待检来料`, `本班待报工任务`.
3. Use empty-state guidance tied to the next action: `暂无待派工工单，请先确认生产计划并下达到车间。`

If data is demo-only or incomplete, keep that fact in developer docs, PR notes or test fixtures. Do not surface it in the application UI.

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
| `Tabs` + parts | `@nerv-iip/ui` | Peer sections inside a detail object | Primary app navigation |
| `Sheet` + parts | `@nerv-iip/ui` | Slide-in detail/edit panels that preserve list context | Full-page workflows |
| `DatePicker` / `DateRangePicker` | `@nerv-iip/ui` | DateOnly form fields and business date range filters | Date-time selection or timezone-specific timestamps |
| `Chart` parts | `@nerv-iip/ui` | Business dashboards with semantic chart tokens | Decorative one-off visualizations |
| `FileUpload` | `@nerv-iip/ui` | FileStorage-backed attachments and evidence uploads | Direct object-storage uploads |
| `Avatar` + parts | `@nerv-iip/ui` | User identity display | Generic icons |
| `DropdownMenu` + parts | `@nerv-iip/ui` | Contextual row actions, topbar user menu | Primary navigation |
| `Pagination` + parts | `@nerv-iip/ui` | Server-side paginated lists (via IamPagination wrapper) | Client-side filtered lists |
| `Skeleton` | `@nerv-iip/ui` | Initial data load placeholder | Refresh over existing data (use Spinner) |
| `Spinner` | `@nerv-iip/ui` | Button/inline loading indicator | Full-section initial load (use Skeleton) |
| `Progress` | `@nerv-iip/ui` | Upload, batch, or operation progress | Binary status labels |
| `ScrollArea` + `ScrollBar` | `@nerv-iip/ui` | Constrained task/detail lists | Whole-page scrolling |
| `Separator` | `@nerv-iip/ui` | Visual section dividers | Layout spacing (use `gap-*`) |
| `Toaster` / `toast` | `@nerv-iip/ui` | Transient success/error feedback | Persistent errors (use Alert) |
| `Breadcrumb` + parts | `@nerv-iip/ui` | Deep hierarchy navigation (plant → line → device) | Flat single-level pages |
| `Tooltip` + parts | `@nerv-iip/ui` | Icon-only button labels, status descriptions | Long-form help text (use Popover) |
| `Popover` + parts | `@nerv-iip/ui` | Date pickers and compact advanced filter panels | Modal workflows (use Dialog/Sheet) |
| `Sidebar` + parts | `@nerv-iip/ui` | App shell collapsible sidebar layout | — (only used by `AppShell`) |

### Not yet installed — install as needed

See `components/install-backlog.md` for full list and install commands.

| Component | When you need it |
|---|---|
| `Collapsible` | Timeline entries, config section groups (available via `reka-ui` directly) |
| `Command` | Combobox / searchable Select for large datasets |
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
| 甘特 / 资源排产可视化 | Scheduling Workbench | `patterns/blocks/scheduling-workbench.md` |

> **排程可视化组件**（`GanttChart` / `ResourceSchedulerBoard` / `SchedulingWorkbench`）来自独立包 **`@nerv-iip/scheduling`**（非 `@nerv-iip/ui`）：引擎无关契约 + DHTMLX 适配器(试用开发 / 正式手动分发),无本地引擎时优雅占位。组件契约见 `components/gantt-chart.md`、`components/resource-scheduler-board.md`；引擎接缝见包 `README.md`。

## Roadmaps

| Scenario | File |
|---|---|
| Business console component readiness (#143) | `roadmaps/business-console-readiness.md` |
| Business Console MES PC workbench | `roadmaps/business-console-mes-pc-workbench.md` |

---

## Rules All AI Agents Must Follow

1. **User-facing copy first**: pages are for business users, not developers. Never expose demo/test/scaffolding/gateway/context wording in UI copy.
2. **Import boundary**: always import from `@nerv-iip/ui`, never from deep paths.
3. **No raw palette classes**: `bg-blue-600`, `text-gray-500`, `border-zinc-*` are forbidden. Use semantic utilities (`bg-primary`, `text-muted-foreground`, `border-border`).
4. **No raw hex in templates**: use token utilities.
5. **No `--legacy-color-*` in new components**.
6. **Badge variants for status**: use `success`/`warning`/`destructive`/`secondary` — never handcraft colors.
7. **AlertDialog for destructive confirms**: never use `window.confirm` or a plain `Dialog`.
8. **`<script setup lang="ts">`** with Composition API — Options API is not used.
9. **Icon rules**: `size-4` default, `aria-hidden="true"` on decorative, `aria-label` on icon-only buttons.
10. **New shadcn components**: install via CLI → export from `index.ts` → write spec in `DESIGN/components/`.
11. **Scoped CSS exception**: only the login page (`login.vue`) uses `<style scoped>` for the fluid `clamp()` heading. All other new components use Tailwind utilities only.
