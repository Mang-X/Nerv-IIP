# 业务工业遥测 MVP 实施计划

> **面向智能体执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**构建精简版 IndustrialTelemetry（工业遥测），用于标签映射、受控遥测摄取、设备状态快照、报警事件和粗粒度时序摘要。

**架构：**IndustrialTelemetry（工业遥测）是 `backend/services/Business/IndustrialTelemetry` 下的独立 CleanDDD 服务。它通过公开 API 和业务集成契约接收来自 Connector Host（连接器宿主）或已授权外部客户端的遥测事实。它不持有 DeviceAsset（设备资产）主数据，不控制 PLC/DCS/SCADA，并且在 MVP 中不存储高频原始时序数据。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 集成事件、xUnit。

---

## 边界

1. 不得实现 PLC/DCS 控制命令。
2. 不得实现 SCADA 画面构建。
3. 不得存储 PLC/DCS 凭据或原始控制载荷。
4. 不得持有 `DeviceAsset`；通过来自 MasterData（主数据服务）的稳定 ID/编码引用设备资产。
5. 不得直接创建 Maintenance（维护）工单；发布报警事件供维护服务消费。

## 文件结构图

```text
backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/
  IndustrialTelemetryFacts.cs
  AggregatesModel/TelemetryTagAggregate/TelemetryTag.cs
  AggregatesModel/DeviceStateSnapshotAggregate/DeviceStateSnapshot.cs
  AggregatesModel/AlarmEventAggregate/AlarmEvent.cs
  AggregatesModel/TelemetrySummaryAggregate/TelemetrySummary.cs
  DomainEvents/IndustrialTelemetryDomainEvents.cs

backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/
  ApplicationDbContext.cs
  EntityConfigurations/*.cs
  Repositories/*.cs
  Migrations/*

backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/
  Application/Auth/IndustrialTelemetryPermissionCodes.cs
  Application/Commands/CreateTelemetryTagCommand.cs
  Application/Commands/RecordTelemetrySampleCommand.cs
  Application/Commands/RaiseAlarmCommand.cs
  Application/Commands/ClearAlarmCommand.cs
  Application/Queries/ListTelemetryTagsQuery.cs
  Application/Queries/QueryDeviceStateTimelineQuery.cs
  Application/Queries/ListAlarmEventsQuery.cs
  Application/IntegrationEvents/IndustrialTelemetryIntegrationEvents.cs
  Endpoints/Iiot/IiotEndpoints.cs

backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/
  IndustrialTelemetryAggregateTests.cs

backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/
  IndustrialTelemetryEndpointTests.cs
  IndustrialTelemetryIntegrationEventTests.cs
  IndustrialTelemetrySchemaConventionTests.cs
```

## 任务 1：搭建工业遥测（IndustrialTelemetry）服务脚手架

**文件：**

- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/Nerv.IIP.Business.IndustrialTelemetry.Domain.csproj`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.csproj`
- 创建：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj`
- 创建：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建服务和测试项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.IndustrialTelemetry -o backend/services/Business/IndustrialTelemetry --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests -o backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.IndustrialTelemetry.Web.Tests -o backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/Nerv.IIP.Business.IndustrialTelemetry.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj
```

预期结果：项目已添加，且本计划未创建 Maintenance（维护）项目。

- [ ] **步骤 2：提交脚手架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/IndustrialTelemetry
git commit -m "feat: scaffold industrial telemetry service"
```

## 任务 2：实现遥测领域事实

**文件：**

- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/IndustrialTelemetryFacts.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/TelemetryTagAggregate/TelemetryTag.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/DeviceStateSnapshotAggregate/DeviceStateSnapshot.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/AlarmEventAggregate/AlarmEvent.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/TelemetrySummaryAggregate/TelemetrySummary.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/IndustrialTelemetryAggregateTests.cs`

- [ ] **步骤 1：编写失败的遥测测试**

创建涵盖以下内容的测试：

```csharp
var tag = TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-01", "spindle.speed", "number", "rpm", "sample-10s");
var state = DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-01", "running", DateTimeOffset.UtcNow, "connector-seq-001");
var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", DateTimeOffset.UtcNow, "alarm-ext-001");
alarm.Clear(DateTimeOffset.UtcNow.AddMinutes(10), "operator-001");
```

断言标签键在每台设备内唯一、源序列对每条标签/状态流具有幂等性、报警外部 ID 具有幂等性，并且没有聚合暴露控制命令载荷。

- [ ] **步骤 2：实现事件**

