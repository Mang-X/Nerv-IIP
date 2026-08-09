# 业务维护 MVP 实施计划

> **面向智能体执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**构建精简版 Maintenance（维护服务），用于维护工单、维护计划、巡检、停机原因和资产可用性事件。

**架构：**Maintenance（维护服务）是 `backend/services/Business/Maintenance` 下独立的精简版计算机化维护管理系统（CMMS）CleanDDD 服务。它消费 IndustrialTelemetry（工业遥测）报警事件并引用 MasterData（主数据）设备资产，但不持有设备主数据、遥测样本、生产工单或备件库存余额。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 集成事件、xUnit。

---

## 边界

1. 不得创建或变更 `DeviceAsset`；引用 MasterData（主数据）设备 ID/编码。
2. 不得存储遥测样本或报警原始载荷；消费报警事件 ID。
3. 不得持有备件库存余额；请求或引用 Inventory（库存）变动记录。
4. 不得直接更改 MES 排程；为 MES 和 Planning（计划）发布资产可用性事件。
5. 不得实现完整的 EAM 折旧或资产会计。

## 文件结构图

```text
backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/
  MaintenanceFacts.cs
  AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs
  AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs
  AggregatesModel/MaintenanceInspectionAggregate/MaintenanceInspection.cs
  AggregatesModel/DowntimeReasonAggregate/DowntimeReason.cs
  DomainEvents/MaintenanceDomainEvents.cs

backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/
  ApplicationDbContext.cs
  EntityConfigurations/*.cs
  Repositories/*.cs
  Migrations/*

backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/
  Application/Auth/MaintenancePermissionCodes.cs
  Application/Commands/CreateMaintenanceWorkOrderCommand.cs
  Application/Commands/CompleteMaintenanceWorkOrderCommand.cs
  Application/Commands/CreateMaintenancePlanCommand.cs
  Application/Commands/RecordMaintenanceInspectionCommand.cs
  Application/Queries/ListMaintenanceWorkOrdersQuery.cs
  Application/Queries/ListMaintenancePlansQuery.cs
  Application/IntegrationEvents/MaintenanceIntegrationEvents.cs
  Application/IntegrationEventHandlers/OpenWorkOrderWhenAlarmRaisedHandler.cs
  Endpoints/Maintenance/MaintenanceEndpoints.cs

backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/
  MaintenanceAggregateTests.cs

backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/
  MaintenanceEndpointTests.cs
  MaintenanceIntegrationEventHandlerTests.cs
  MaintenanceSchemaConventionTests.cs
```

## 任务 1：搭建维护（Maintenance）服务脚手架

**文件：**

- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/Nerv.IIP.Business.Maintenance.Domain.csproj`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Nerv.IIP.Business.Maintenance.Infrastructure.csproj`
- 创建：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj`
- 创建：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建服务和测试项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Maintenance -o backend/services/Business/Maintenance --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Maintenance.Domain.Tests -o backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Maintenance.Web.Tests -o backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/Nerv.IIP.Business.Maintenance.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Nerv.IIP.Business.Maintenance.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj
```

预期结果：项目已添加，且本计划未创建 IndustrialTelemetry（工业遥测）项目。

- [ ] **步骤 2：提交脚手架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Maintenance
git commit -m "feat: scaffold maintenance service"
```

## 任务 2：实现维护领域事实

**文件：**

- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/MaintenanceFacts.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceInspectionAggregate/MaintenanceInspection.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/DowntimeReasonAggregate/DowntimeReason.cs`
- 创建：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/MaintenanceAggregateTests.cs`

- [ ] **步骤 1：编写失败的维护测试**

创建涵盖以下内容的测试：

```csharp
var workOrder = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, "over temperature");
workOrder.Complete("replaced sensor", 45, new[] { SparePartLine.Create("SKU-SP-001", 1m) });

var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "weekly-inspection", "P7D", DateOnly.FromDateTime(DateTime.UtcNow));
var inspection = MaintenanceInspection.Record("org-001", "env-dev", plan.Id.Value, "operator-001", "passed", DateTimeOffset.UtcNow);
```

断言完成操作要求提供结果和停机归因、计划间隔明确、备件数量为正，并且巡检引用一项计划或工单。

- [ ] **步骤 2：实现事件**

创建 `MaintenanceWorkOrderOpenedDomainEvent`、`MaintenanceWorkOrderCompletedDomainEvent`、`AssetUnavailableDomainEvent`、`AssetRestoredDomainEvent`、`MaintenancePlanCreatedDomainEvent` 和 `MaintenanceInspectionRecordedDomainEvent`。

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj --no-restore
git add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests
git commit -m "feat: add maintenance cmms lite facts"
```

