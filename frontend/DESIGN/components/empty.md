# Empty

Full empty state for a section or page area when there is no data to display.

> NvUI status: there is no `NvEmpty` on the PC layer — the `Empty*` family is
> the current canonical export from `@nerv-iip/ui` (原版 primitives kept as the
> app-facing name until a brand rebuild exists). Mobile uses `NvMobileEmpty`
> from `@nerv-iip/ui-mobile`.

## When to use which empty state

| Use `Empty`                                               | Use `NvDataTable`'s `emptyMessage` |
| --------------------------------------------------------- | ---------------------------------- |
| Full section/page with no data (first-use, zero state)    | A table query returning 0 rows     |
| After filtering produces zero results in a non-table view | After filtering in a table         |

(The 原版 `TableEmpty` row belongs to hand-composed 原版 tables, which app code
no longer builds — `NvDataTable` renders its own empty row from
`emptyMessage`.)

## Usage

```vue
<!-- Full section empty state -->
<Empty>
  <EmptyMedia>
    <InboxIcon class="size-12 text-muted-foreground" aria-hidden="true" />
  </EmptyMedia>
  <EmptyHeader>
    <EmptyTitle>No instances found</EmptyTitle>
    <EmptyDescription>
      There are no application instances registered in this control plane yet.
    </EmptyDescription>
  </EmptyHeader>
  <EmptyContent>
    <NvButton type="button" @click="openCreate">Register instance</NvButton>
  </EmptyContent>
</Empty>

<!-- Inside a table: built into NvDataTable -->
<NvDataTable :rows="items" empty-message="No users match the current filters." … />
```

## Copy Guidelines

- `EmptyTitle`: declarative, e.g. "No users found", "No active sessions".
- `EmptyDescription`: explain why and what to do, e.g. "Create a user to get started."
- `EmptyContent`: optional CTA button — only include if there's an obvious next action.

## Do NOT

- Do not show Empty during loading — show Skeleton (or `NvDataTable`'s built-in `loading`) instead.
- Do not use a generic "No data" message — be specific about what is empty and why.
- Do not leave a zero-result area visually blank — every data surface needs an explicit empty state.
