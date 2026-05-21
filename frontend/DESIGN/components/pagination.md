# Pagination

Server-side page navigation. Implemented via the `IamPagination` wrapper component.

## Wrapper component

`frontend/apps/console/src/components/iam/IamPagination.vue`

This component encapsulates the shadcn Pagination primitives with a "Showing X–Y of N" label. Use it for all paginated list pages instead of composing the primitives by hand.

## Usage

```vue
<IamPagination
  :page-index="pageIndex"
  :page-size="pageSize"
  :total-count="totalCount"
  @page-change="pageIndex = $event"
/>
```

## Props

| Prop | Type | Purpose |
|---|---|---|
| `pageIndex` | `number` | Current page (1-based) |
| `pageSize` | `number` | Items per page |
| `totalCount` | `number` | Total record count from API |

## Visibility

The component renders nothing when `totalCount <= pageSize` — no need to conditionally wrap it.

## Do NOT

- Do not compose Pagination primitives directly in page files — use `IamPagination`.
- Do not use client-side pagination for large datasets — always pass the server-side total.
- Do not hardcode `pageSize` in the component — receive it from the composable.
- Do not show pagination above the table — always below.

## Raw Pagination Primitives

Only use these when building a new wrapper component that cannot use `IamPagination`:

```vue
<Pagination :page="page" :items-per-page="20" :total="totalCount" @update:page="onPageChange">
  <PaginationContent v-slot="{ items }">
    <PaginationPrevious />
    <template v-for="(item, i) in items" :key="item.type === 'page' ? item.value : `e-${i}`">
      <PaginationItem v-if="item.type === 'page'" :value="item.value" :is-active="item.value === page">
        {{ item.value }}
      </PaginationItem>
      <PaginationEllipsis v-else />
    </template>
    <PaginationNext />
  </PaginationContent>
</Pagination>
```
