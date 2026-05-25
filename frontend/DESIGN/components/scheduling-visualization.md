# Scheduling Visualization
Reusable Gantt and schedule visualization components for simulated planning data.

Layer: Component package | Source: custom Vue + Leafer UI + `@nerv-iip/ui`

## When to use
- Show mock or API-adapted manufacturing tasks, dependencies, milestones, resource loads, operations, and conflicts.
- Build a future Console scheduling page while keeping canvas rendering behind `@nerv-iip/scheduling-visualization`.
- Test time-scale, row grouping, undo/redo preview, and Leafer renderer behavior without a real backend.
- Host the package from any backend adapter by passing fixtures and listening to typed intent events.

## When NOT to use
- Generic dashboard metrics -> use `ChartContainer` and a chart adapter instead.
- Entity lists, order tables, or master-data search -> use the Data Table pattern instead.
- Real finite-capacity scheduling decisions -> use backend APS/MES contracts; the frontend only displays returned facts.
- Current Console MVP navigation -> do not add a route until the backend query/update contract is frozen.

## Exports
| Export | Use when | Notes |
| --- | --- | --- |
| `GanttChart` | Task hierarchy, dependencies, baselines, milestones | Emits typed task/dependency/conflict selections. |
| `ScheduleChart` | Resource rows, operations, capacity bands | Emits typed resource/operation/conflict selections. |
| `SchedulingWorkspace` | Package-local mock workspace or future page composition | Owns mock fixtures, toolbar state, command stack, detail panel. |
| `SchedulingToolbar` | Mode, zoom, layer toggles, history controls | Uses `@nerv-iip/ui` buttons and lucide icons. |
| `SchedulingLegend` | Explains visual coding for operators | Categorized and collapsible; lives below the workspace to save vertical scanning room. |
| `SchedulingDetailSheet` | Adjacent selected-fact inspection | Opens only after selection and does not fetch data. |
| `createLargeMockGanttFixture` | Package preview and large-data validation | Builds 1,000+ task / multi-year mock Gantt plans without requiring backend data. |
| `createLargeMockScheduleFixture` | Package preview and large-data validation | Builds 1,000+ resource / multi-year mock schedules with operation dependency chains, without requiring backend data. |

## Public integration events

| Event | Payload | Host responsibility |
| --- | --- | --- |
| `selectionChange` | `SchedulingWorkspaceSelection \| undefined` | Sync external detail state if needed. |
| `previewCommand` | `SchedulingPreviewCommand` | Record, validate, or mirror a local preview command. |
| `commitPreview` | `Record<string, SchedulingPreviewWindow>` | Convert preview windows to an external save command. |
| `resetPreview` | none | Clear external preview state. |

`SchedulingPreviewWindow` carries `start` and `end`; schedule operation previews may also carry `resourceId` for cross-row reassignment previews. Hosts remain responsible for validating and persisting any submitted intent.

