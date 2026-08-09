# 表格（NvDataTable）

展示实体列表的表格数据。应用代码使用 **`NvDataTable`**，它来自 `@nerv-iip/ui`，
它是一套完整的数据表格（工具栏搜索、列筛选、排序、列设置、选择 + 批量操作栏、内置骨架加载、
空状态文案和分页）。无前缀的 `Table*` 部件是 shadcn 原版 primitive，仅限库内部使用；
不得在应用代码中手动组合它们。

## 核心 API

- `columns: NvDataTableColumn[]` — `{ key, header, align?, sortable?, filter?: 'text' | 'enum', width?, cellClass?, accessor? … }`.
- `rows` + `rowKey`（字段名或函数）。
- `loading` 自动渲染 `skeletonRows` 骨架行。
- `emptyMessage`：内置零状态行（默认 `暂无数据`）。
- 通过具名 slot 覆盖单元格内容：`#cell-<key>="{ row }"`。
- 服务器端数据：`manual` + `v-model:page` + `:total-items` + `:page-size`（page 从 1 开始）；由服务器排序时关闭 `client-sort`。
- 扩展项：`selectable`（+ `#bulk-actions` slot）、`tabs`/`tabKey` 快速筛选、`refreshable`、`stickyHeader`、`rowClass`。

## 用法

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

## 列约定

| 列类型              | 约定                                                   |
| ------------------- | ------------------------------------------------------ |
| 主标识符            | `cellClass: 'font-medium'`                             |
| UUID / 技术 ID      | `cellClass: 'font-mono text-xs text-muted-foreground'` |
| 状态                | `#cell-<key>` slot 中仅包含 `<NvStatusBadge>`           |
| 操作                | 最后一列，`align: 'end'`，窄 `width`                   |
| 时间戳              | `cellClass: 'text-muted-foreground'`                   |

## 禁止事项

- 不得在应用代码中手动组合原版 `Table`/`TableRow`/`TableEmpty`；`NvDataTable` 已覆盖加载、空状态和分页状态。
- 不得将 `NvDataTable` 包在 `NvCard` 中；它自带有边框的表面。
- 不得自行实现骨架行或空行；应使用 `loading` 和 `emptyMessage`。
- 不得在单元格中直接放置多个操作按钮；每行应使用一个 `NvDropdownMenu`。
