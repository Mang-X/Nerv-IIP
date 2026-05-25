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
| `SchedulingLegend` | Categorized, collapsible explanation of baseline, capacity, conflict, today, dependency, and calendar markers. |
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
- Operation bars on an exclusive resource remain overlapped when their time windows collide, making the conflict obvious. Bars stack only when the fixture explicitly marks them as a shared parallel capacity group.
- Calendar, maintenance, downtime, and changeover windows appear as row-local highlights and can be selected for detail inspection.
- Link visibility applies to both Gantt dependencies and schedule operation dependencies and supports hidden, all links, or selected-chain modes.
- Selection-chain mode shows the full connected dependency path for the selected task or operation.
- Dependency links route around the source and target task/operation rectangles and stay out of the frozen side column. A link should not overlap a task bar except at the entering edge. When predecessor and successor are already separated horizontally, prefer the compact top-edge connection operators expect from production Gantt tools instead of forcing a long lane detour.
- Dependency links do not route around row-background highlights such as calendar, maintenance, downtime, and changeover windows.
- The detail sheet opens on demand after a selection; operators can use pointer-following hover tooltips for quick scan-level facts.
- Zoom changes are view-preset changes: they alter time density and bar pixel widths while preserving task/operation dates.
- Opening or closing the detail sheet changes the available chart width; timeline DOM, DOM bars, and Leafer overlays must resize together instead of waiting for the next drag or scroll interaction.
- Scroll and drag previews must repaint canvas overlays from a cleared frame, not rely on partial canvas reuse. Frozen side columns remain opaque masks over scrolled timeline content.
- Large Gantt plans and large schedules must keep row labels, operation/task DOM, Leafer overlays, and timeline tick DOM bounded to the visible viewport plus overscan.
- Package preview should expose large-data scenarios for both Gantt and resource schedule so operators and designers can validate scroll, frozen labels, axis sync, density, and dependency-link rendering without a real backend.
- Host pages may customize row labels, bar content, tooltip content, detail content, and legend content through documented slots while keeping scheduling logic and persistence outside this package.

## Do's and Don'ts
- Do: keep the workspace dense, scan-friendly, and operational.
- Do: keep Console page integration separate from the component package.
- Do: validate package changes in the package-local preview before proposing Console integration.
- Don't: add marketing copy, hero layouts, or decorative panels.
- Don't: infer scheduling conflicts in the frontend; display conflicts from the fixture or API payload.
