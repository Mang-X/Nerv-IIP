# Block: Pagination Bar

Shows record range and page navigation below a data table.

## 现役实现（Current implementations）

| 场景                             | 用法                                                                                                                          |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| 实体列表（默认）                 | `NvDataTable` 内建分页：`manual` + `v-model:page` + `:total-items` + `:page-size`（服务端 1-based，见 `components/table.md`） |
| 非表格的分页面（卡片墙、时间线） | 独立 `NvPagination`（`@nerv-iip/ui`，props/emits 见 `components/pagination.md`）                                              |

## 规则（Rules）

- 分页永远在列表**下方**，不放上方。
- 服务端分页为默认；大结果集禁止客户端分页。
- `pageSize` 不硬编码 —— 由 composable 或用户偏好控制。
- 页码契约是 **1-based**（网关约定），换页后滚动回列表顶部。

## 判定

- 「分页的是表格吗？」是 → 用 `NvDataTable` 内建分页（不另挂独立分页组件）；不是
  （卡片墙/时间线等非表格集合）→ 独立 `NvPagination`。
- 「页码窗口谁持有？」服务端返回分页结果 → `manual` 模式（页面持 `page`/`pageSize`，
  `total` 用服务端 total）；仅小数据集全量在前端 → 内建客户端分页可用。
- 「发给网关的页码是 1-based 吗？」`pageIndex: 0` 会被网关校验 400（见反例）。

## 正例

`apps/business-console/src/pages/mes/operation-tasks.vue:380`：`NvDataTable` `manual` +
`:page` + `:total-items="operationTasksTotal"`（服务端总量）+ `@update:page`。

## 反例

❌ **发 0-based 页码给 1-based 网关契约** —— `useBusinessMasterData.ts` 曾发
`pageIndex: 0`，网关校验器 `GreaterThan(0)` 直接 400，人员/班组选择器静默空
（P0-1，已修：改 1-based + 前后端契约测试，PR #867）。出处：
`frontend/DESIGN/roadmaps/2026-07-11-ux-walkthrough-findings.md` §3.1 P0-1。
