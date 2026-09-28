# API 契约与代码生成参考索引

本页只维护稳定路径、机器事实入口和已批准的窄例外；不复制 Governance 的长期规则，也不维护 endpoint/operationId 第二份清单。

## 权威入口

| 对象 | 当前路径 / 生产者 |
| --- | --- |
| API 运行时架构 | `docs/architecture/integration/api-contracts.md` |
| API/codegen 治理 | `docs/governance/api/contracts-and-codegen.md` |
| API/codegen Runbook | `docs/runbooks/api-codegen.md` |
| Platform SDK 能力基线 | `docs/architecture/platform/sdk.md` |
| BusinessGateway surface 治理 | `docs/governance/api/business-gateway-surface.md` |
| BusinessGateway restore manifest | `docs/reference/api/business-gateway-surface-restore.manifest.json` |
| Facade coverage 治理 | `docs/governance/api/facade-coverage.md` |
| Facade coverage 机器事实 | `docs/reference/api/facade-coverage-matrix.json` |
| API/codegen 历史总账 | `docs/reports/audits/api-contract-and-codegen.md` |
| BusinessGateway surface 迁移前快照 | `docs/reports/audits/business-gateway-api-surface-canonicalization.md` |
| Facade coverage 历史渲染/决策记录 | `docs/reports/audits/facade-coverage-matrix.md` |

## 当前 OpenAPI 与生成路径

| 对象 | 当前路径 |
| --- | --- |
| PlatformGateway OpenAPI snapshot | `frontend/packages/api-client/openapi/platform-gateway.v1.json` |
| BusinessGateway Console OpenAPI snapshot | `frontend/packages/api-client/openapi/business-gateway-console.v1.json` |
| Hey API 配置 | `frontend/packages/api-client/openapi-ts.config.ts` |
| PlatformGateway generated output | `frontend/packages/api-client/src/generated/` |
| BusinessGateway Console generated output | `frontend/packages/api-client/src/generated/business-console/` |
| Business Console 稳定导出 | `frontend/packages/api-client/src/business-console.ts` |
| api-client 总入口 | `frontend/packages/api-client/src/index.ts` |
| OpenAPI 导出 producer | `scripts/export-gateway-openapi.ps1` |
| OpenAPI/api-client drift verifier | `scripts/verify-openapi-client-drift.ps1` |

当前 Hey API 配置只有 PlatformGateway 和 BusinessGateway Console 两个输入。`business-gateway-mobile.v1.json`、`src/generated/mobile/` 与 `mobile.ts` 属于历史总账中记录的后续演进设想，不是当前已交付机器事实；若未来落地，以届时代码/OpenAPI 配置为准。

工具精确版本以 `frontend/package.json`、受影响 package、.NET build 配置和脚本头声明为准，本页不复制版本号。

## Endpoint / operationId 清单的权威来源

当前 endpoint、route、operationId、DTO、权限和生成类型以这些生产者为准：

1. 后端 endpoint/contract 与相应测试；
2. 两个受控 Gateway OpenAPI snapshots；
3. `frontend/packages/api-client/src/generated/**` 机械生成结果；
4. facade 暴露状态则额外以 `facade-coverage-matrix.json` 为机器事实。

迁移前 `docs/reports/audits/api-contract-and-codegen.md` 中的大型 endpoint/operationId 表只用于历史调查，**不是当前清单**。不得从该 audit 复制、恢复或校正当前 endpoint；发现差异时必须回到上述生产者确认。

## 受控兼容例外

### FileStorage `scanStatus` v1 删除（#1604）

2026-08-17 已批准一个窄范围版本例外：在 FileStorage v1 尚未形成受支持客户发布基线、且 `scanStatus` 没有受支持外部消费方的前提下，#1604 允许从当前 `/v1` DTO、PlatformGateway v1 OpenAPI snapshot 和同批生成客户端直接删除病毒扫描字段，而不为该废弃字段单独建立 v2 路由。

该例外要求后端与重新生成的客户端同批升级，并由数据库迁移先对历史非 `clean` 文件 fail closed 后再删除扫描列。它**只适用于 #1604 / `scanStatus`**；FileStorage 形成受支持客户发布后，字段删除仍遵循 Governance 的一般破坏性变更/主版本规则。

