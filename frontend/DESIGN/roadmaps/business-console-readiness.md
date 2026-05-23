# Business Console Component Readiness

This roadmap is the canonical design-system scope for #143. Superpowers plans may reference it, but component decisions belong here.

## Decision

Business console readiness is design-system work first and implementation work second. New primitives must be added to `@nerv-iip/ui`, documented in `frontend/DESIGN`, and only then consumed by app pages.

## Immediate Component Set

| Capability | Design-System Direction | Notes |
| --- | --- | --- |
| Tabs | Install shadcn-vue `tabs` and export all public parts. | Used for dense detail pages such as order, work order, device and SKU detail. |
| Sheet | Install shadcn-vue `sheet` and export all public parts. | Used for slide-in inspection/detail/edit panels from list pages. |
| Date and range pickers | Compose shadcn-vue `popover`, `calendar` and `range-calendar`. | Keep business date ranges compact and form-friendly. |
| Charts | Use shadcn-vue `chart` as the base primitive and wrap domain-specific chart shells in `@nerv-iip/ui` only when repeated usage appears. | Chart examples are references, not page-local visual systems. |
| File upload | Build a Nerv-IIP FileUpload wrapper with shadcn structure and FileStorage semantics. | UI talks to FileStorage upload sessions and tus/download endpoints, never MinIO directly. |
| Progress | Install shadcn-vue `progress` when upload, batch or operation progress appears. | Used by FileUpload and execution progress indicators. |
| Scroll area | Install shadcn-vue `scroll-area` for constrained task/detail lists. | Avoid page-local scrollbar styling. |

## FileUpload Direction

Prefer Uppy core/headless plus `@uppy/tus` behind a Nerv-IIP shadcn-styled wrapper when uploads need resumability, retry, pause/resume, progress and protocol compatibility. Uppy provides Vue integration, headless/custom UI options and a tus plugin, so it lets the design system own visuals while avoiding a hand-written tus client.

Do not use Uppy Dashboard as the default visual baseline. Its behavior can inspire interaction details, but the rendered shell should remain Calm Control Plane and use `@nerv-iip/ui` primitives.

A custom tus client is acceptable only for a narrow, single-file local upload path where dependency weight is more important than protocol coverage. It should not become the default for business attachments, CAD packages, quality evidence or maintenance photos.

## FileUpload Contract

The first FileUpload primitive should expose:

1. `purpose`, `ownerService`, `ownerType`, `ownerId`, `organizationId`, `environmentId`.
2. Accepted content types, max file size and max file count.
3. Current upload rows with file name, size, status, progress and error.
4. Completed `fileId` values only; no object keys, bucket names or long-lived URLs.
5. Transport adapter support for FileStorage `server-proxy` and `tus` modes.
6. Error states for rejected size/type, expired session, checksum mismatch and upload interruption.

## Chart Contract

Charts should:

1. Use semantic chart tokens from the console token contract.
2. Prefer line, bar and donut/pie shapes for the first business dashboards.
3. Keep legends, tooltips and axes readable in dense panels.
4. Avoid decorative gradients or one-off palettes.
5. Provide empty/loading/error states using existing `Empty`, `Skeleton`, `Alert` and `Spinner` primitives.

## Date Picker Contract

Date controls should:

1. Use Popover + Calendar/RangeCalendar composition.
2. Support clear, apply and cancel behavior for range filters.
3. Return typed `DateOnly`-compatible ISO date strings at API boundaries.
4. Use compact triggers suitable for toolbar filters and form fields.
5. Avoid page-local calendar styling.

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

