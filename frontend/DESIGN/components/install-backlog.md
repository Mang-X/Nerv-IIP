# Recommended Components to Install

Status ledger for components that were once missing. Checked against
`packages/ui/src/index.ts` and the `pc/` layer (post-ADR-0020 NvUI state).

## Already Delivered

- **#143 business-console set** — now available from `@nerv-iip/ui` under their
  NvUI names: `NvTabs*`, `NvSheet*`, `NvDatePicker`, `NvDateRangePicker`,
  `FileUpload`, plus 原版-canonical `Popover*`, `Progress`, `ScrollArea` and
  the chart layer (now app-facing as `NvAreaChart` / `NvLineChart` /
  `NvBarChart` / `NvDonutChart`).
- **`command`** — ✅ delivered as `NvCommand` (pc layer).
- **Combobox pattern** — ✅ delivered as `NvCombobox` (type-to-filter, free
  input allowed) and `NvSearchSelect` (searchable popup single-select) for
  large datasets (SKUs, devices, technicians).
- **`breadcrumb`** — ✅ installed as a 原版 primitive and exported from
  `@nerv-iip/ui` (`Breadcrumb*`); no Nv rebuild yet.

## Still Open

### `toggle` / `toggle-group`

Not installed (`components/ui/` has no `toggle`). **Why**: view mode switches
(table vs. card view), filter toggle pills, chart time range selectors.
Interim: `NvTabs` quick filters or `NvDataTable` `tabs` cover most cases;
screen/touch layers have `NvScreenSegmented` / `NvTouchSegmented`.

### `resizable`

For resizable panel layouts (e.g. code editor / output split view for
connector config).

### `stepper` (custom — not in shadcn-vue core)

For multi-step onboarding flows (register instance → configure connector →
validate connection).

## Installation procedure (per ADR 0020)

1. Run `pnpm dlx shadcn-vue@latest add <name>` from `frontend/` — the 原版
   lands in `packages/ui/src/components/ui/<name>/` and stays byte-for-byte
   unchanged.
2. Export the 原版 parts from `packages/ui/src/index.ts` (library-internal
   baseline).
3. If app surfaces need it, copy-rebuild a branded version in the matching
   layer (`pc/` / `touch/` / `screen/`), named per ADR 0020 §1.2 R1–R5
   (usually `Nv` + plain name), and export it from the layer barrel — app code
   only ever uses the `Nv*` name.
4. Add/update the design-system docs page
   (`frontend/apps/design-system/docs/`) and the contract tests
   (`nvui-naming`), plus a spec under `DESIGN/components/<name>.md`.