### Gateway download-grant 两跳路由 v1 删除（#3314）

2026-09-20 已批准一个窄范围版本例外：在 PlatformGateway / BusinessGateway v1 尚未形成受支持客户发布基线、且被删的四条路由没有受支持外部消费方的前提下，#3314 允许从当前 v1 OpenAPI snapshot 与同批生成客户端直接删除下列路由，而不为它们建立 v2 或保留过渡期：

| 删除的路由 | 原 operationId | 原权限口径 |
| --- | --- | --- |
| `POST /api/business-console/v1/files/{fileId}/download-grants` | `createBusinessConsoleSopFileDownloadGrant` | `business.engineering.documents.read` |
| `GET /api/business-console/v1/files/download-grants/{downloadGrantId}/content` | `downloadBusinessConsoleSopFileContent` | `business.engineering.documents.read` |
| `POST /api/console/v1/files/{fileId}/download-grants` | `createConsoleFileDownloadGrant` | `files.download-grants.create` |
| `GET /api/console/v1/files/download-grants/{downloadGrantId}/content` | `downloadConsoleFileGrantContent` | `files.read` |

**为什么不能走一般的主版本/迁移窗口程序**：这四条路由本身就是缺陷载体。它们把 FileStorage 的 download grant id 交给调用方，而该 id 是全服务共用命名空间、其兑换面既不校验用途也不记录签发门面——#3314 实测两条权限口径不同的路由可以互相兑换对方签发的 grant，三层没有一层拒绝。保留过渡期等于按窗口长度延长一个已确证的越权面。

替代面按 ADR 0030 决策 3 提供，形状不同（以 `fileId` 为入参的单跳字节路由），因此不是重命名而是契约变更：

- `GET /api/business-console/v1/files/sop-documents/{fileId}/content`（operationId 沿用 `downloadBusinessConsoleSopFileContent`）
- `GET /api/console/v1/files/{fileId}/content`（`downloadConsoleFileContent`，要求 `files.download-grants.create` + `files.read` 两个码，与被删两跳合计所需一致、不收窄）

该例外要求后端与重新生成的客户端同批升级（本仓前端调用方已在同一 PR 内迁移，`verify-openapi-client-drift.ps1` 为零漂移的机器判据）。它**只适用于 #3314 与上表四条路由**；两个 Gateway 形成受支持客户发布后，路由删除仍遵循 Governance 的一般破坏性变更/主版本规则。

### MES 列表 `status` 枚举收窄到聚合真实值域（#3912）

2026-09-28 已批准一个窄范围版本例外：#3912 允许把 BusinessGateway v1 OpenAPI 中
**MES 列表**的状态枚举，从「10 个 schema 与 11 条列表路径共用的同一份 29 值小写并集」
收窄为各聚合的真实值域，而不为被移除的码值建立 v2 路由或保留过渡期。

**收窄前**：每个 MES 列表的 `status`（行属性与 `status` 查询参数）都是同一份
`accepted/active/blocked/cancelled/closed/completed/...` 共 29 值的并集。

**收窄后**，按路径背后聚合的真实值域：

| 路径 / schema 组 | 聚合 | 值域 |
| --- | --- | --- |
| `/mes/work-orders`、`/mes/production-plans`、`BusinessConsoleMesWorkOrderItem` | `WorkOrder` | 10（小写） |
| `/mes/operation-tasks`、`/mes/dispatch-tasks`、`/mes/wip`，4 个 schema（`OperationTaskItem` / `DispatchTaskRow` / `OperationTaskRow` / `WipSummaryRow`） | `OperationTask` | 6（PascalCase） |
| `/mes/material-issue-requests`、`BusinessConsoleMesMaterialIssueRequestRow` | `MaterialIssueRequest` | 7（PascalCase） |
| `/mes/finished-goods-receipt-requests`、`BusinessConsoleMesReceiptRequestRow.receiptStatus` | `FinishedGoodsReceiptRequest` | 5（PascalCase） |
| `/mes/related-quality-items`、`BusinessConsoleMesRelatedQualityItemRow` | `DefectRecord` | 5（PascalCase） |
| `/mes/shift-handovers` | `ShiftHandover` | 2（PascalCase） |
| `/mes/downtime-events`、`/mes/capacity-impacts`、2 个停机/产能 schema | `WorkCenterUnavailability`（读面派生） | 2（PascalCase） |

