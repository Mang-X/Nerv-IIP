# Block: Pagination Bar

Shows record range and page navigation below a data table.

## Current implementations

| 场景                             | 用法                                                                                                                          |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| 实体列表（默认）                 | `NvDataTable` 内建分页：`manual` + `v-model:page` + `:total-items` + `:page-size`（服务端 1-based，见 `components/table.md`） |
| 非表格的分页面（卡片墙、时间线） | 独立 `NvPagination`（`@nerv-iip/ui`，props/emits 见 `components/pagination.md`）                                              |

## Rules

- 分页永远在列表**下方**，不放上方。
- 服务端分页为默认；大结果集禁止客户端分页。
- `pageSize` 不硬编码 —— 由 composable 或用户偏好控制。
- 页码契约是 **1-based**（网关约定），换页后滚动回列表顶部。

## Do NOT

- Do not show pagination above the table.
- Do not implement client-side pagination for large result sets.
- Do not hardcode `pageSize`.
