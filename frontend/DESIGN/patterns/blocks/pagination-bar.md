# Block: Pagination Bar

Shows record range and page navigation below a data table. Implemented as `IamPagination`.

## Component location

`frontend/apps/console/src/components/iam/IamPagination.vue`

## Usage

```vue
<IamPagination
  :page-index="pageIndex"
  :page-size="20"
  :total-count="totalCount"
  @page-change="pageIndex = $event"
/>
```

## Layout

Responsive stack: count label on left, page controls on right (flex row on `sm:`, stacked on mobile).

Renders nothing (no DOM) when `totalCount <= pageSize` — always safe to include unconditionally.

## Complete page + pagination example

```vue
<div class="flex flex-col gap-4">
  <!-- Toolbar -->
  <IamListToolbar … />

  <!-- Table -->
  <div class="overflow-hidden rounded-lg border bg-background">
    <Table>…</Table>
  </div>

  <!-- Pagination — always below table -->
  <IamPagination
    :page-index="pageIndex"
    :page-size="pageSize"
    :total-count="totalCount"
    @page-change="pageIndex = $event"
  />
</div>
```

## Do NOT

- Do not show pagination above the table.
- Do not implement client-side pagination for large result sets.
- Do not hardcode `pageSize` — let the composable or a user preference control it.
