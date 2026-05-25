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
| `SchedulingDetailSheet` | Adjacent selected-fact inspection | Does not fetch data. |

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
- `day`, `week`, and `month` zoom must update both timeline labels and chart content width so bars and axis remain visually linked.
- Timeline labels should keep padding at the first and last tick to avoid clipped edge text.

## Do's and Don'ts
- Do: import only from `@nerv-iip/scheduling-visualization`.
- Do: keep Leafer-specific APIs inside the package adapter.
- Do: pass immutable fixture snapshots and explicit preview maps to renderers.
- Do: use `pnpm -C frontend --filter @nerv-iip/scheduling-visualization dev` for package-local browser checks.
- Do: keep the package preview wired to Tailwind/shadcn sources and semantic tokens so `@nerv-iip/ui` primitives render as design-system components, not native controls.
- Don't: import `leafer-ui` directly from Console pages.
- Don't: connect these components to generated API clients in this foundation slice.
- Don't: create page-local Gantt CSS or a second scheduling component set.

## Tokens and color
Leafer scene elements use package-owned renderer colors because canvas nodes cannot consume Tailwind classes. DOM controls should still use `@nerv-iip/ui` primitives and semantic tokens where possible.
