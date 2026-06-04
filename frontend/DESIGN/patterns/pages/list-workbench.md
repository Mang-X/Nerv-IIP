# Page Prototype: List Workbench (列表工作台页)

The canonical Business Console list page — the copy-from baseline for **all** stage-B
list/工作台 pages. Gold standard: `apps/business-console/src/pages/mes/operation-tasks.vue`
(工序执行). Built entirely from FE-2 blocks; never hand-assemble these regions.

## Structure (top → bottom)

| Region | Block | Rules |
|---|---|---|
| Layout | `BusinessLayout` (T-shaped `AppShellT`) | The page only fills the content slot — no shell chrome inline. |
| Page header | `PageHeader` | Breadcrumb-as-title + result `count` + `#actions` (e.g. 刷新). No big-title/description block. |
| KPIs | `SectionCards` + `SectionCard` | 2–4 metrics; `tabular-nums` values + short hint. Never raw metric `<div>`s. |
| Toolbar | `Toolbar` | `v-model:search` (live) + `#filters` (status/scope `Select`s) + `#actions` (重置 / 次操作). One row. |
| Error | inline `text-destructive` `role="alert"` | Only when the facade errored; otherwise the empty state speaks. |
| Table | `DataTable` | Column config + `#cell-<key>` slots. `:loading` skeleton, empty message, click-to-sort. Status → `StatusBadge`; row menu → `RowActions`. Never raw `<Table>`. |
| Pagination | `DataTablePagination` | Independent, below the table. Total = the **sorted/filtered** length (before paging). |

## Data / sorting / paging contract

- Keep the page's data composable (e.g. `useMesOperationTasks`) untouched.
- Filtering + sorting + pagination are **page-owned**, in that order: `visible → sorted → paged`.
  Pass `:rows="paged"` and `:client-sort="false"` to `DataTable` (it renders + emits
  `update:sort`; the page re-sorts the full list so paging stays correct).
- Cross-domain navigation uses links / `RowActions` items, not extra menu entries.

## Copy & metadata rules (from frontend-navigation-map.md)

- Visible copy is business Chinese. **No** dev/platform terms: organization/environment/
  context, `operationId`, `sourceSystem`, demo/seed/mock/样例.
- Object detail, action forms and in-page tabs are never promoted to nav.

## Acceptance checklist (per migrated list page)

- [ ] Uses `BusinessLayout` + `PageHeader` + `SectionCards` + `Toolbar` + `DataTable` + `DataTablePagination`.
- [ ] No legacy per-app blocks (`BusinessPageHeader`/`BusinessContextBar`/`BusinessMetricCell`/`BusinessTablePagination`/`BusinessRowActions`/`BusinessStatusBadge`/`BusinessEmptyState`/`BusinessFormStatus`).
- [ ] No raw `<Table>`/`<TableHeader>` assembly in the page — table via `DataTable`.
- [ ] No raw metric `<div>`s — KPIs via `SectionCard`.
- [ ] Status via `StatusBadge`; row actions via `RowActions`.
- [ ] Only `@nerv-iip/ui` / `@nerv-iip/app-shell` imports — no deep imports (`@nerv-iip/ui/...`, `reka-ui`, `shadcn-vue`).
- [ ] No org/environment/debug/source metadata or dev-language copy.
- [ ] `pnpm -C frontend --filter @nerv-iip/business-console typecheck && test && build`.

## Enforcement

`apps/business-console/src/pages/goldStandardPages.contract.test.ts` runs the checklist
against a curated allowlist of migrated pages (starts with `mes/operation-tasks.vue`). Add
each page to the allowlist as it is migrated in stage B — the test then prevents drift
(including by codex). Un-migrated pages are not yet enforced.
