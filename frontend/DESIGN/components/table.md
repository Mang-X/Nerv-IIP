# Table (NvDataTable)

Displays tabular data for entity lists. App code uses **`NvDataTable`** from
`@nerv-iip/ui` — a complete data table (toolbar search, column filters,
sorting, column settings, selection + bulk bar, built-in skeleton loading,
empty message and pagination). The un-prefixed `Table*` parts are the shadcn
原版 primitives — library-internal only; do not hand-compose them in app code.

## Core API

- `columns: NvDataTableColumn[]` — `{ key, header, align?, sortable?, filter?: 'text' | 'enum', width?, cellClass?, accessor? … }`.
- `rows` + `rowKey` (field name or function).
- `loading` — renders `skeletonRows` skeleton rows automatically.
- `emptyMessage` — built-in zero-state row (default `暂无数据`).
- Cell content overrides via named slots: `#cell-<key>="{ row }"`.
- Server-side data: `manual` + `v-model:page` + `:total-items` + `:page-size` (page is 1-based); turn off `client-sort` when the server sorts.
- Extras: `selectable` (+ `#bulk-actions` slot), `tabs`/`tabKey` quick filters, `refreshable`, `stickyHeader`, `rowClass`.

## Usage

```vue
<NvDataTable
  :columns="[
    { key: 'name', header: 'Name' },
    { key: 'email', header: 'Email' },
    { key: 'status', header: 'Status' },
    { key: 'actions', header: '', align: 'end', width: 'w-16' },
  ]"
  :rows="items"
  row-key="id"
  :loading="pending"
  empty-message="No users match the current filters."
  manual
  v-model:page="page"
  :total-items="totalCount"
  :page-size="pageSize"
>
  <template #cell-status="{ row }">
    <NvStatusBadge :value="row.status" />
  </template>
  <template #cell-actions="{ row }">
    <!-- row actions via NvDropdownMenu -->
  </template>
</NvDataTable>
```

## Column Conventions

| Column type         | Convention                                             |
| ------------------- | ------------------------------------------------------ |
| Primary identifier  | `cellClass: 'font-medium'`                             |
| UUID / technical ID | `cellClass: 'font-mono text-xs text-muted-foreground'` |
| Status              | `#cell-<key>` slot containing `<NvStatusBadge>` only   |
| Actions             | last column, `align: 'end'`, narrow `width`            |
| Timestamps          | `cellClass: 'text-muted-foreground'`                   |

## Do NOT

- Do not hand-compose 原版 `Table`/`TableRow`/`TableEmpty` in app code — `NvDataTable` already covers loading, empty and pagination states.
- Do not wrap `NvDataTable` in an `NvCard` — it brings its own bordered surface.
- Do not roll your own skeleton rows or empty row — use `loading` and `emptyMessage`.
- Do not put multiple action buttons directly in cells — use an `NvDropdownMenu` per row.
