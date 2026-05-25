# Block: Scheduling Workspace
Interactive planning workspace that combines a toolbar, Gantt view, schedule view, and adjacent detail panel.

Category: block

## When to use
- A page needs task timeline and resource schedule inspection in the same planning context.
- Users need to switch between Gantt and resource scheduling without leaving the workflow.
- The data source is a mock fixture or a future frozen APS/MES schedule query contract.

## When NOT to use
- Static KPIs or business charts -> use the Chart component pattern.
- Simple tables of work orders or operations -> use Data Table.
- Console MVP without backend schedule contracts -> do not create a route; keep the package-local workspace only.

## Components used
| Component | Role in this pattern |
| --- | --- |
| `SchedulingToolbar` | Mode switch, zoom level, visible layer toggles, undo/redo/reset. |
| `SchedulingLegend` | Operator-facing explanation of baseline, capacity, conflict, today, dependency, and calendar markers. |
| `GanttChart` | Task hierarchy, baseline, dependency, milestone, and conflict visualization. |
| `ScheduleChart` | Resource row, operation, dependency, calendar highlight, capacity, and overload visualization. |
| `SchedulingDetailSheet` | Selection detail without replacing the planning canvas. |

## Interaction rules
- The toolbar owns view state; charts receive props and emit typed selection events.
- Selecting a task, resource, or operation opens detail content in the adjacent panel.
- Preview moves stay client-side until a future explicit save intent exists.
- The package must work with mock fixtures and must not require a real backend.
- Search filters task/resource/operation names and codes without mutating the source fixture.
- Dragging a task or operation bar emits a preview command; the host decides whether that preview can be committed.
- Operation bars may stack into visual lanes to keep short or adjacent work blocks clickable. Business-invalid overlap must be expressed as a conflict payload, not inferred by the chart.
- Calendar, maintenance, downtime, and changeover windows appear as row-local highlights and can be selected for detail inspection.
- The links toggle applies to both Gantt dependencies and schedule operation dependencies.

## Do's and Don'ts
- Do: keep the workspace dense, scan-friendly, and operational.
- Do: keep Console page integration separate from the component package.
- Do: validate package changes in the package-local preview before proposing Console integration.
- Don't: add marketing copy, hero layouts, or decorative panels.
- Don't: infer scheduling conflicts in the frontend; display conflicts from the fixture or API payload.
