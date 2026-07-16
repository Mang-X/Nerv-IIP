# Pagination (NvPagination)

Server-side page navigation.

> The old console-app pagination wrapper described by earlier versions of this
> doc no longer exists. Current state:
>
> - **List pages built on `NvDataTable`** get pagination for free — set
>   `manual` with `v-model:page`, `:total-items` and `:page-size` on the table
>   (see `table.md`). This is the default for entity lists.
> - **Standalone `NvPagination`** (from `@nerv-iip/ui`) is for paginated
>   surfaces that are not tables (card grids, timelines).
>
> The un-prefixed `Pagination*` parts are the shadcn 原版 primitives —
> library-internal only.

## NvPagination API

Props: `page` (1-based), `pageSize` (number or string), `totalItems`,
`pageSizeOptions` (default `[10, 20, 50, 100]`), `siblingCount`, `showJump`,
`showEdges`. Emits `update:page` / `update:pageSize`. Includes numbered pages
with ellipsis, first/last + prev/next, a page-size select and a result summary.

## Usage

```vue
<NvPagination
  v-model:page="page"
  :page-size="pageSize"
  :total-items="totalCount"
  @update:page-size="pageSize = $event"
/>
```

## Do NOT

- Do not compose 原版 `Pagination*` primitives in page files — use `NvDataTable`'s built-in footer or `NvPagination`.
- Do not use client-side pagination for large datasets — always pass the server-side total.
- Do not hardcode `pageSize` in the component — receive it from the composable.
- Do not show pagination above the table — always below.
- Remember the gateway page contract is **1-based** (`pageIndex` starts at 1).
