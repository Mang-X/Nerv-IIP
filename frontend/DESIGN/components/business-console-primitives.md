# Business Console Primitives

These primitives close the #143 business-console gap for dense detail pages
and adjacent edit surfaces. Names updated to the NvUI (ADR 0020) current state.

## Exports

- `NvTabs`, `NvTabsList`, `NvTabsTrigger`, `NvTabsContent`
- `NvSheet`, `NvSheetTrigger`, `NvSheetContent`, `NvSheetHeader`, `NvSheetTitle`, `NvSheetDescription`, `NvSheetFooter`, `NvSheetClose`
- `Popover`, `PopoverTrigger`, `PopoverContent`, `PopoverAnchor` — 原版 kept as canonical (no `NvPopover` yet); most popup needs are already covered by `NvSelect` / `NvSearchSelect` / `NvCombobox` / `NvDatePicker` / `NvPopconfirm`
- `Progress` — 原版 kept as canonical (no Nv rebuild yet)
- `ScrollArea`, `ScrollBar` — 原版 kept as canonical (no Nv rebuild yet)

## Contract

1. Tabs are for peer sections inside one object detail, not app-level navigation.
2. Sheets preserve list context for adjacent detail, inspection and edit panels — prefer `NvSheet` over `NvDialog` for longer forms next to a list.
3. Popover is for compact anchored controls; modal workflows use `NvDialog` or `NvSheet`.
4. Progress represents numeric work completion; status labels still use `NvStatusBadge`.
5. ScrollArea is used for constrained lists and panels, not whole-page scrolling.

## Rules

Import every part from `@nerv-iip/ui`. App code must not deep-import shadcn,
`reka-ui`, or `packages/ui/src/components/ui/*`.