**为什么不能走一般的主版本/迁移窗口程序**：那份 29 值并集本身就是缺陷载体，不是既有能力。
它把 6 个互不相干的聚合的状态混成一个集合，并且**用小写拼写冒充 PascalCase 域的运行时值** ——
工序、领料单、完工入库单、不良记录、交接班、停机这 6 个聚合在服务端实际会产生的值，
**一个都不在**该枚举内。保留过渡期等于让 29 个并集码继续作为合法过滤值被接受，其中
27 个在任何聚合上都没有对应数据；真正的运行时值反而被契约判为非法。契约此时不是在
描述能力，而是在描述漂移。

特别地，`/mes/downtime-events` 与 `/mes/capacity-impacts` 从 29 值收到 2 值（`Open` /
`Recovered`）的准确性质是**两件事叠加**：

- **取消 27 个本就不生效的码**。读面 `MesProductionQueries` / `MesWorkbenchQueries` 的过滤是
  `request.Status.Trim().ToLowerInvariant()` 之后 switch，只认 `open` / `recovered`，
  其余一律 `Where(_ => false)`。被收窄掉的那 27 个码本就在这条 switch 的 `_` 分支里，
  传进去只会得到空结果集 —— 取消它们不损失任何能返回行的过滤能力。
- **对仍生效的两个码改拼写**。`open` / `recovered` 在收窄前本来就在 29 值枚举内且**真能过滤到行**，
  收窄后契约写 PascalCase。这不是「取消」而是「改拼写」，但因为读面比较前先 lower，
  `Open` 与 `open` 行为等价（都归一化成 `open`），故运行时无差异。

所以结论是「无可观测功能损失」，但理由不是「取消的码本就不生效」这一句 —— 生效的那两个
是被改拼写而非被取消。

**替代面**：无。这不是新增能力或重命名，是把契约校正回生产者的事实。

该例外要求后端与重新生成的客户端同批升级（本仓前端消费方已在同一 PR 内迁移到真实值域，
`verify-openapi-client-drift.ps1` 为零漂移的机器判据；后端契约测试以 MES 域常量比对
OpenAPI 枚举，域里增删状态而契约没跟上时断言必然红）。它**只适用于 #3912 与上表所列
MES 列表路径/schema**；两个 Gateway 形成受支持客户发布后，任何状态枚举的收窄仍遵循
Governance 的一般破坏性变更/主版本规则。

### #3912 的两次范围裁定（消费端迁移登记）

本仓前端的消费端迁移分两次裁定完成，两次的范围都记在这里，使票内可审计（issue 评论是
过程记录，本节是长期事实）：

1. **值域收窄随契约同批落地**：前端各处状态字面量、筛选下拉、词表键集一律改用上表所列
   真实值域；`useMesReferenceLabels.ts` 的 `toLowerCase()` 归一化适配层**删除**而非保留 ——
   保留它会让后端哪天改发另一种拼写时的契约漂移被静默吸收。`Open` 是跨聚合重码
   （不良记录=待处理、交接=待接班、停机/产能=未恢复），共享词表只放停机语境的读法，
   其余语境由 `statusOptions` 的 `overrides` 显式覆盖。
2. **console 与 `business-core` 的入库词表本次不合并**：`console` 的
   `RECEIPT_STATUS_LABELS` / `RECEIPT_STATUS_TONES` 与
   `frontend/packages/business-core/src/labels/mesLabels.ts` 保持两份，本次仅登记为已知重复。
   合并需另票，届时一并决定键集归属。

守卫现状：后端 `Nerv.IIP.ContractBoundary.Tests/MesListStatusContractTests` 以 MES 域常量
比对导出 snapshot 的枚举（21 个用例）；前端 `useMesReferenceLabels.test.ts` 逐语境钉住
`overrides` 与词表键集。

## 历史材料边界

`docs/reports/audits/**` 保存迁移前总账、历史漂移、修复批次、调查和曾经的端点渲染，目的是可追溯，不承担当前规范或机器事实。若 audit 与 Current Architecture / Governance / Runbook / Reference 或代码生产者冲突，以当前权威来源为准，并把 audit 视为当时状态快照。