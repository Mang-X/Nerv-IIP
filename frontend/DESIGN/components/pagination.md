# 分页（NvPagination）

服务器端分页导航。

> 本文档早期版本所述的旧 console-app 分页封装已不复存在。当前状态：
>
> - **基于 `NvDataTable` 的列表页**自动获得分页：在表格上设置 `manual`，并带有
>   `v-model:page`、`:total-items` 和 `:page-size`
>   （见 `table.md`）。这是实体列表的默认方式。
> - **独立的 `NvPagination`**（来自 `@nerv-iip/ui`）用于非表格的分页界面
>   （卡片网格、时间线）。
>
> 无前缀的 `Pagination*` 部件是 shadcn 原版 primitive，仅限库内部使用。

## NvPagination API

Props：`page`（从 1 开始）、`pageSize`（number 或 string）、`totalItems`、
`pageSizeOptions`（默认 `[10, 20, 50, 100]`）、`siblingCount`、`showJump`、
`showEdges`。Emits `update:page` / `update:pageSize`。包含带省略号的页码、
首页/末页 + 上一页/下一页、每页数量选择器和结果汇总。

## 用法

```vue
<NvPagination
  v-model:page="page"
  :page-size="pageSize"
  :total-items="totalCount"
  @update:page-size="pageSize = $event"
/>
```

## 禁止事项

- 不得在页面文件中组合原版 `Pagination*` primitive，应使用 `NvDataTable` 的内置页脚或 `NvPagination`。
- 大型数据集不得使用客户端分页，必须传入服务器端总数。
- 不得在组件中硬编码 `pageSize`，应从 composable（组合式函数）接收它。
- 不得将分页显示在表格上方，必须显示在下方。
- 请注意 Gateway 的分页契约**从 1 开始**（`pageIndex` 从 1 开始）。
