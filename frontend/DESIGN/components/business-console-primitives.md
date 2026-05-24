# Business Console Primitives

These primitives close the #143 business-console gap for dense detail pages and adjacent edit surfaces.

## Exports

- `Tabs`, `TabsList`, `TabsTrigger`, `TabsContent`
- `Sheet`, `SheetTrigger`, `SheetContent`, `SheetHeader`, `SheetTitle`, `SheetDescription`, `SheetFooter`, `SheetClose`
- `Popover`, `PopoverTrigger`, `PopoverContent`, `PopoverAnchor`, `PopoverHeader`, `PopoverTitle`, `PopoverDescription`
- `Progress`
- `ScrollArea`, `ScrollBar`

## Contract

1. Tabs are for peer sections inside one object detail, not app-level navigation.
2. Sheets preserve list context for adjacent detail, inspection and edit panels.
3. Popover is for compact filters and date controls; modal workflows use Dialog or Sheet.
4. Progress represents numeric work completion; status labels still use Badge.
5. ScrollArea is used for constrained lists and panels, not whole-page scrolling.

## Rules

Import every part from `@nerv-iip/ui`. App code must not deep-import shadcn or `packages/ui/src/components/ui/*`.
