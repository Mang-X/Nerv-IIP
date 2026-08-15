# 设备可靠性缺口 416 实施计划

> **供代理执行者使用：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**解决 GitHub issue #416 中 IndustrialTelemetry 和 Maintenance 的可靠性缺口：预防性维护到期工单、备件库存移动请求、与 SEMI E10 对齐的 OEE 运行状态映射、告警清除后的工单恢复标记，以及 MTBF/MTTR 查询覆盖。

**架构：**Maintenance 仍然拥有维护计划、工单、检查、备件需求和可靠性指标。IndustrialTelemetry 仍然拥有设备状态、告警和 OEE 输入事实。跨服务协作只使用公开契约：Maintenance 消费 `Nerv.IIP.Contracts.IndustrialTelemetry.AlarmClearedIntegrationEvent`，并根据 Maintenance 领域事件发布 `Nerv.IIP.Contracts.Inventory.InventoryMovementRequestedIntegrationEvent`；它不引用 Inventory 服务项目。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、EF Core、netcorepal 领域/集成事件、ADR 0011 信封、xUnit。

---

## 文件映射

- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/DomainEvents/MaintenanceDomainEvents.cs`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceEntityTypeConfigurations.cs`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Program.cs`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceCommands.cs`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/MaintenanceQueries.cs`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventConverters/MaintenanceIntegrationEventConverters.cs`
- 创建： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventHandlers/MarkWorkOrderAlarmClearedHandler.cs`
- 修改： `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- 修改： `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/MaintenanceAggregateTests.cs`
- 修改： `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointContractTests.cs`
- 修改： `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventHandlerTests.cs`
- 修改： `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventTests.cs`
- 修改： `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSchemaConventionTests.cs`
- 修改： `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/IndustrialTelemetryQueries.cs`
- 修改： `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointContractTests.cs`
- 修改： `docs/architecture/equipment-status-event-flow.md`
- 修改： `docs/architecture/implementation-readiness.md`

## Task 1：可靠性缺口的 TDD 测试

- [ ] **步骤 1：添加 Maintenance 聚合测试**

添加测试以证明：

```csharp
var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-001", "P7D", new DateOnly(2026, 6, 1), "maintenance");
Assert.True(plan.IsDueOn(new DateOnly(2026, 6, 8)));

var order = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
order.MarkAlarmCleared(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
Assert.True(order.AlarmCleared);
```

运行：

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj --no-restore
```

预期为红灯：缺少到期生成和告警清除成员。

- [ ] **步骤 2：添加 Maintenance Web 测试**

添加测试以证明：

```csharp
await new GenerateDueMaintenanceWorkOrdersCommandHandler(dbContext).Handle(
    new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"),
    CancellationToken.None);

var reliability = await new QueryAssetReliabilityQueryHandler(dbContext).Handle(
    new QueryAssetReliabilityQuery("org-001", "env-dev", "DEV-CNC-01", windowStart, windowEnd),
    CancellationToken.None);
```

还要断言 `AlarmClearedIntegrationEvent` 会标记匹配的未结工单，并且 `MaintenanceSparePartIssuedDomainEvent` 会转换为 `InventoryMovementRequestedIntegrationEvent`，其中出库数量为负数，幂等键为 `maintenance:{org}:{env}:{workOrderId}:{sparePartLineId}`。

运行：

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
```

预期为红灯：命令/查询/处理器/转换器尚不存在。

- [ ] **步骤 3：添加 IndustrialTelemetry OEE 测试**

添加测试，验证在 `running` 与 `standby` 之间分割的时间窗口仅根据生产性运行时间报告可用率，而运行时可用性仍可将 `standby` 归类为可用：

```csharp
Assert.Equal(0.5m, response.AvailabilityRate);
```

