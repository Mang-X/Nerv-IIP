# Business Console MES PC Workbench

This roadmap is the design source for the Business Console PC interaction model. It complements `docs/superpowers/plans/2026-05-26-business-console-mes-pc-completion.md` by describing how pages should behave for production planners, shift leaders, inspectors and inventory operators.

## Design Conclusion

The first implementation exposed useful API surfaces, but several pages behaved like interface test panels: filters, metrics, tables and large forms were stacked in one route. The PC workbench must be task-driven instead:

1. Keep list/search/result surfaces on the main page.
2. Move create, post, report, schedule and confirm actions into a sheet, dialog or object detail route.
3. Use the current object context to prefill action forms.
4. Hide or auto-generate technical fields such as idempotency keys and source service codes.
5. Keep setup checks as supporting diagnostics, not as prominent primary operations.

## Reference Signals

Mature MES and frontline systems organize around production execution, worklists and operator guidance rather than raw service methods. SAP Digital Manufacturing describes worklists, live operations, resource orchestration and shop-floor execution as core interaction surfaces. Tulip's Order Execution app centers on selecting a work order, executing operations, following instructions and logging production in one guided flow. These are directionally useful references, but Nerv-IIP should remain a dense industrial management UI, not a marketing dashboard.

References:

1. SAP Digital Manufacturing: https://www.sap.com/products/scm/execution-mes.html
2. Tulip Order Execution: https://support.tulip.co/docs/order-execution

## Navigation Model

Top-level domains remain:

1. `主数据`
2. `库存`
3. `质量`
4. `MES`

MES menu order:

| Menu | Route | Role |
| --- | --- | --- |
| 生产驾驶舱 | `/mes` | Shift leader / dispatcher first screen. |
| 工单与派工 | `/mes/work-orders` | Work order list, rush order entry, dispatch context. |
| 工序执行 | `/mes/operation-tasks` | Operation task worklist and execution entry. |
| 在制跟踪 | `/mes/wip` | WIP status and blockers. |
| 报工记录 | `/mes/production-reports` | Report history; create action comes from work order or operation context. |
| 完工入库 | `/mes/receipts` | Receipt requests; create action comes from completion context. |
| 异常与产能 | `/mes/capacity` | Equipment/capacity impact and exception awareness. |
| 规则排程 | `/mes/schedules` | Rule result and explicit schedule-run action, not a Gantt workspace. |
| 生产准备检查 | `/mes/foundation` | Supporting readiness diagnostics for release/start/dispatch. |

`基础就绪` must not be a primary operator label. Use `生产准备检查` and position it after execution pages.

## Page Patterns

### List Workbench

Use for work orders, operation tasks, WIP, production reports, receipts, NCRs and inventory facts.

Required structure:

1. Page header with one primary action and refresh as secondary action.
2. Business context/filter bar.
3. KPI strip with only action-driving numbers.
4. Main table or queue.
5. Action sheets or row actions declared once at page level.

Do not put multi-section forms below the table.

### Action Sheet

Use for bounded adjacent actions:

1. Rush order creation.
2. Production report posting.
3. Inventory movement posting.
4. Count task creation and count adjustment confirmation.
5. Inspection record creation.
6. Finished goods receipt request creation.
7. Schedule run.

The sheet preserves the list context and makes it clear that the user is performing a side action against a current queue.

### Object Detail

Use when an object has peer sections:

1. Work order detail: overview, operations, materials, quality, reports, receipts, blockers, history.
2. NCR detail: evidence, disposition, closure, related inspection, audit trail.
3. Count task detail: scope, counted quantity, variance, adjustment result.

Tabs belong inside detail pages only. They are not primary navigation.

## Field Rules

1. `organizationId` and `environmentId` remain visible only until global business context is implemented.
2. `site`, `line`, `work center` and `shift` should become compact business context controls.
3. `idempotencyKey` is optional and defaults to a generated value.
4. `sourceService` is not a user-facing field; label source context as business source when it must be shown.
5. Object IDs should be filled from selected rows whenever possible.
6. Empty states must explain the next business action, not just say there is no data.

## Status Rules

Use consistent status semantics across MES, inventory and quality:

| Semantics | Examples | Visual Direction |
| --- | --- | --- |
| Normal / completed | `Ready`, `Completed`, `Closed`, `Passed`, `Available`, `Active` | Green status badge. |
| In progress | `Running`, `Started`, `InProgress` | Blue status badge. |
| Warning / pending | `Pending`, `Warning`, `ConditionalRelease` | Amber status badge. |
| Blocked / failed | `Blocked`, `Failed`, `Rejected`, `Unavailable` | Red status badge. |
| Unknown / neutral | Unknown code or missing value | Slate status badge. |

## Current P0 Scope

Implemented in this PR:

1. Reworded and reordered MES navigation into production-flow terminology.
2. Changed the home page from a route directory to a workbench entry with business action groups.
3. Added app-level action sheet, empty-state and status-badge helpers.
4. Moved the largest direct forms into sheets for work orders, inventory movement, inventory counts, inspections, receipts and schedules.
5. Renamed `基础就绪` to `生产准备检查`.

## Next P0/P1 Scope

P0 after this PR:

1. Add a global business context store for organization, environment, plant, line, work center and shift.
2. Convert operation task rows into actionable execution entries: start, pause, report, exception, call quality.
3. Move `/mes/work-order-detail/:workOrderId` to `/mes/work-orders/:workOrderId` and make the detail page the main work-order action surface.
4. Replace free-text status filters with status segments or Select controls.
5. Add row action menus for work orders, operation tasks, NCRs, receipts and inventory facts.

P1:

1. Add saved views, column visibility, export and batch selection.
2. Add proper count-task and NCR detail flows.
3. Add richer success feedback with next-step links after rush order, report, receipt and schedule actions.
4. Add stronger status dictionary mapping at the app level.

## Non-Goals

1. Do not implement APS/Gantt in this workbench pass.
2. Do not fake release, dispatch, start, pause or close APIs before backend support exists.
3. Do not make MES maintain master data, engineering, inventory, quality, barcode or numbering rules.
4. Do not build mobile/PDA screens in this PC workbench scope.
5. Do not bypass BusinessGateway or hand-edit generated API clients.
