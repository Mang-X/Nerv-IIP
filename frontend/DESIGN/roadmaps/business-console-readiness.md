# Business Console Component Readiness

This roadmap is the canonical design-system scope for #143. Superpowers plans may reference it, but component decisions belong here.

## Decision

Business console readiness is design-system work first and implementation work second. New primitives must be added to `@nerv-iip/ui`, documented in `frontend/DESIGN`, and only then consumed by app pages.

## Immediate Component Set

| Capability | Design-System Direction | #143 Status |
| --- | --- | --- |
| Tabs | shadcn-vue `tabs` style and public parts. | Delivered in `@nerv-iip/ui`; used for dense detail pages such as order, work order, device and SKU detail. |
| Sheet | shadcn-vue `sheet` style and public parts. | Delivered in `@nerv-iip/ui`; used for slide-in inspection/detail/edit panels from list pages. |
| Date and range pickers | Popover-backed compact DateOnly controls. | Delivered in `@nerv-iip/ui`; current UI uses native date inputs, with `Calendar`/`RangeCalendar` exported only as low-level Reka roots until styled calendar-grid parts are added. |
| Charts | shadcn-style chart shell with semantic token bridge. | Delivered in `@nerv-iip/ui`; page-level chart engines remain adapters, not a second design system. |
| Gantt and scheduling visualization | Standalone `@nerv-iip/scheduling-visualization` package with Leafer behind an adapter and `@nerv-iip/ui` controls. | Delivered as a mock-data component foundation for #78; no Console route and no real backend contract in this slice. |
| File upload | Nerv-IIP FileUpload wrapper with shadcn structure and FileStorage semantics. | Delivered in `@nerv-iip/ui`; UI talks to FileStorage upload sessions and tus/server-proxy transport, never MinIO directly. |
| Progress | shadcn-vue `progress` style. | Delivered in `@nerv-iip/ui`; used by FileUpload and execution progress indicators. |
| Scroll area | shadcn-vue `scroll-area` style. | Delivered in `@nerv-iip/ui`; avoids page-local scrollbar styling. |

## FileUpload Direction

The #143 baseline uses a small native FileStorage transport with a pluggable `transport` prop. It supports the current FileStorage `tus` `HEAD`/`PATCH` path and `server-proxy` binary `PUT` instructions without adding Uppy dependency weight to the design-system package.

The current wrapper includes drag-and-drop, per-row progress, pause/resume through `AbortController`, retry after failed transport attempts, readable file-family labels for common Office/PDF/media formats and light row/drop feedback animations through Vue transitions plus Tailwind semantic classes. It supports both automatic upload and manual queue mode through `autoUpload=false` plus a deliberately small exposed component API for form-level submission orchestration. Large queues switch to a fixed-height virtualized scroll container so bulk attachment workflows do not render every row at once.

Uppy core/headless plus `@uppy/tus` remains the preferred adapter when uploads need richer resumability controls, retry policy, pause/resume UI, source providers or broader tus protocol coverage. It should sit behind the same Nerv-IIP wrapper contract.

Do not use Uppy Dashboard as the default visual baseline. Its behavior can inspire interaction details, but the rendered shell should remain Calm Control Plane and use `@nerv-iip/ui` primitives.

A custom tus client is acceptable for the current narrow FileStorage transport path where dependency weight is more important than full protocol coverage. It should remain replaceable by an Uppy-backed adapter before expanding to large CAD packages or heavy media workflows.

## FileUpload Contract

The first FileUpload primitive should expose:

1. `purpose`, `ownerService`, `ownerType`, `ownerId`, `organizationId`, `environmentId`.
2. Accepted content types, max file size and max file count.
3. Current upload rows with file name, size, matched file family, status, progress and error.
4. Completed `fileId` values only; no object keys, bucket names or long-lived URLs.
5. Transport adapter support for FileStorage `server-proxy` and `tus` modes.
6. Pause/resume, retry and remove actions at row level.
7. Manual queue mode through `autoUpload=false` and exposed `addFiles`, `uploadQueued`, `pauseAll`, `resumeAll`, `retryFailed`, `clear` and `browse` methods.
8. Rejected and failed rows stay visible without consuming available upload slots.
9. Virtualized row rendering for large queues, with configurable threshold, row height and list height.
10. Error states for rejected size/type, expired session, checksum mismatch and upload interruption.

## Chart Contract

Charts should:

1. Use semantic chart tokens from the console token contract.
2. Prefer line, bar and donut/pie shapes for the first business dashboards.
3. Keep legends, tooltips and axes readable in dense panels.
4. Avoid decorative gradients or one-off palettes.
5. Provide empty/loading/error states using existing `Empty`, `Skeleton`, `Alert` and `Spinner` primitives.

## Scheduling Visualization Contract

Gantt and scheduling visualization should:

1. Live in `frontend/packages/scheduling-visualization` and export through `@nerv-iip/scheduling-visualization`.
2. Keep `leafer-ui` behind `canvas/createLeaferSurface.ts` and renderer interfaces.
3. Use `@nerv-iip/ui` for DOM controls such as buttons, badges, toolbar controls and detail actions.
4. Render from mock fixtures or frozen schedule query DTOs; do not fetch directly from backend services.
5. Emit typed selection and preview intent events; do not decide finite-capacity scheduling rules in frontend code.
6. Stay package-local until a future Console page issue freezes route, permissions and API contracts.
7. Use the package-local Vite preview for browser validation; it is not a Console route.
8. Keep Gantt and resource-schedule time axes linked to `day` / `week` / `month` zoom through both label formatting and chart content width; bars must not remain visually fixed while the axis changes.
9. Show live drag previews before commit. Schedule operation previews may include a `resourceId` so hosts can support cross-resource reassignment without coupling the component to a backend.
10. Constrain schedule operation blocks to their resource row height and leave readable axis padding at the first and last timeline labels.
11. Keep interactive task/resource labels in a frozen DOM column during horizontal scrolling; zoom changes must not leave stale scroll offsets that visually separate labels from bars.
12. Avoid duplicate bar rendering between DOM and Leafer. Canvas layers are for background grid/dependencies/baselines/capacity/conflict markers, while interactive bars remain DOM overlays.
13. Preserve task duration when drag previews clamp at timeline range edges.

## Date Picker Contract

Date controls should:

1. Use Popover-backed compact controls for MVP forms and filters.
2. Support clear, apply and cancel behavior for range filters.
3. Return typed `DateOnly`-compatible ISO date strings at API boundaries.
4. Use compact triggers suitable for toolbar filters and form fields.
5. Avoid page-local calendar styling; app pages should not directly use the low-level `Calendar`/`RangeCalendar` roots until styled design-system parts exist.

## Sheet And Tabs Contract

Sheets should be used for adjacent detail or edit surfaces that preserve list context. Tabs should be used only when a detail object has several peer sections. They are not primary navigation.

## Governance

1. Install shadcn-vue components via CLI from `frontend/`.
2. Export all public parts from `frontend/packages/ui/src/index.ts`.
3. Add a component spec under `frontend/DESIGN/components/`.
4. Update `frontend/DESIGN/index.md`.
5. Add or update component contract tests where the export boundary or token contract changes.

## Non-Goals

1. Do not implement MinIO/S3 multipart as part of #143.
2. Do not create a second charting design system.
3. Do not expose FileStorage object keys or direct object-storage URLs.
4. Do not create page-specific upload, chart, date, sheet or tabs styling outside `@nerv-iip/ui`.
5. Do not add a Console Gantt/scheduling route from the mock package foundation alone.
