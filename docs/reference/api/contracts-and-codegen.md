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

## 历史材料边界

`docs/reports/audits/**` 保存迁移前总账、历史漂移、修复批次、调查和曾经的端点渲染，目的是可追溯，不承担当前规范或机器事实。若 audit 与 Current Architecture / Governance / Runbook / Reference 或代码生产者冲突，以当前权威来源为准，并把 audit 视为当时状态快照。