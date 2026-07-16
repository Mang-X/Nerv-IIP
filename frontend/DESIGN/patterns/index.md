# Patterns Index

Quick lookup: scenario → which pattern to apply.

## Cross-cutting standards (横切规范)

| Scenario                                                                                          | Pattern                  | File                            |
| ------------------------------------------------------------------------------------------------- | ------------------------ | ------------------------------- |
| 表单承载 / 行操作 / 列表-详情 / 操作后引导与失效 / 空态·批量·筛选 / PDA 条款 — W2/W3 交互验收依据 | Interaction Patterns v1  | `interaction-patterns.md`       |
| 操作反馈：toast vs 内联校验（单一规则）                                                           | Feedback & Notifications | `feedback-and-notifications.md` |

## Pages (full page templates)

| Scenario                                           | Pattern               | File                             |
| -------------------------------------------------- | --------------------- | -------------------------------- |
| Authentication / sign in                           | Login Page            | `pages/login-page.md`            |
| CRUD entity list (IAM admin, any list)             | List Page             | `pages/list-page.md`             |
| Business Console 列表工作台（stage-B 基线）        | List Workbench        | `pages/list-workbench.md`        |
| 主数据六类页型（树-详情/列表/月历/矩阵/表单/主从） | Master Data Templates | `pages/master-data-templates.md` |

## Flows (multi-step interactions)

| Scenario                                         | Pattern         | File                       |
| ------------------------------------------------ | --------------- | -------------------------- |
| Create a new entity                              | Create Dialog   | `flows/create-dialog.md`   |
| Confirm an irreversible action (delete, disable) | Confirm Destroy | `flows/confirm-destroy.md` |

## Blocks (functional page sections)

| Scenario                                       | Pattern          | File                         |
| ---------------------------------------------- | ---------------- | ---------------------------- |
| FE-2 区块库总览（Nv\* 区块清单 + 取代关系）    | Block Library v2 | `blocks/block-library-v2.md` |
| App chrome: top domains + side nav (AppShellT) | App Shell        | `blocks/app-shell.md`        |
| Breadcrumb-as-title page header (NvPageHeader) | Page Header      | `blocks/page-header.md`      |
| Search + filter + action bar (NvToolbar)       | Toolbar          | `blocks/toolbar.md`          |
| Entity list table (NvDataTable)                | Data Table       | `blocks/data-table.md`       |
| Record count + page navigation                 | Pagination Bar   | `blocks/pagination-bar.md`   |