运行：

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter FullyQualifiedName~Oee
```

预期为红灯：当前 OEE 将 `standby` 计入运行时间。

## Task 2：实现 Maintenance 领域与命令

- [ ] **步骤 1：扩展 `MaintenancePlan`**

添加可空的生成状态：

```csharp
public DateOnly? LastGeneratedOn { get; private set; }
public DateOnly NextDueOn { get; private set; }
public bool IsDueOn(DateOnly businessDate) => NextDueOn <= businessDate;
public void MarkGenerated(DateOnly generatedOn)
{
    LastGeneratedOn = generatedOn;
    NextDueOn = generatedOn.AddDays(ParseIsoDayInterval(Interval));
}
```

P0 支持 `P7D` 之类的 ISO 日间隔；无效间隔在命令校验中抛出 `KnownException`。

- [ ] **步骤 2：扩展 `MaintenanceWorkOrder`**

添加告警清除标记和备件发放事件：

```csharp
public bool AlarmCleared { get; private set; }
public DateTimeOffset? AlarmClearedAtUtc { get; private set; }
public void MarkAlarmCleared(DateTimeOffset clearedAtUtc) { ... }
```

当 `Complete` 替换备件行时，在创建各行后为每行引发一个 `MaintenanceSparePartIssuedDomainEvent`。

- [ ] **步骤 3：添加命令**

添加 `GenerateDueMaintenanceWorkOrdersCommand`，扫描某个组织/环境/日期下的到期计划，为每个到期计划创建一个未结的手工工单，标记已生成计划，并返回生成数量。使用计划代码作为来源上下文，并依赖计划状态保证幂等性。

添加 `MarkMaintenanceWorkOrderAlarmClearedCommand`，匹配 `SourceAlarmId`、组织和环境，然后调用 `MarkAlarmCleared`。

- [ ] **步骤 4：注册告警清除消费者**

添加 `MarkWorkOrderAlarmClearedHandler`，使用 `IntegrationEventConsumerGuard<AlarmClearedIntegrationEvent>` 和同一个 inbox 辅助工具，且不直接引用 IndustrialTelemetry 实现。

## Task 3：Inventory 移动请求

- [ ] **步骤 1：引用 Inventory 契约**

在 Maintenance Web 项目中添加对 `backend/common/Contracts/Nerv.IIP.Contracts.Inventory/Nerv.IIP.Contracts.Inventory.csproj` 的引用。

- [ ] **步骤 2：添加转换器**

将 `MaintenanceSparePartIssuedDomainEvent` 转换为 `InventoryMovementRequestedIntegrationEvent`：

```csharp
new InventoryMovementRequestedPayload(
    MovementType: "outbound",
    SourceService: "maintenance",
    SourceDocumentId: workOrder.Id.ToString(),
    SourceDocumentLineId: line.Id.ToString(),
    IdempotencyKey: idempotencyKey,
    SkuCode: line.SkuCode,
    UomCode: line.UomCode ?? "EA",
    SiteCode: "maintenance",
    LocationCode: "maintenance-spares",
    LotNo: null,
    SerialNo: null,
    QualityStatus: "available",
    OwnerType: "maintenance",
    OwnerId: null,
    Quantity: -Math.Abs(line.Quantity),
    RequestedAtUtc: workOrder.CompletedAtUtc ?? DateTimeOffset.UtcNow);
```

由于 Maintenance 不拥有仓库主数据，所选 `SiteCode`/`LocationCode` 是显式的 P0 默认值，并记录为可配置的后续事项。

## Task 4：查询与 Endpoint

- [ ] **步骤 1：可靠性指标查询**

添加返回以下内容的 `QueryAssetReliabilityQuery`：

```csharp
public sealed record AssetReliabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int FailureCount,
    int RepairCount,
    decimal? MtbfHours,
    decimal? MttrMinutes);
```

故障数是在该时间窗口内 `SourceAlarmId != null` 的已完成或未结工单数量。MTTR 是已完成故障工单的平均 `CompletedAtUtc - OpenedAtUtc`，没有已完成维修样本时返回 `null`。MTBF P0 使用查询窗口经过的小时数除以故障数，没有故障样本时返回 `null`，因为运行小时集成仍由后续 IndustrialTelemetry 适配器完成。

- [ ] **步骤 2：Endpoint 契约**

添加：

```text
POST /api/business/v1/maintenance/plans/generate-due
GET /api/business/v1/maintenance/assets/{deviceAssetId}/reliability
```

两者都要求 `InternalServiceAuthorizationPolicy`；生成操作使用 `business.maintenance.plans.manage`，可靠性查询使用 `business.maintenance.work-orders.read`。

## Task 5：IndustrialTelemetry OEE 映射

- [ ] **步骤 1：集中状态分类器**

运行时可用状态保持宽松（`available`、`idle`、`ready`、`running`、`standby`），但将 OEE 生产性运行时间收窄到实际状态事实值 `running`。`productive` 不是当前持久化的运行时状态值。添加注释/测试，将其与 SEMI E10 的 Productive 与 Standby 区分联系起来。

- [ ] **步骤 2：运行 IndustrialTelemetry 聚焦测试**

运行：

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter FullyQualifiedName~Oee
```

预期为绿灯。

## Task 6：文档与验证

- [ ] **步骤 1：更新文档**

更新 `equipment-status-event-flow.md` 和 `implementation-readiness.md`，加入以下内容：

1. 告警清除会将 Maintenance 工单标记为已恢复待确认；
2. PM 到期生成功能以有界命令/API 提供，在部署策略最终确定前不作为长期运行的 scheduler；
3. 备件发放使用 P0 维护库存默认值发布 Inventory 移动请求；
4. MTBF/MTTR P0 使用工单经过时间和查询窗口小时数。

- [ ] **步骤 2：运行聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter FullyQualifiedName~Oee
dotnet build backend/Nerv.IIP.sln --no-restore
git diff --check
```

预期：全部通过且没有新增 warning。

## 自审

- #416 P0 PM 生成由计划状态和命令级幂等性覆盖。
- #416 P0 备件库存移动仅使用 `Nerv.IIP.Contracts.Inventory`。
- #416 P1 OEE 映射将生产性时间与待机/空闲可用性分开。
- #416 P1 告警清除由 Maintenance 消费，且不会自动完成工单。
- #416 P1 MTBF/MTTR 由 Maintenance 公开，并记录了 P0 限制。