创建 `TelemetryTagCreatedDomainEvent`、`TelemetrySampleRecordedDomainEvent`、`DeviceStateChangedDomainEvent`、`AlarmRaisedDomainEvent` 和 `AlarmClearedDomainEvent`。

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj --no-restore
git add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests
git commit -m "feat: add industrial telemetry facts"
```

预期结果：测试在提交前通过。

## 任务 3：添加持久化和事件

**文件：**

- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/TelemetryTagEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/DeviceStateSnapshotEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/AlarmEventEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/TelemetrySummaryEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/IntegrationEvents/IndustrialTelemetryIntegrationEvents.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryIntegrationEventTests.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetrySchemaConventionTests.cs`
- 修改：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：配置 schema**

使用 schema `industrial_telemetry`。数据表为 `telemetry_tags`、`device_state_snapshots`、`alarm_events`、`telemetry_summaries`。为 `deviceAssetId + tagKey`、`deviceAssetId + sourceSequence` 和 `externalAlarmId` 添加唯一索引。

- [ ] **步骤 2：定义集成事件**

创建：

```csharp
public sealed record DeviceStateChangedIntegrationEvent(string DeviceAssetId, string PreviousState, string CurrentState, DateTimeOffset OccurredAtUtc);
public sealed record AlarmRaisedIntegrationEvent(string AlarmId, string DeviceAssetId, string AlarmCode, string Severity, DateTimeOffset OccurredAtUtc);
public sealed record AlarmClearedIntegrationEvent(string AlarmId, string DeviceAssetId, DateTimeOffset ClearedAtUtc);
```

- [ ] **步骤 3：运行 schema 和事件测试**

运行：

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~IndustrialTelemetrySchemaConventionTests|FullyQualifiedName~IndustrialTelemetryIntegrationEventTests"
```

预期结果：通过。

- [ ] **步骤 4：提交持久化实现**

运行：

```powershell
git add backend/services/Business/IndustrialTelemetry docs/architecture/database-schema-catalog.md
git commit -m "feat: persist industrial telemetry facts"
```

## 任务 4：添加遥测 API 和权限

**文件：**

- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Auth/IndustrialTelemetryPermissionCodes.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/CreateTelemetryTagCommand.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/RecordTelemetrySampleCommand.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/RaiseAlarmCommand.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/ClearAlarmCommand.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/ListTelemetryTagsQuery.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/QueryDeviceStateTimelineQuery.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/ListAlarmEventsQuery.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Endpoints/Iiot/IiotEndpoints.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointTests.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`

- [ ] **步骤 1：添加路由**

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/iiot/tags` | `business.iiot.tags.manage` |
| `GET /api/business/v1/iiot/tags` | `business.iiot.telemetry.read` |
| `POST /api/business/v1/iiot/samples` | `business.iiot.telemetry.write` |
| `POST /api/business/v1/iiot/alarms` | `business.iiot.alarms.write` |
| `GET /api/business/v1/iiot/alarms` | `business.iiot.alarms.read` |
| `GET /api/business/v1/iiot/devices/{deviceAssetId}/timeline` | `business.iiot.telemetry.read` |

- [ ] **步骤 2：写入初始权限数据**

写入 `business.iiot.tags.manage`、`business.iiot.telemetry.read`、`business.iiot.telemetry.write`、`business.iiot.alarms.read`、`business.iiot.alarms.write` 的初始数据。Connector Host（连接器宿主）和外部客户端的写入测试必须包含组织/环境和能力范围。

- [ ] **步骤 3：运行测试并提交**

运行：

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
git add backend/services/Business/IndustrialTelemetry backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs
git commit -m "feat: expose industrial telemetry api"
```

预期结果：测试在提交前通过。

## 任务 5：添加验证与就绪状态记录

**文件：**

- 创建：`scripts/verify-business-industrial-telemetry-mvp.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：运行验证**

运行：

```powershell
scripts/verify-business-industrial-telemetry-mvp.ps1
git diff --check
```

预期结果：脚本运行工业遥测领域测试和 Web 测试，并以 `0` 退出。

- [ ] **步骤 2：提交文档**

运行：

```powershell
git add scripts/verify-business-industrial-telemetry-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record industrial telemetry readiness"
```

## 自审清单

1. IndustrialTelemetry（工业遥测）与 Maintenance（维护）分开跟踪。
2. Connector Host（连接器宿主）写入由遥测写入权限或报警写入权限授权。
3. 未建模任何 PLC/DCS/SCADA 控制命令。
4. 报警事件和设备状态事件可供 Maintenance（维护）、MES、Planning（计划）和 Notification（通知）消费者使用。