预期结果：测试在提交前通过。

## 任务 3：添加持久化、报警消费者和事件

**文件：**

- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceWorkOrderEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenancePlanEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceInspectionEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/DowntimeReasonEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEvents/MaintenanceIntegrationEvents.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventHandlers/OpenWorkOrderWhenAlarmRaisedHandler.cs`
- 创建：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventHandlerTests.cs`
- 创建：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSchemaConventionTests.cs`
- 修改：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：配置 schema**

使用 schema `maintenance`。数据表为 `maintenance_work_orders`、`maintenance_plans`、`maintenance_inspections`、`downtime_reasons`。

- [ ] **步骤 2：实现报警消费者**

`OpenWorkOrderWhenAlarmRaisedHandler` 消费 `industrialTelemetry.AlarmRaised`，并为每个 `sourceAlarmId` 创建一张维护工单。重复投递返回现有工单 ID，且不创建第二张工单。

- [ ] **步骤 3：定义集成事件**

创建：

```csharp
public sealed record MaintenanceWorkOrderOpenedIntegrationEvent(string WorkOrderId, string DeviceAssetId, string? SourceAlarmId, string Priority);
public sealed record MaintenanceWorkOrderCompletedIntegrationEvent(string WorkOrderId, string DeviceAssetId, int DowntimeMinutes);
public sealed record AssetUnavailableIntegrationEvent(string DeviceAssetId, string Reason, DateTimeOffset FromUtc);
public sealed record AssetRestoredIntegrationEvent(string DeviceAssetId, DateTimeOffset RestoredAtUtc);
```

- [ ] **步骤 4：运行 schema 和处理器测试**

运行：

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MaintenanceSchemaConventionTests|FullyQualifiedName~MaintenanceIntegrationEventHandlerTests"
```

预期结果：通过。

- [ ] **步骤 5：提交持久化实现**

运行：

```powershell
git add backend/services/Business/Maintenance docs/architecture/database-schema-catalog.md
git commit -m "feat: persist maintenance facts"
```

## 任务 4：添加维护 API 和权限

**文件：**

- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Auth/MaintenancePermissionCodes.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/CreateMaintenanceWorkOrderCommand.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/CompleteMaintenanceWorkOrderCommand.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/CreateMaintenancePlanCommand.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/RecordMaintenanceInspectionCommand.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/ListMaintenanceWorkOrdersQuery.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/ListMaintenancePlansQuery.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- 创建：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointTests.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`

- [ ] **步骤 1：添加路由**

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/maintenance/work-orders` | `business.maintenance.work-orders.manage` |
| `POST /api/business/v1/maintenance/work-orders/{workOrderId}/complete` | `business.maintenance.work-orders.manage` |
| `GET /api/business/v1/maintenance/work-orders` | `business.maintenance.work-orders.read` |
| `POST /api/business/v1/maintenance/plans` | `business.maintenance.plans.manage` |
| `GET /api/business/v1/maintenance/plans` | `business.maintenance.plans.read` |
| `POST /api/business/v1/maintenance/inspections` | `business.maintenance.plans.manage` |

- [ ] **步骤 2：写入初始权限数据**

写入 `business.maintenance.work-orders.read`、`business.maintenance.work-orders.manage`、`business.maintenance.plans.read`、`business.maintenance.plans.manage` 的初始数据。

- [ ] **步骤 3：运行测试并提交**

运行：

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
git add backend/services/Business/Maintenance backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs
git commit -m "feat: expose maintenance api"
```

预期结果：测试在提交前通过。

## 任务 5：添加验证与就绪状态记录

**文件：**

- 创建：`scripts/verify-business-maintenance-mvp.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：运行验证**

运行：

```powershell
scripts/verify-business-maintenance-mvp.ps1
git diff --check
```

预期结果：脚本运行维护领域测试和 Web 测试，并以 `0` 退出。

- [ ] **步骤 2：提交文档**

运行：

```powershell
git add scripts/verify-business-maintenance-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record maintenance readiness"
```

## 自审清单

1. Maintenance（维护）与 IndustrialTelemetry（工业遥测）分开跟踪。
2. 报警到工单的流程具有幂等性。
3. 资产不可用/已恢复事件可供 MES、Planning（计划）和 Notification（通知）消费者使用。
4. Maintenance（维护）不存储遥测样本、DeviceAsset（设备资产）主数据或备件库存余额。
