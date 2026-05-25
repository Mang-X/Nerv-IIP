# Scheduling Visualization Productization Design

## Goal

Complete the frontend-only productization pass for `@nerv-iip/scheduling-visualization` while keeping it fully decoupled from backend services. The package exposes typed component inputs, selection events, and preview/commit intents; external adapters can later connect generated API clients without changing the visual components.

## Scope

This slice stays inside `frontend/packages/scheduling-visualization`, `frontend/DESIGN`, and architecture docs. It does not add Gateway endpoints, OpenAPI snapshots, generated api-client code, persistence, backend scheduling rules, or a Console business route.

## Component Boundary

1. `GanttChart` receives a `GanttFixture`, display options, selection, preview windows, optional filter text, and emits `select`, `toggleExpand`, and `previewCommand`.
2. `ScheduleChart` receives a `ScheduleFixture`, display options, selection, preview windows, optional filter text, and emits `select` and `previewCommand`.
3. `SchedulingWorkspace` composes both charts, owns mock fallback state only, and re-emits `selectionChange`, `previewCommand`, `commitPreview`, and `resetPreview` so a future host page can adapt those events to any backend or local store.
4. `SchedulingToolbar` remains a dense operational toolbar and adds search/filter input and explicit commit/reset preview actions.
5. A package-local Vite preview harness is allowed for browser validation. It is not a Console route and is not part of platform navigation.

## Feature Set

1. Time-axis header for day/week/month zoom, rendered in DOM above the chart body.
2. Search/filter support for task/resource/operation names and codes.
3. Virtualized visible rows for DOM row controls and overlays using `calculateVisibleRowRange`.
4. Pointer drag preview for task and operation bars. Dragging emits a typed preview command; the workspace command stack applies it locally.
5. Public intent events for host integration: preview command, commit preview, reset preview, and selection change.
6. Browser preview page with both chart modes, mock data, toolbar controls, drag preview, detail panel, and responsive layout.

## Non-Goals

1. No real backend connection.
2. No frontend finite-capacity scheduling decisions.
3. No route inside `frontend/apps/console`.
4. No export to PNG/SVG/Excel/PDF in this slice.
5. No full keyboard drag/resize interaction in this slice; click selection and buttons remain keyboard-accessible.

## Validation

1. Unit/component tests cover time-axis rendering, filtering, virtualization, drag preview events, and workspace event re-emission.
2. `pnpm -C frontend --filter @nerv-iip/scheduling-visualization test`
3. `pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck`
4. `pnpm -C frontend typecheck`
5. `pnpm -C frontend test`
6. `pnpm -C frontend build`
7. Browser validation against the package-local preview in desktop and mobile viewports.