## Interaction details
- Gantt and schedule bars show a live preview while dragging, before a `previewCommand` is emitted.
- Schedule operation blocks must stay inside the resource row height and use `resourceId` from preview state when shown on another row.
- Schedule operation blocks default to exclusive resource semantics: overlapping operations stay in the same lane and show conflict styling. Lanes are reserved for explicit parallel capacity cases where overlapping operations share `resourceUsageMode: 'parallel-capacity'` and `parallelGroupId`.
- Valid parallel examples include an abstracted oven/furnace/curing room capacity pool, multi-cavity tooling, or a work-center row representing several equivalent machines. Single line, single equipment, single crew, or unique tooling work should remain exclusive.
- Dependency links support `none`, `all`, and `selection` modes. Selection mode shows the full connected task/operation chain for the current selection, not only directly adjacent edges.
- Dependency links use orthogonal source/target rectangle routing. They must respect start/finish dependency sides, stay inside the timeline area, and avoid drawing through the source or target block. Same-row forward links with enough horizontal room use a direct side-center connector. Same-row close or overlapping links use a short top or bottom center bridge. Cross-row close finish-to-start links use the available row gap before falling back to the external lane router.
- Dependency links avoid interactive task/operation rectangles only. Calendar, maintenance, downtime, changeover and other row-background highlights are contextual backgrounds and do not force link detours.
- `day`, `week`, and `month` zoom must behave like a Gantt view preset: dates remain stable, but scale unit / tick density / pixels-per-day change. Task and operation bar pixel widths must therefore change even when the viewport is wider than the visible date range; unused right-side space may remain empty.
- Timeline labels should stay visible after `day` / `week` / `month` switches and horizontal scrolling; edge labels should be filtered by viewport rather than forcing off-screen first/last ticks into the DOM.
- Interactive task and operation blocks are DOM overlays only. Leafer renders non-interactive background layers such as grid lines, dependencies, baselines, capacity and conflict markers so canvas progress fills cannot visually duplicate the DOM blocks.
- The left task/resource label column remains frozen during horizontal timeline scrolling, and horizontal scroll resets on zoom changes to prevent stale offsets.
- Chart viewport size changes, including opening or closing the adjacent detail sheet, must resize Leafer overlays in the same frame so canvas layers never keep an older workspace width.
- Horizontal scroll state updates are animation-frame throttled, but Leafer overlays must be fully cleared and redrawn in the same frame as scroll, vertical virtualization, or drag-preview changes so dependencies, baselines, row lines, capacity bands, and today lines never leave old-frame artifacts. Timeline tick DOM is bounded to the visible viewport plus overscan for large time ranges.
- Frozen task/resource label columns must use opaque backgrounds so scrolled timeline bars and canvas links cannot bleed through the side column.
- Drag movement is clamped as a whole time window at range boundaries, preserving task duration instead of shortening bars at the edge.
- Schedule fixtures may include `dependencies` for operation-to-operation links and `calendarHighlights` for working-time, maintenance, downtime, or changeover windows. Large schedule fixtures should include dependency chains as well as resource density so the canvas link layer is validated under realistic load.
- Schedule conflicts may include `submitPolicy` and `reasonCode`; the component displays these facts but does not make APS/MES scheduling decisions.
- Bars and highlight windows expose brief pointer-following hover tooltips for scan-level details. Native `title` attributes and fixed-position shadcn Tooltip triggers are avoided because long timeline bars may place the trigger edge outside the operator's current viewport.

## Slot extension points

`GanttChart` exposes `headerMeta`, `taskRow`, `taskBar`, and `tooltip` slots. `ScheduleChart` exposes `headerMeta`, `resourceRow`, `calendarHighlight`, `operationBar`, and `tooltip` slots. `SchedulingWorkspace` forwards chart customization through `ganttTaskRow`, `ganttTaskBar`, `ganttTooltip`, `scheduleResourceRow`, `scheduleCalendarHighlight`, `scheduleOperationBar`, and `scheduleTooltip`, and also exposes `toolbar`, `detail`, and `legend`.

Slots are for composition around stable fixtures and typed intent events. They must not introduce backend fetching, deep `leafer-ui` access, or a second set of scheduling visuals outside the package boundary.

## Do's and Don'ts
- Do: import only from `@nerv-iip/scheduling-visualization`.
- Do: keep Leafer-specific APIs inside the package adapter.
- Do: pass immutable fixture snapshots and explicit preview maps to renderers.
- Do: use `pnpm -C frontend --filter @nerv-iip/scheduling-visualization dev` for package-local browser checks.
- Do: keep the package preview wired to Tailwind/shadcn sources and semantic tokens so `@nerv-iip/ui` primitives render as design-system components, not native controls.
- Do: keep legend labels visible near the chart whenever baseline, capacity, conflicts, today, links, or calendar overlays are shown.
- Do: keep large data rendering bounded by visible row/time ranges; tests and preview fixtures should cover 1,000+ Gantt tasks, 1,000+ schedule resources, and multi-year timelines.
- Don't: import `leafer-ui` directly from Console pages.
- Don't: connect these components to generated API clients in this foundation slice.
- Don't: create page-local Gantt CSS or a second scheduling component set.

## Tokens and color
Leafer scene elements use package-owned renderer colors because canvas nodes cannot consume Tailwind classes. DOM controls should still use `@nerv-iip/ui` primitives and semantic tokens where possible.
