# 授权目录与 Producer 索引

本页提供当前 IAM seed 权限、默认角色和已成文 Gateway 权限映射的**人工查询面**。它不是 IAM 的第二份权限 registry：任何权限是否存在、是否 seed、某个 endpoint 是否真正强制，最终必须回到代码 producer 和测试核实。

授权语义与 scope 规则见 [`../../governance/security/authorization.md`](../../governance/security/authorization.md)。

## 权威 Producer

| 事实 | Producer |
| --- | --- |
| 平台权限常量与 `NervIipSeedPermissions.All` | `backend/services/Iam/src/Nerv.IIP.Iam.Domain/IamFacts.cs` |
| IAM 权限目录/管理读面 | `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Permissions/IamPermissionCatalog.cs` |
| 默认角色与 seed 行为 | `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs` |
| Console IAM facade 的 operation 与授权 | `backend/gateway/PlatformGateway` 对应 IAM endpoints / clients |
| Console 日志 facade 的授权 | `backend/gateway/PlatformGateway` 对应 Observability/log endpoints |
| Business facade 的最终用户权限 | `backend/gateway/BusinessGateway` 对应 endpoint metadata / authorization code |
| 服务自身权限元数据与行为 | 各服务 endpoint、authorization guard 与测试 |

本页与任一 producer 冲突时，以 producer 为准并修正文档。

## 当前 IAM seed 权限清单

下列 code **只抄录当前 `NervIipSeedPermissions.All` 已存在项**，用于人工检索和 Review；不包含仅冻结命名空间、尚未进入 producer 的未来候选。新增、删除或改名时必须先改 IAM producer 与行为测试，再同步本节。

### IAM / Platform

- `iam.users.read`
- `iam.users.manage`
- `iam.roles.read`
- `iam.roles.manage`
- `iam.sessions.read`
- `iam.sessions.revoke`
- `iam.security-audit.read`
- `connectors.registrations.write`
- `connectors.heartbeats.write`
- `connectors.state-snapshots.write`
- `apphub.instances.read`
- `files.upload`
- `files.read`
- `files.download-grants.create`
- `files.archive`
- `ops.tasks.create`
- `ops.tasks.read`
- `ops.results.write`
- `ops.audit.read`
- `observability.logs.read`

### MasterData / Quality / Inventory

- `business.masterdata.products.read`
- `business.masterdata.products.manage`
- `business.masterdata.partners.read`
- `business.masterdata.partners.manage`
- `business.masterdata.resources.read`
- `business.masterdata.resources.manage`
- `business.quality.inspection-plans.manage`
- `business.quality.inspection-records.create`
- `business.quality.inspection-records.read`
- `business.quality.ncr.read`
- `business.quality.ncr.manage`
- `business.inventory.locations.manage`
- `business.inventory.movements.create`
- `business.inventory.ledger.read`
- `business.inventory.counts.manage`
- `business.inventory.expired-stock.override`

### MES

- `business.mes.foundation.read`
- `business.mes.overview.read`
- `business.mes.plans.read`
- `business.mes.work-orders.read`
- `business.mes.work-orders.manage`
- `business.mes.materials.read`
- `business.mes.materials.manage`
- `business.mes.dispatch.read`
- `business.mes.dispatch.manage`
- `business.mes.operations.read`
- `business.mes.operations.manage`
- `business.mes.reporting.read`
- `business.mes.reporting.write`
- `business.mes.quality.read`
- `business.mes.quality.write`
- `business.mes.receipts.read`
- `business.mes.receipts.manage`
- `business.mes.downtime.read`
- `business.mes.downtime.manage`
- `business.mes.handovers.read`
- `business.mes.handovers.manage`
- `business.mes.traceability.read`
- `business.mes.schedules.read`
- `business.mes.schedules.manage`
- `business.mes.capacity.read`

### ProductEngineering / Planning

- `business.engineering.documents.read`
- `business.engineering.documents.manage`
- `business.engineering.items.read`
- `business.engineering.items.manage`
- `business.engineering.boms.read`
- `business.engineering.boms.manage`
- `business.engineering.routings.read`
- `business.engineering.routings.manage`
- `business.engineering.standard-operations.read`
- `business.engineering.standard-operations.manage`
- `business.engineering.production-versions.read`
- `business.engineering.production-versions.manage`
- `business.engineering.changes.read`
- `business.engineering.changes.manage`
- `business.planning.demands.read`
- `business.planning.demands.manage`
- `business.planning.mps.read`
- `business.planning.mps.manage`
- `business.planning.mps.release`
- `business.planning.mrp.read`
- `business.planning.mrp.run`
- `business.planning.suggestions.manage`

### Barcode / Approval / ERP / Scheduling

- `business.barcodes.templates.manage`
- `business.barcodes.print`
- `business.barcodes.scans.write`
- `business.approvals.read`
- `business.approvals.manage`
- `business.erp.procurement.read`
- `business.erp.procurement.manage`
- `business.erp.sales.read`
- `business.erp.sales.manage`
- `business.erp.finance.read`
- `business.erp.finance.manage`
- `business.scheduling.plans.read`
- `business.scheduling.plans.manage`
- `business.scheduling.plans.release`

