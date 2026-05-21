# Empty

Full empty state for a section or page area when there is no data to display.

## When to use which empty component

| Use `Empty` | Use `TableEmpty` |
|---|---|
| Full section/page with no data (first-use, zero state) | Inside a `Table` when query returns 0 rows |
| After filtering produces zero results in a non-table view | After filtering in a table |

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
    <Button type="button" @click="openCreate">Register instance</Button>
  </EmptyContent>
</Empty>

<!-- Inside a Table (TableEmpty, not Empty) -->
<TableEmpty :colspan="5">
  No users match the current filters.
</TableEmpty>
```

## Copy Guidelines

- `EmptyTitle`: declarative, e.g. "No users found", "No active sessions".
- `EmptyDescription`: explain why and what to do, e.g. "Create a user to get started."
- `EmptyContent`: optional CTA button — only include if there's an obvious next action.
- Keep messaging in English to match the rest of the console UI.

## Do NOT

- Do not show Empty during loading — show Skeleton rows instead.
- Do not use a generic "No data" message — be specific about what is empty and why.
- Do not omit `TableEmpty` from Table components — never leave the tbody visually empty.
