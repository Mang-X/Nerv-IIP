# Recommended Components to Install

These shadcn-vue components are not yet installed but are relevant for the Nerv-IIP industrial IoT control plane. Install as needed per feature.

## High Priority (very likely needed)

### `tabs`

```bash
pnpm dlx shadcn-vue@latest add tabs
```

**Why**: Detail pages for instances, operations, and devices will need tabbed sections (e.g. Overview / Config / Metrics / Events). Also needed for settings pages.

**Pattern use**: Detail page tab navigation.

---

### `popover`

```bash
pnpm dlx shadcn-vue@latest add popover
```

**Why**: Date range pickers, advanced filter panels, and column visibility toggles in data tables all use Popover as the floating container.

---

### `scroll-area`

```bash
pnpm dlx shadcn-vue@latest add scroll-area
```

**Why**: Replaces raw `overflow-y-auto` on constrained-height scrollable panels (e.g. `RolePermissionEditor`'s `max-h-[min(45vh,28rem)]` container). Provides consistent scrollbar styling across platforms.

---

## Medium Priority (needed for upcoming business platform features)

### `sheet`

```bash
pnpm dlx shadcn-vue@latest add sheet
```

**Why**: Slide-in detail panels for entity inspection without leaving the list page. Ideal for instance detail (replacing the current side-panel component), device detail, and work order detail.

---

### `progress`

```bash
pnpm dlx shadcn-vue@latest add progress
```

**Why**: Operation task progress bars, MES execution completion %, WMS batch progress. Already implicit in the `OperationTimeline` but not formalized.

---

### `toggle` / `toggle-group`

```bash
pnpm dlx shadcn-vue@latest add toggle toggle-group
```

**Why**: View mode switches (table vs. card view), filter toggle groups (e.g. status filter pills), chart time range selectors.

---

### `breadcrumb`

> **Already installed** as a UI primitive (`@nerv-iip/ui`). Listed here for context — no CLI install needed.

**Why**: Deep entity hierarchies (Plant → Line → Station → Device) require breadcrumb navigation. Use via the `#header` slot in `AppShell`.

---

### `command`

```bash
pnpm dlx shadcn-vue@latest add command
```

**Why**: Powers the Combobox pattern for searching large datasets — required when `Select` is insufficient (e.g. assigning a product to a work order from thousands of SKUs, selecting a device from a large inventory).

---

### `date-picker` (via `calendar` + `popover`)

```bash
pnpm dlx shadcn-vue@latest add calendar range-calendar
```

**Why**: Maintenance scheduling, production planning date ranges, telemetry time window selection. Compose with `Popover` + an `Input` trigger.

---

## Lower Priority (polish / advanced features)

### `resizable`

For resizable panel layouts (e.g. code editor / output split view for connector config).

### `stepper` (custom — not in shadcn-vue core)

For multi-step onboarding flows (register instance → configure connector → validate connection).

### `chart` (shadcn charts via recharts or unovis)

For telemetry dashboards — industrial sensor data, OEE gauges, throughput trends. Consider `unovis` or `chart.js` rather than recharts for Vue 3.

---

## Installation procedure

1. Run `pnpm dlx shadcn-vue@latest add <name>` from `frontend/`.
2. Source files land in `packages/ui/src/components/ui/<name>/`.
3. Export all public parts from `packages/ui/src/index.ts`.
4. Add a component spec under `DESIGN/components/<name>.md`.
5. Update `DESIGN/index.md` component table.
