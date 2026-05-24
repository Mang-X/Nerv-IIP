# Recommended Components to Install

These shadcn-vue components are not yet installed but are relevant for the Nerv-IIP industrial IoT control plane. Install as needed per feature.

## Already Closed By #143

The following business-console readiness components are now available from `@nerv-iip/ui`: `Tabs`, `Sheet`, `Popover`, `DatePicker`, `DateRangePicker`, `ChartContainer`, `ChartLegendContent`, `ChartTooltipContent`, `FileUpload`, `Progress`, and `ScrollArea`.

## Medium Priority (needed for upcoming business platform features)

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

## Lower Priority (polish / advanced features)

### `resizable`

For resizable panel layouts (e.g. code editor / output split view for connector config).

### `stepper` (custom — not in shadcn-vue core)

For multi-step onboarding flows (register instance → configure connector → validate connection).

## Installation procedure

1. Run `pnpm dlx shadcn-vue@latest add <name>` from `frontend/`.
2. Source files land in `packages/ui/src/components/ui/<name>/`.
3. Export all public parts from `packages/ui/src/index.ts`.
4. Add a component spec under `DESIGN/components/<name>.md`.
5. Update `DESIGN/index.md` component table.
