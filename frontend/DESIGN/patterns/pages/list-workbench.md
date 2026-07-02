# Page Prototype: List Workbench (列表工作台页)

The canonical Business Console list page — the copy-from baseline for **all** stage-B
list/工作台 pages. Gold standard: `apps/business-console/src/pages/mes/operation-tasks.vue`
(工序执行). Built entirely from FE-2 blocks; never hand-assemble these regions.

## Structure (top → bottom)

| Region | Block | Rules |
|---|---|---|
| Layout | `BusinessLayout` (T-shaped `AppShellT`) | The page only fills the content slot — no shell chrome inline. |
| Page header | `PageHeader` | Breadcrumb-as-title + result `count` + `#actions` (e.g. 刷新). No big-title/description block. |
| KPIs (optional) | `SectionCards` + `SectionCard` | Only when a genuine **semantic** metric helps the operator act (e.g. 待收料 3 单). 1–4 cards, `tabular-nums` + short hint. Never **mechanical** page counts (本页 X 行 / 后端分页总数) — they mislead. Never raw metric `<div>`s. Drop the region entirely if there's no real metric. |
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
- **Guide with UI, not prose — the page is not a manual.** No floating "用途说明" paragraph
  above the toolbar (`这里是… 先… 再…`). The PageHeader (title + count), column headers,
  `StatusBadge`, the primary header/row buttons and the empty-state already say "what is this /
  what do I do". A subtitle, if truly needed, is **one short clause** — never multi-sentence
  instructions. No redundant "本页 N 行 / N 单" counter line (the count is already in the
  PageHeader and the pagination footer).
- **Show the real identifier; never hide it behind a placeholder.** Render the human-readable
  codes the facade actually returns — `workOrderId=WO-20260608-000015`, `workCenterId=WC-ASSY`,
  `skuId=SKU-…`, `operationTaskId=WO-…-OP-10` — directly. Make the **ID itself** the clickable
  link that opens detail / quick-view; do **not** replace it with a generic 「查看X」 button, and
  do **not** show 「待接入 / 名称待接入 / 物料名称待接入」 in place of a value that exists. These
  business codes are operator vocabulary, **not** dev-language — only true technical internals
  (raw GUID, `resourceType`, org/env, `#hash`) are banned. Use `—` only when the value is genuinely
  null. (MES facades return codes, not GUIDs — verify with a probe before assuming a field is unusable.)

## Acceptance checklist (per migrated list page)

- [ ] Uses `BusinessLayout` + `PageHeader` + `Toolbar` + `DataTable` + `DataTablePagination` (`SectionCards` only when there's a real semantic KPI).
- [ ] No legacy per-app blocks (`BusinessPageHeader`/`BusinessContextBar`/`BusinessMetricCell`/`BusinessTablePagination`/`BusinessRowActions`/`BusinessStatusBadge`/`BusinessEmptyState`/`BusinessFormStatus`).
- [ ] No raw `<Table>`/`<TableHeader>` assembly in the page — table via `DataTable`.
- [ ] No raw metric `<div>`s — KPIs via `SectionCard`.
- [ ] Status via `StatusBadge`; row actions via `RowActions`.
- [ ] Only `@nerv-iip/ui` / `@nerv-iip/app-shell` imports — no deep imports (`@nerv-iip/ui/...`, `reka-ui`, `shadcn-vue`).
- [ ] No org/environment/debug/source metadata or dev-language copy.
- [ ] **Every side-nav entry for this page's domain has an `icon`** — no first-character fallback in the rail (see `blocks/app-shell.md`).
- [ ] **No "用途说明" paragraph and no redundant "本页 N" counter** — guidance comes from header, columns, badges, buttons, empty-state.
- [ ] **Identifiers show the real code** (WO-…/WC-…/SKU-…), with the ID itself as the click target for detail/quick-view — not a 「查看X」 button or 「待接入」 placeholder.
- [ ] `pnpm -C frontend --filter @nerv-iip/business-console typecheck && test && build`.

## Enforcement

`apps/business-console/src/pages/goldStandardPages.contract.test.ts` runs the checklist
against a curated allowlist of migrated pages (starts with `mes/operation-tasks.vue`). Add
each page to the allowlist as it is migrated in stage B — the test then prevents drift
(including by codex). Un-migrated pages are not yet enforced.
