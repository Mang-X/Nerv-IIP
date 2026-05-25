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
| `GanttChart` | Task hierarchy, baseline, dependency, milestone, and conflict visualization. |
| `ScheduleChart` | Resource row, operation, capacity, and overload visualization. |
| `SchedulingDetailSheet` | Selection detail without replacing the planning canvas. |

## Interaction rules
- The toolbar owns view state; charts receive props and emit typed selection events.
- Selecting a task, resource, or operation opens detail content in the adjacent panel.
- Preview moves stay client-side until a future explicit save intent exists.
- The package must work with mock fixtures and must not require a real backend.

## Do's and Don'ts
- Do: keep the workspace dense, scan-friendly, and operational.
- Do: keep Console page integration separate from the component package.
- Don't: add marketing copy, hero layouts, or decorative panels.
- Don't: infer scheduling conflicts in the frontend; display conflicts from the fixture or API payload.