### WMS

- `business.wms.receipts.read`
- `business.wms.receipts.manage`
- `business.wms.shipments.read`
- `business.wms.shipments.manage`
- `business.wms.counts.read`
- `business.wms.automation.manage`
- `business.wms.work-pools.manage`

### IndustrialTelemetry / Maintenance

- `business.iiot.tags.manage`
- `business.iiot.alarm-rules.manage`
- `business.iiot.telemetry.read`
- `business.iiot.telemetry.write`
- `business.iiot.device-control.write`
- `business.iiot.device-control.manage`
- `business.iiot.device-control.read`
- `business.iiot.alarms.read`
- `business.iiot.alarms.write`
- `business.maintenance.work-orders.read`
- `business.maintenance.work-orders.manage`
- `business.maintenance.plans.read`
- `business.maintenance.plans.manage`
- `business.maintenance.downtime-reasons.read`

### Notification

- `notifications.intents.submit`
- `notifications.dlq.read`
- `notifications.dlq.manage`
- `notifications.messages.read`
- `notifications.messages.mark-read`
- `notifications.tasks.read`
- `notifications.delivery.manage`

> 注意：本清单证明的是“当前 IAM seed producer 登记了该 code”，**不证明**某个默认角色拥有它，也不证明某个 endpoint 已强制它。这两件事必须分别回到 seed 与 endpoint/authorization tests 核实。

## 默认 ERP 岗位角色

IAM 仅在对应角色缺失时创建默认角色；重复 seed 不应覆盖同 ID 角色已经被运营调整的名称、权限或 data scope。

| 角色 ID | 角色名称 | 默认权限 | 默认 scope |
| --- | --- | --- | --- |
| `role-erp-procurement` | ERP 采购专员 | `business.masterdata.products.read`、`business.masterdata.resources.read`、`business.erp.procurement.read`、`business.erp.procurement.manage` | Organization |
| `role-erp-sales` | ERP 销售专员 | `business.masterdata.products.read`、`business.masterdata.resources.read`、`business.erp.sales.read`、`business.erp.sales.manage` | Organization |
| `role-erp-finance` | ERP 财务专员 | `business.masterdata.resources.read`、`business.erp.procurement.read`、`business.erp.sales.read`、`business.erp.finance.read`、`business.erp.finance.manage` | Organization |

角色是否仍由当前 seed 创建以及实际默认集合，以 `IamFacts.cs` / `IamSeedService.cs` 为准。

## Console IAM facade 映射

下表用于导航当前 Console IAM 操作；Gateway 代码仍是 operationId 与 permission 的最终事实源。

| 控制台门面路由 | operationId | 权限码 |
| --- | --- | --- |
| `GET /api/console/v1/iam/users` | `listConsoleIamUsers` | `iam.users.read` |
| `POST /api/console/v1/iam/users` | `createConsoleIamUser` | `iam.users.manage` |
| `PATCH /api/console/v1/iam/users/{userId}` | `updateConsoleIamUser` | `iam.users.manage` |
| `POST /api/console/v1/iam/users/{userId}/disable` | `disableConsoleIamUser` | `iam.users.manage` |
| `POST /api/console/v1/iam/users/{userId}/reset-password` | `resetConsoleIamUserPassword` | `iam.users.manage` |
| `GET /api/console/v1/iam/roles` | `listConsoleIamRoles` | `iam.roles.read` |
| `POST /api/console/v1/iam/roles` | `createConsoleIamRole` | `iam.roles.manage` |
| `PATCH /api/console/v1/iam/roles/{roleId}/permissions` | `updateConsoleIamRolePermissions` | `iam.roles.manage` |
| `GET /api/console/v1/iam/permissions` | `listConsoleIamPermissions` | `iam.roles.read` |
| `GET /api/console/v1/iam/sessions` | `listConsoleIamSessions` | `iam.sessions.read` |
| `POST /api/console/v1/iam/sessions/{sessionId}/revoke` | `revokeConsoleIamSession` | `iam.sessions.revoke` |

## Console Observability facade 映射

| 控制台门面路由 | operationId | 权限码 |
| --- | --- | --- |
| `POST /api/console/v1/logs/query` | `queryConsoleLogs` | `observability.logs.read` |

BusinessGateway 的大量业务 operation 不在本页复制第二张 facade registry；按目标 operation 从 Gateway code、OpenAPI/generated client 与现有 facade/现场 Reference 查询。

## 使用纪律

1. 新增/删除权限或变更映射时，先改真实 producer 与测试，再同步本页。
2. 不以“本页有这一行”证明 endpoint 已强制授权；必须检查 endpoint metadata/guard 与相应测试。
3. 不以“endpoint 要求这一权限”证明默认角色拥有它；默认角色事实单独回到 seed。
4. 不把 Governance 中仅冻结的未来 namespace 候选加入“当前 IAM seed 权限清单”，除非它已经进入 `NervIipSeedPermissions.All`。
5. 历史实现状态、票号和阶段完成说明不进入本页。