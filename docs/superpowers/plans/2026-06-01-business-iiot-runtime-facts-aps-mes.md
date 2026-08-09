# 业务 IIoT 运行时事实与 APS/MES 实施计划

> **供代理执行者使用：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 子技能，逐任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**实施 #207，使 IndustrialTelemetry 与 Maintenance 运行时事实产出统一的设备可用性语言，供 BusinessScheduling、MES 就绪状态和 Business Console 设备视图使用。

**架构：**新增窄范围共享契约包 `Nerv.IIP.Contracts.EquipmentRuntime`，承载可用性 DTO 和原因码；随后由 IndustrialTelemetry 与 Maintenance 暴露查询优先的运行时可用性 API。BusinessScheduling 在纯排程前使用规范化的不可用时间窗，MES 就绪状态使用同一原因码目录，而 BusinessGateway/Business Console 只聚合并呈现这些事实。

**技术栈：**.NET 10、FastEndpoints、EF Core PostgreSQL、CleanDDD、xUnit、ADR 0011 集成事件、`Nerv.IIP.Contracts.Scheduling`、`Nerv.IIP.Testing`、BusinessGateway facade、Hey API 生成的 `@nerv-iip/api-client`、Vue 3、Vite Plus、Pinia Colada、`@nerv-iip/ui`。

---

## 规格

以 `docs/superpowers/specs/2026-06-01-business-iiot-runtime-facts-aps-mes-design.md` 作为本计划的领域契约。ADR 0014 仍是 APS/MES/IIoT 边界的架构权威。

## 文件

- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/Nerv.IIP.Contracts.EquipmentRuntime.csproj`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/EquipmentRuntimeContracts.cs`
- 创建：`backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/EquipmentRuntimeContractSerializationTests.cs`
- 修改：`backend/Nerv.IIP.sln`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/DeviceStateSnapshotAggregate/DeviceStateSnapshot.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/TelemetrySummaryAggregate/TelemetrySummary.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/DeviceStateSnapshotEntityTypeConfiguration.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/TelemetrySummaryEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Migrations/*_AddRuntimeSourceMetadata.cs`，由任务 2 中的 EF 命令生成
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/IndustrialTelemetryCommands.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/IndustrialTelemetryQueries.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Endpoints/Iiot/IndustrialTelemetryEndpoints.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointContractTests.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetrySchemaConventionTests.cs`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceEntityTypeConfigurations.cs`
- 创建：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/*_AddMaintenancePlanRuntimeWindow.cs`，由任务 3 中的 EF 命令生成
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceCommands.cs`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/MaintenanceQueries.cs`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- 修改：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointContractTests.cs`
- 修改：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSchemaConventionTests.cs`
- 修改：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/EquipmentAvailabilitySchedulingAdapter.cs`
- 修改：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/FiniteCapacitySchedulerTests.cs`
- 创建：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/EquipmentAvailabilitySchedulingAdapterTests.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesReadinessReasonCodes.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs`
- 修改：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesPersistenceContractTests.cs`
- 修改：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesEndpointContractTests.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Equipment/BusinessConsoleEquipmentEndpoints.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayTestServiceBaseUrls.cs`
- 创建：`frontend/apps/business-console/src/composables/useBusinessEquipment.ts`
- 创建：`frontend/apps/business-console/src/composables/useBusinessEquipment.test.ts`
- 创建：`frontend/apps/business-console/src/pages/equipment/index.vue`
- 创建：`frontend/apps/business-console/src/pages/equipment/alarms.vue`
- 创建：`frontend/apps/business-console/src/pages/equipment/[deviceAssetId].vue`
- 修改：`docs/architecture/api-contract-and-codegen.md`
- 修改：`docs/architecture/authorization-matrix.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/frontend-navigation-map.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 创建：`scripts/verify-business-iiot-runtime-facts-aps-mes.ps1`
- 创建：`scripts/tests/business-iiot-runtime-verify-script.Tests.ps1`

## Task 1：创建共享设备运行时契约

**文件：**
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/Nerv.IIP.Contracts.EquipmentRuntime.csproj`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/EquipmentRuntimeContracts.cs`
- 创建：`backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/EquipmentRuntimeContractSerializationTests.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建契约项目和测试项目**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.EquipmentRuntime -o backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime --framework net10.0
dotnet new xunit -n Nerv.IIP.Contracts.EquipmentRuntime.Tests -o backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/Nerv.IIP.Contracts.EquipmentRuntime.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/Nerv.IIP.Contracts.EquipmentRuntime.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj
```

预期：项目已创建并添加到 `backend/Nerv.IIP.sln`。

- [ ] **步骤 2：编写失败的序列化测试**

创建 `backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/EquipmentRuntimeContractSerializationTests.cs`：

```csharp
using System.Text.Json;
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Contracts.EquipmentRuntime.Tests;

public sealed class EquipmentRuntimeContractSerializationTests
{
    [Fact]
    public void Runtime_availability_response_round_trips_camel_case_enums_and_reason_codes()
    {
        var response = new EquipmentRuntimeAvailabilityResponse(
            ContractVersion: 1,
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            QueryWindowStartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            QueryWindowEndUtc: new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero),
            Items:
            [
                new EquipmentRuntimeAvailabilityWindowContract(
                    DeviceAssetId: "DEV-OIL-01",
                    WorkCenterId: "WC-OIL-SEAL",
                    AvailabilityStatus: EquipmentRuntimeAvailabilityStatus.Unavailable,
                    ReasonCode: EquipmentRuntimeReasonCodes.ActiveAlarm,
                    Severity: EquipmentRuntimeSeverity.Critical,
                    StartUtc: new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
                    EndUtc: new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero),
                    SourceType: EquipmentRuntimeSourceType.Alarm,
                    SourceReferenceId: "alarm-001",
                    MessageKey: "equipment.activeAlarm",
                    SubstituteDeviceAssetIds: ["DEV-OIL-02"])]);

        var json = JsonSerializer.Serialize(response, EquipmentRuntimeJson.Options);
        var roundTrip = JsonSerializer.Deserialize<EquipmentRuntimeAvailabilityResponse>(json, EquipmentRuntimeJson.Options);

        Assert.Contains("\"availabilityStatus\":\"unavailable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"sourceType\":\"alarm\"", json, StringComparison.Ordinal);
        Assert.NotNull(roundTrip);
        Assert.Equal(EquipmentRuntimeReasonCodes.ActiveAlarm, roundTrip!.Items.Single().ReasonCode);
        Assert.Equal(EquipmentRuntimeSeverity.Critical, roundTrip.Items.Single().Severity);
    }

    [Fact]
    public void Reason_code_catalog_contains_issue_207_p0_codes()
    {
        Assert.Equal("equipment.activeAlarm", EquipmentRuntimeReasonCodes.ActiveAlarm);
        Assert.Equal("equipment.stateUnavailable", EquipmentRuntimeReasonCodes.StateUnavailable);
        Assert.Equal("equipment.downtime", EquipmentRuntimeReasonCodes.Downtime);
        Assert.Equal("equipment.maintenanceWindow", EquipmentRuntimeReasonCodes.MaintenanceWindow);
        Assert.Equal("equipment.inspectionRequired", EquipmentRuntimeReasonCodes.InspectionRequired);
        Assert.Equal("equipment.sourceStale", EquipmentRuntimeReasonCodes.SourceStale);
        Assert.Equal("equipment.tagMappingMissing", EquipmentRuntimeReasonCodes.TagMappingMissing);
        Assert.Equal("equipment.noEligibleSubstitute", EquipmentRuntimeReasonCodes.NoEligibleSubstitute);
    }
}
```

- [ ] **步骤 3：运行测试并验证 RED**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj --no-restore
```

预期：失败，因为 `EquipmentRuntimeContracts.cs` 不存在。

- [ ] **步骤 4：实现设备运行时契约**

创建 `backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/EquipmentRuntimeContracts.cs`：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerv.IIP.Contracts.EquipmentRuntime;

public static class EquipmentRuntimeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public static class EquipmentRuntimeReasonCodes
{
    public const string ActiveAlarm = "equipment.activeAlarm";
    public const string StateUnavailable = "equipment.stateUnavailable";
    public const string Downtime = "equipment.downtime";
    public const string MaintenanceWindow = "equipment.maintenanceWindow";
    public const string InspectionRequired = "equipment.inspectionRequired";
    public const string SourceStale = "equipment.sourceStale";
    public const string TagMappingMissing = "equipment.tagMappingMissing";
    public const string NoEligibleSubstitute = "equipment.noEligibleSubstitute";
}

public sealed record EquipmentRuntimeAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<string>? DeviceAssetIds,
    IReadOnlyCollection<string>? WorkCenterIds,
    int FreshnessMaxAgeMinutes = 60);

public sealed record EquipmentRuntimeAvailabilityResponse(
    int ContractVersion,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset QueryWindowStartUtc,
    DateTimeOffset QueryWindowEndUtc,
    IReadOnlyCollection<EquipmentRuntimeAvailabilityWindowContract> Items);

public sealed record EquipmentRuntimeAvailabilityWindowContract(
    string DeviceAssetId,
    string? WorkCenterId,
    EquipmentRuntimeAvailabilityStatus AvailabilityStatus,
    string ReasonCode,
    EquipmentRuntimeSeverity Severity,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    EquipmentRuntimeSourceType SourceType,
    string SourceReferenceId,
    string MessageKey,
    IReadOnlyCollection<string> SubstituteDeviceAssetIds);

public sealed record EquipmentRuntimeCurrentStateResponse(
    int ContractVersion,
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    string? CurrentState,
    DateTimeOffset? StateOccurredAtUtc,
    bool IsSourceFresh,
    IReadOnlyCollection<EquipmentRuntimeAlarmSummary> ActiveAlarms);

public sealed record EquipmentRuntimeAlarmSummary(
    string AlarmEventId,
    string DeviceAssetId,
    string AlarmCode,
    string Severity,
    DateTimeOffset RaisedAtUtc,
    string ExternalAlarmId);

public enum EquipmentRuntimeAvailabilityStatus
{
    Available = 0,
    Unavailable = 1,
    Unknown = 2
}

public enum EquipmentRuntimeSeverity
{
    Info = 0,
    Warning = 1,
    Blocked = 2,
    Critical = 3
}

public enum EquipmentRuntimeSourceType
{
    DeviceState = 0,
    Alarm = 1,
    Downtime = 2,
    MaintenanceWindow = 3,
    Inspection = 4,
    StaleSource = 5,
    ManualBlock = 6
}
```

- [ ] **步骤 5：运行契约测试并验证 GREEN**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj --no-restore
```

预期：通过。

## Task 2：添加 IndustrialTelemetry 来源元数据和运行时可用性 API

**文件：**
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/DeviceStateSnapshotAggregate/DeviceStateSnapshot.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/TelemetrySummaryAggregate/TelemetrySummary.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/DeviceStateSnapshotEntityTypeConfiguration.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/TelemetrySummaryEntityTypeConfiguration.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/IndustrialTelemetryCommands.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/IndustrialTelemetryQueries.cs`
- 修改：`backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Endpoints/Iiot/IndustrialTelemetryEndpoints.cs`
- 测试：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointContractTests.cs`
- 测试：`backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetrySchemaConventionTests.cs`

- [ ] **步骤 1：引用设备运行时契约**

运行：

```powershell
dotnet add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/Nerv.IIP.Contracts.EquipmentRuntime.csproj
```

预期：IndustrialTelemetry Web 可引用 `Nerv.IIP.Contracts.EquipmentRuntime` 完成编译。

- [ ] **步骤 2：编写失败的 endpoint 和行为测试**

扩展 `IndustrialTelemetryEndpointContractTests.cs`，添加以下名称的测试：

```csharp
[Fact]
public void IndustrialTelemetry_endpoints_expose_issue_207_runtime_availability_routes()
{
    var contracts = IndustrialTelemetryEndpointContracts.All.ToArray();

    Assert.Contains(contracts, x => x.HttpMethod == "GET"
        && x.Route == "/api/business/v1/iiot/devices/{deviceAssetId}/runtime-availability"
        && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead
        && x.OperationId == "getBusinessIiotDeviceRuntimeAvailability");
    Assert.Contains(contracts, x => x.HttpMethod == "GET"
        && x.Route == "/api/business/v1/iiot/runtime-availability"
        && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead
        && x.OperationId == "queryBusinessIiotRuntimeAvailability");
    Assert.Contains(contracts, x => x.HttpMethod == "GET"
        && x.Route == "/api/business/v1/iiot/devices/{deviceAssetId}/current-state"
        && x.PermissionCode == IndustrialTelemetryPermissionCodes.TelemetryRead
        && x.OperationId == "getBusinessIiotDeviceCurrentState");
}

[Fact]
public async Task Runtime_availability_reports_active_alarm_and_state_unavailable_reason_codes()
{
    using var fixture = await IndustrialTelemetryApiFixture.CreateAsync();
    var client = fixture.CreateClientWithInternalServiceAuth();
    var windowStart = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    var windowEnd = new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero);

    await client.PostAsJsonAsync("/api/business/v1/iiot/samples", new RecordTelemetrySampleRequest(
        "org-001", "env-dev", "DEV-OIL-01", "run-state", windowStart, windowStart.AddMinutes(5),
        1, 0, 0, 0, "opcua-100", "faulted", windowStart.AddMinutes(4), "opcua", "line-1-gateway"));
    await client.PostAsJsonAsync("/api/business/v1/iiot/alarms", new PostAlarmEventRequest(
        "org-001", "env-dev", "DEV-OIL-01", "OIL_PRESSURE_LOW", "critical",
        windowStart.AddHours(1), "alarm-oil-001", null, null, null));

    var response = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeAvailabilityResponse>>(
        "/api/business/v1/iiot/devices/DEV-OIL-01/runtime-availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z",
        EquipmentRuntimeJson.Options);

    Assert.NotNull(response);
    Assert.Contains(response!.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
    Assert.Contains(response.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.StateUnavailable);
}
```

使用 `IndustrialTelemetryEndpointContractTests.cs` 中现有的 fixture 模式。如果 fixture 使用不同的 helper 名称，则保持测试意图并使用本地 fixture helper。

- [ ] **步骤 3：运行测试并验证 RED**

运行：

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~IndustrialTelemetryEndpointContractTests"
```

预期：失败，因为运行时可用性 DTO、请求来源元数据和 route 尚不存在。

- [ ] **步骤 4：向遥测状态和摘要事实添加来源元数据**

修改 `DeviceStateSnapshot` 和 `TelemetrySummary` 的构造函数及静态工厂，使其接收 `sourceSystem` 与 `sourceConnector`。在命令处理程序中为现有调用方保留默认值：

```csharp
public string SourceSystem { get; private set; } = string.Empty;
public string SourceConnector { get; private set; } = string.Empty;
```

按以下方式设置值：

```csharp
SourceSystem = IndustrialTelemetryText.Required(sourceSystem, nameof(sourceSystem)).ToLowerInvariant();
SourceConnector = IndustrialTelemetryText.Required(sourceConnector, nameof(sourceConnector));
```

更新重复检查以包含这两个字段：

```csharp
return OrganizationId == other.OrganizationId
    && EnvironmentId == other.EnvironmentId
    && DeviceAssetId == other.DeviceAssetId
    && SourceSystem == other.SourceSystem
    && SourceConnector == other.SourceConnector
    && SourceSequence == other.SourceSequence;
```

- [ ] **步骤 5：添加 EF 列和 migration**

在状态快照和遥测摘要的实体配置中添加带注释与最大长度的 `source_system` 和 `source_connector`。在 migration 中为现有行使用默认值：

```csharp
builder.Property(x => x.SourceSystem)
    .HasColumnName("source_system")
    .IsRequired()
    .HasMaxLength(80)
    .HasDefaultValue("default")
    .HasComment("External source system name, for example opcua, mqtt or manual.");

builder.Property(x => x.SourceConnector)
    .HasColumnName("source_connector")
    .IsRequired()
    .HasMaxLength(120)
    .HasDefaultValue("default")
    .HasComment("Connector or adapter instance that supplied the runtime fact.");
```

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddRuntimeSourceMetadata --project backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.csproj --startup-project backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj --output-dir Migrations
```

预期：migration 添加来源元数据，且不会将现有 schema history 移出 `industrial_telemetry`。

- [ ] **步骤 6：扩展样本请求和命令，同时不破坏现有 payload**

向 `RecordTelemetrySampleRequest` 和 `RecordTelemetrySampleCommand` 追加可选字段：

```csharp
string? SourceSystem = null,
string? SourceConnector = null
```

在处理程序中使用：

```csharp
var sourceSystem = string.IsNullOrWhiteSpace(request.SourceSystem) ? "default" : request.SourceSystem.Trim();
var sourceConnector = string.IsNullOrWhiteSpace(request.SourceConnector) ? "default" : request.SourceConnector.Trim();
```

将这些值传入 `DeviceStateSnapshot.Record(...)` 和 `TelemetrySummary.Record(...)`。

- [ ] **步骤 7：实现当前状态与可用性查询**

在 `IndustrialTelemetryQueries.cs` 中添加查询记录和处理程序：

```csharp
public sealed record GetDeviceCurrentStateQuery(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    int FreshnessMaxAgeMinutes = 60) : IQuery<EquipmentRuntimeCurrentStateResponse>;

public sealed record QueryRuntimeAvailabilityQuery(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<string>? DeviceAssetIds,
    IReadOnlyCollection<string>? WorkCenterIds,
    int FreshnessMaxAgeMinutes = 60) : IQuery<EquipmentRuntimeAvailabilityResponse>;
```

规则：

1. 最新状态先按 `OccurredAtUtc` 降序排列，再按 `RecordedAtUtc` 降序排列。
2. `running`、`ready` 和 `idle` 是可用状态。
3. `faulted`、`stopped`、`offline` 和 `down` 产生 `equipment.stateUnavailable`。
4. 任何满足 `Status == "raised"` 的报警都会产生 `equipment.activeAlarm`。
5. 仅当 `FreshnessMaxAgeMinutes > 0` 时，缺失或过期的最新状态才产生 `equipment.sourceStale`。
6. 将返回的每个 `StartUtc`/`EndUtc` 裁剪到请求的查询时间窗。
7. 对 `WindowEndUtc <= WindowStartUtc` 使用 `KnownException("Runtime availability window end must be after start.")` 拒绝请求。

- [ ] **步骤 8：添加 FastEndpoints route**

添加请求记录：

```csharp
public sealed record GetDeviceRuntimeAvailabilityRequest(
    string DeviceAssetId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int FreshnessMaxAgeMinutes = 60);

public sealed record QueryRuntimeAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetIds,
    string? WorkCenterIds,
    int FreshnessMaxAgeMinutes = 60);

public sealed record GetDeviceCurrentStateRequest(
    string DeviceAssetId,
    string OrganizationId,
    string EnvironmentId,
    int FreshnessMaxAgeMinutes = 60);
```

使用测试中的 operation ID 向 `IndustrialTelemetryEndpointContracts.All` 添加 endpoint。通过 `StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries` 将逗号分隔的 `DeviceAssetIds` 和 `WorkCenterIds` 解析为数组。

- [ ] **步骤 9：运行 IndustrialTelemetry 测试**

运行：

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore
```

预期：通过。

## Task 3：添加 Maintenance 运行时可用时间窗

**文件：**
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceEntityTypeConfigurations.cs`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceCommands.cs`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/MaintenanceQueries.cs`
- 修改：`backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- 测试：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointContractTests.cs`
- 测试：`backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSchemaConventionTests.cs`

- [ ] **步骤 1：引用设备运行时契约**

运行：

```powershell
dotnet add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/Nerv.IIP.Contracts.EquipmentRuntime.csproj
```

预期：Maintenance Web 可引用 `Nerv.IIP.Contracts.EquipmentRuntime` 完成编译。

- [ ] **步骤 2：编写失败的 route 和行为测试**

扩展 `MaintenanceEndpointContractTests.cs`：

```csharp
[Fact]
public void Maintenance_endpoints_expose_issue_207_availability_window_routes()
{
    var contracts = MaintenanceEndpointContracts.All.ToArray();

    Assert.Contains(contracts, x => x.HttpMethod == "GET"
        && x.Route == "/api/business/v1/maintenance/assets/{deviceAssetId}/availability-windows"
        && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead
        && x.OperationId == "getMaintenanceAssetAvailabilityWindows");
    Assert.Contains(contracts, x => x.HttpMethod == "GET"
        && x.Route == "/api/business/v1/maintenance/availability-windows"
        && x.PermissionCode == MaintenancePermissionCodes.WorkOrdersRead
        && x.OperationId == "queryMaintenanceAvailabilityWindows");
}

[Fact]
public async Task Availability_windows_include_active_asset_unavailable_and_planned_maintenance()
{
    using var fixture = await MaintenanceApiFixture.CreateAsync();
    var client = fixture.CreateClientWithInternalServiceAuth();

    await client.PostAsJsonAsync("/api/business/v1/maintenance/work-orders", new CreateMaintenanceWorkOrderRequest(
        "org-001", "env-dev", "DEV-OIL-01", "high", "alarm-oil-001", "operator-001", EquipmentRuntimeReasonCodes.ActiveAlarm));
    await client.PostAsJsonAsync("/api/business/v1/maintenance/plans", new CreateMaintenancePlanRequest(
        "org-001", "env-dev", "DEV-OIL-01", "PM-OIL-01", "P7D", new DateOnly(2026, 6, 1), "maintenance-team",
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero)));

    var response = await client.GetFromJsonAsync<ResponseData<EquipmentRuntimeAvailabilityResponse>>(
        "/api/business/v1/maintenance/assets/DEV-OIL-01/availability-windows?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z",
        EquipmentRuntimeJson.Options);

    Assert.NotNull(response);
    Assert.Contains(response!.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.ActiveAlarm);
    Assert.Contains(response.Data.Items, x => x.ReasonCode == EquipmentRuntimeReasonCodes.MaintenanceWindow);
}
```

如果现有 Maintenance 测试 fixture helper 的类名不同，则使用该 helper。

- [ ] **步骤 3：运行测试并验证 RED**

运行：

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MaintenanceEndpointContractTests"
```

预期：失败，因为可用性 route 和计划运行时窗口尚不存在。

- [ ] **步骤 4：添加显式维护计划运行时窗口**

扩展 `MaintenancePlan`，添加：

```csharp
public DateTimeOffset? WindowStartUtc { get; private set; }
public DateTimeOffset? WindowEndUtc { get; private set; }
```

更新 `MaintenancePlan.Create(...)`，使其接收可选窗口值并进行验证：

```csharp
if (windowStartUtc is not null && windowEndUtc is not null && windowEndUtc <= windowStartUtc)
{
    throw new ArgumentOutOfRangeException(nameof(windowEndUtc), "Maintenance window end must be after start.");
}
```

扩展 `CreateMaintenancePlanRequest` 和 `CreateMaintenancePlanCommand`，添加 `DateTimeOffset? WindowStartUtc` 与 `DateTimeOffset? WindowEndUtc`。

- [ ] **步骤 5：为计划窗口添加 EF migration**

在 `MaintenanceEntityTypeConfigurations.cs` 中添加带注释的可空列 `window_start_utc` 和 `window_end_utc`。

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddMaintenancePlanRuntimeWindow --project backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Nerv.IIP.Business.Maintenance.Infrastructure.csproj --startup-project backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj --output-dir Migrations
```

预期：migration 向 `maintenance.maintenance_plans` 添加可空窗口列。

- [ ] **步骤 6：实现维护可用性查询**

在 `MaintenanceQueries.cs` 中添加查询记录：

```csharp
public sealed record QueryMaintenanceAvailabilityWindowsQuery(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyCollection<string>? DeviceAssetIds,
    IReadOnlyCollection<string>? WorkCenterIds) : IQuery<EquipmentRuntimeAvailabilityResponse>;
```

规则：

1. 对于 `AssetUnavailable == true` 的未关闭工单，存在 `SourceAlarmId` 时产生 `equipment.activeAlarm`，否则产生 `equipment.downtime`。
2. 当已完成且资产不可用的工单时间窗与查询时间窗重叠时，产生从 `AssetUnavailableFromUtc` 到 `CompletedAtUtc` 的时间窗。
3. 同时具有 `WindowStartUtc` 和 `WindowEndUtc` 的计划在与查询时间窗重叠时产生 `equipment.maintenanceWindow`。
4. 结果为 `failed`、`fail`、`blocked` 或 `not-ok` 的点检，从 `InspectedAtUtc` 到查询结束时间产生 `equipment.inspectionRequired`。
5. 将所有返回窗口裁剪到查询时间窗。
6. 对 `WindowEndUtc <= WindowStartUtc` 使用 `KnownException("Maintenance availability window end must be after start.")` 拒绝请求。

- [ ] **步骤 7：添加 FastEndpoints route**

添加请求记录：

```csharp
public sealed record GetMaintenanceAssetAvailabilityWindowsRequest(
    string DeviceAssetId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc);

public sealed record QueryMaintenanceAvailabilityWindowsRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetIds,
    string? WorkCenterIds);
```

使用失败测试中的 operation ID 向 `MaintenanceEndpointContracts.All` 添加 route。

- [ ] **步骤 8：运行 Maintenance 测试**

运行：

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
```

预期：通过。

## Task 4：将运行时可用性接入排程输入

**文件：**
- 修改：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/EquipmentAvailabilitySchedulingAdapter.cs`
- 创建：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/EquipmentAvailabilitySchedulingAdapterTests.cs`
- 修改：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/FiniteCapacitySchedulerTests.cs`

- [ ] **步骤 1：引用设备运行时契约**

运行：

```powershell
dotnet add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/Nerv.IIP.Contracts.EquipmentRuntime.csproj
```

预期：Scheduling Web 可引用 `Nerv.IIP.Contracts.EquipmentRuntime` 完成编译。

- [ ] **步骤 2：编写失败的 adapter 测试**

创建 `EquipmentAvailabilitySchedulingAdapterTests.cs`：

```csharp
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class EquipmentAvailabilitySchedulingAdapterTests
{
    [Fact]
    public void Adapter_maps_unavailable_equipment_windows_to_scheduling_unavailability()
    {
        var runtime = new EquipmentRuntimeAvailabilityResponse(
            1,
            "org-001",
            "env-dev",
            new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero),
            [
                new EquipmentRuntimeAvailabilityWindowContract(
                    "DEV-OIL-01",
                    "WC-OIL-SEAL",
                    EquipmentRuntimeAvailabilityStatus.Unavailable,
                    EquipmentRuntimeReasonCodes.ActiveAlarm,
                    EquipmentRuntimeSeverity.Critical,
                    new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero),
                    EquipmentRuntimeSourceType.Alarm,
                    "alarm-oil-001",
                    "equipment.activeAlarm",
                    [])]);

        var windows = EquipmentAvailabilitySchedulingAdapter.ToUnavailabilityWindows(runtime);

        var window = Assert.Single(windows);
        Assert.Equal("DEV-OIL-01", window.ResourceId);
        Assert.Equal("WC-OIL-SEAL", window.WorkCenterId);
        Assert.Equal(EquipmentRuntimeReasonCodes.ActiveAlarm, window.ReasonCode);
    }

    [Fact]
    public void Runtime_equipment_window_causes_scheduler_equipment_conflict_for_shock_absorber_fixture()
    {
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var blocked = EquipmentAvailabilitySchedulingAdapter.Apply(
            problem,
            new EquipmentRuntimeAvailabilityResponse(
                1,
                problem.OrganizationId,
                problem.EnvironmentId,
                problem.HorizonStartUtc,
                problem.HorizonEndUtc,
                [
                    new EquipmentRuntimeAvailabilityWindowContract(
                        "DEV-OIL-01",
                        "WC-OIL-SEAL",
                        EquipmentRuntimeAvailabilityStatus.Unavailable,
                        EquipmentRuntimeReasonCodes.ActiveAlarm,
                        EquipmentRuntimeSeverity.Critical,
                        problem.HorizonStartUtc,
                        problem.HorizonEndUtc,
                        EquipmentRuntimeSourceType.Alarm,
                        "alarm-oil-001",
                        "equipment.activeAlarm",
                        [])]));

        var plan = new FiniteCapacityScheduler().Schedule(blocked, "plan-equipment-runtime-001", new DateTimeOffset(2026, 6, 1, 7, 0, 0, TimeSpan.Zero));

        Assert.Contains(plan.Conflicts, x => x.ReasonCode == ScheduleConflictReasonCodeContract.Equipment);
        Assert.Contains(plan.UnscheduledOperations, x => x.ReasonCode == ScheduleConflictReasonCodeContract.Equipment);
    }
}
```

- [ ] **步骤 3：运行测试并验证 RED**

运行：

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~EquipmentAvailabilitySchedulingAdapterTests"
```

预期：失败，因为 `EquipmentAvailabilitySchedulingAdapter` 不存在。

- [ ] **步骤 4：实现排程 adapter**

创建 `EquipmentAvailabilitySchedulingAdapter.cs`：

```csharp
using Nerv.IIP.Contracts.EquipmentRuntime;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;

public static class EquipmentAvailabilitySchedulingAdapter
{
    public static SchedulingProblemContract Apply(
        SchedulingProblemContract problem,
        EquipmentRuntimeAvailabilityResponse availability)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(availability);
        if (problem.OrganizationId != availability.OrganizationId || problem.EnvironmentId != availability.EnvironmentId)
        {
            throw new ArgumentException("Equipment runtime availability context does not match scheduling problem context.", nameof(availability));
        }

        var windows = problem.UnavailabilityWindows
            .Concat(ToUnavailabilityWindows(availability))
            .OrderBy(x => x.ResourceId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.WorkCenterId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.StartUtc)
            .ThenBy(x => x.EndUtc)
            .ThenBy(x => x.ReasonCode, StringComparer.Ordinal)
            .ToArray();

        return problem with { UnavailabilityWindows = windows };
    }

    public static IReadOnlyCollection<SchedulingUnavailabilityWindowContract> ToUnavailabilityWindows(
        EquipmentRuntimeAvailabilityResponse availability)
    {
        return availability.Items
            .Where(x => x.AvailabilityStatus != EquipmentRuntimeAvailabilityStatus.Available)
            .Select(x => new SchedulingUnavailabilityWindowContract(
                ResourceId: string.IsNullOrWhiteSpace(x.DeviceAssetId) ? null : x.DeviceAssetId,
                WorkCenterId: string.IsNullOrWhiteSpace(x.WorkCenterId) ? null : x.WorkCenterId,
                StartUtc: x.StartUtc,
                EndUtc: x.EndUtc,
                ReasonCode: x.ReasonCode))
            .ToArray();
    }
}
```

- [ ] **步骤 5：运行 Scheduling 测试**

运行：

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore
```

预期：通过。

## Task 5：使 MES 就绪状态与设备运行时原因码对齐

**文件：**
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesReadinessReasonCodes.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs`
- 测试：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesPersistenceContractTests.cs`
- 测试：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesEndpointContractTests.cs`

- [ ] **步骤 1：引用设备运行时契约**

运行：

```powershell
dotnet add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.EquipmentRuntime/Nerv.IIP.Contracts.EquipmentRuntime.csproj
```

预期：MES Web 可引用 `Nerv.IIP.Contracts.EquipmentRuntime` 完成编译。

- [ ] **步骤 2：编写失败的 MES 就绪状态测试**

向 `MesPersistenceContractTests.cs` 添加测试：

```csharp
[Fact]
public async Task Equipment_readiness_returns_shared_active_alarm_reason_code()
{
    var services = CreateServices(nameof(Equipment_readiness_returns_shared_active_alarm_reason_code));
    await using var scope = services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.WorkCenterUnavailabilities.Add(WorkCenterUnavailability.Open(
        "org-001",
        "env-dev",
        "DOWNTIME-001",
        "WC-OIL-SEAL",
        new DateTimeOffset(2026, 6, 1, 9, 30, 0, TimeSpan.Zero),
        null,
        EquipmentRuntimeReasonCodes.ActiveAlarm,
        "DEV-OIL-01"));
    await dbContext.SaveChangesAsync();

    var handler = new GetMesFoundationReadinessAreaQueryHandler(new MesFoundationReadinessService(dbContext));
    var readiness = await handler.Handle(new GetMesFoundationReadinessAreaQuery(
        "org-001",
        "env-dev",
        "equipment",
        null,
        null,
        "WC-OIL-SEAL",
        null,
        null,
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero)), CancellationToken.None);

    Assert.Equal("Blocked", readiness.Status);
    var issue = Assert.Single(readiness.Issues);
    Assert.Equal(EquipmentRuntimeReasonCodes.ActiveAlarm, issue.Code);
    Assert.Equal("IndustrialTelemetry", issue.SourceSystem);
    Assert.Equal("DEV-OIL-01", issue.ReferenceDisplayName);
}

[Fact]
public async Task Operation_start_rejects_same_equipment_reason_code_used_by_readiness()
{
    var services = CreateServices(nameof(Operation_start_rejects_same_equipment_reason_code_used_by_readiness));
    await using var scope = services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await SeedReleasedWorkOrderWithOperationAsync(dbContext, "org-001", "env-dev", "WO-OIL-001", "OP-OIL-001", "WC-OIL-SEAL");
    dbContext.WorkCenterUnavailabilities.Add(WorkCenterUnavailability.Open(
        "org-001",
        "env-dev",
        "DOWNTIME-002",
        "WC-OIL-SEAL",
        new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero),
        null,
        EquipmentRuntimeReasonCodes.MaintenanceWindow,
        "DEV-OIL-01"));
    await dbContext.SaveChangesAsync();

    var handler = new ChangeOperationTaskStateCommandHandler(dbContext);
    var ex = await Assert.ThrowsAsync<KnownException>(() => handler.Handle(
        new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-OIL-001", "start", new DateTimeOffset(2026, 6, 1, 10, 30, 0, TimeSpan.Zero)),
        CancellationToken.None));

    Assert.Contains(EquipmentRuntimeReasonCodes.MaintenanceWindow, ex.Message, StringComparison.Ordinal);
}
```

在可用时使用现有的 MES 测试 seed helper。如果第二个测试没有可用 helper，则在同一测试文件中创建私有 helper，通过现有领域工厂方法插入工单和工序任务。

- [ ] **步骤 3：运行测试并验证 RED**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Equipment_readiness_returns_shared_active_alarm_reason_code|FullyQualifiedName~Operation_start_rejects_same_equipment_reason_code_used_by_readiness"
```

预期：失败，因为 MES 仍通过旧式大写原因码和字符串启发式规则映射原因。

- [ ] **步骤 4：替换设备就绪状态代码分类**

修改 `MesReadinessReasonCodes.cs`：

```csharp
using Nerv.IIP.Contracts.EquipmentRuntime;

namespace Nerv.IIP.Business.Mes.Web.Application.Readiness;

public static class MesReadinessReasonCodes
{
    public const string QualityPlanMissing = "QUALITY_PLAN_MISSING";
    public const string QualityHoldActive = "QUALITY_HOLD_ACTIVE";

    public const string EquipmentActiveAlarm = EquipmentRuntimeReasonCodes.ActiveAlarm;
    public const string EquipmentStateUnavailable = EquipmentRuntimeReasonCodes.StateUnavailable;
    public const string EquipmentDowntime = EquipmentRuntimeReasonCodes.Downtime;
    public const string EquipmentMaintenanceWindow = EquipmentRuntimeReasonCodes.MaintenanceWindow;
    public const string EquipmentInspectionRequired = EquipmentRuntimeReasonCodes.InspectionRequired;
    public const string EquipmentSourceStale = EquipmentRuntimeReasonCodes.SourceStale;
    public const string EquipmentTagMappingMissing = EquipmentRuntimeReasonCodes.TagMappingMissing;

    public static EquipmentReadinessClassification ClassifyEquipmentReason(string reason)
    {
        var normalized = string.IsNullOrWhiteSpace(reason)
            ? EquipmentRuntimeReasonCodes.Downtime
            : reason.Trim();

        return normalized switch
        {
            EquipmentRuntimeReasonCodes.ActiveAlarm => new EquipmentReadinessClassification(
                normalized,
                "IndustrialTelemetry",
                "设备存在未解除报警，当前工序不能派工或开工。",
                "处理并解除设备报警后重新检查"),
            EquipmentRuntimeReasonCodes.StateUnavailable => new EquipmentReadinessClassification(
                normalized,
                "IndustrialTelemetry",
                "设备状态不可运行，当前工序不能派工或开工。",
                "确认设备已恢复运行状态后重新检查"),
            EquipmentRuntimeReasonCodes.MaintenanceWindow => new EquipmentReadinessClassification(
                normalized,
                "Maintenance",
                "设备存在维修或保养占用，当前工序不能派工或开工。",
                "调整维修窗口、选择替代设备或等待维修释放"),
            EquipmentRuntimeReasonCodes.InspectionRequired => new EquipmentReadinessClassification(
                normalized,
                "Maintenance",
                "设备点检未完成或点检未通过，当前工序不能开工。",
                "完成点检并确认结果后重新检查"),
            EquipmentRuntimeReasonCodes.SourceStale => new EquipmentReadinessClassification(
                normalized,
                "IndustrialTelemetry",
                "设备运行数据超过有效时间，当前状态不可信。",
                "检查采集连接并刷新设备状态"),
            EquipmentRuntimeReasonCodes.TagMappingMissing => new EquipmentReadinessClassification(
                normalized,
                "IndustrialTelemetry",
                "设备缺少必要采集点映射，无法确认运行状态。",
                "补齐设备 tag 映射后重新检查"),
            _ => new EquipmentReadinessClassification(
                EquipmentRuntimeReasonCodes.Downtime,
                "BusinessMes",
                "MES 停机记录显示设备或工作中心当前不可用。",
                "关闭停机事件、选择替代设备或调整派工时间"),
        };
    }
}
```

- [ ] **步骤 5：在下达/开工错误中保留原因码**

保持 `ReadinessReasonCodes.GetEquipmentBlockingIssuesAsync(...)` 返回 `classification.Code`，而不是旧式大写值。更新下达和开工命令中的异常消息，使其拼接精确的 issue code：

```csharp
throw new KnownException(string.Join("; ", equipmentIssues.Select(x => x.Code)));
```

该行已经存在；完成步骤 4 后，它会发出共享设备原因码。

- [ ] **步骤 6：运行 MES 测试**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

预期：通过。

## Task 6：添加 BusinessGateway 设备 facade

**文件：**
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Equipment/BusinessConsoleEquipmentEndpoints.cs`
- 测试：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- 测试：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- 测试：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayTestServiceBaseUrls.cs`

- [ ] **步骤 1：添加 Gateway 权限**

添加常量：

```csharp
public const string IiotTelemetryRead = "business.iiot.telemetry.read";
public const string IiotAlarmsRead = "business.iiot.alarms.read";
public const string MaintenanceWorkOrdersRead = "business.maintenance.work-orders.read";
public const string MaintenancePlansRead = "business.maintenance.plans.read";
```

概览、详情和可用性页面使用 `IiotTelemetryRead`；报警列表页面使用 `IiotAlarmsRead`。

- [ ] **步骤 2：添加失败的 OpenAPI、授权和代理测试**

添加 OpenAPI 断言：

```csharp
AssertOperationId(paths, "/api/business-console/v1/equipment/overview", "get", "getBusinessConsoleEquipmentOverview");
AssertOperationId(paths, "/api/business-console/v1/equipment/devices/{deviceAssetId}", "get", "getBusinessConsoleEquipmentDevice");
AssertOperationId(paths, "/api/business-console/v1/equipment/availability", "get", "getBusinessConsoleEquipmentAvailability");
AssertOperationId(paths, "/api/business-console/v1/equipment/alarms", "get", "listBusinessConsoleEquipmentAlarms");
```

添加授权 route 映射：

```csharp
routes.Add(HttpMethod.Get, "/api/business-console/v1/equipment/overview", BusinessGatewayPermissions.IiotTelemetryRead);
routes.Add(HttpMethod.Get, "/api/business-console/v1/equipment/devices/DEV-OIL-01", BusinessGatewayPermissions.IiotTelemetryRead);
routes.Add(HttpMethod.Get, "/api/business-console/v1/equipment/availability", BusinessGatewayPermissions.IiotTelemetryRead);
routes.Add(HttpMethod.Get, "/api/business-console/v1/equipment/alarms", BusinessGatewayPermissions.IiotAlarmsRead);
```

添加代理测试断言：对 `/api/business-console/v1/equipment/availability?organizationId=org-001&environmentId=env-dev&windowStartUtc=2026-06-01T08:00:00Z&windowEndUtc=2026-06-01T16:00:00Z&deviceAssetIds=DEV-OIL-01` 的请求，会使用相同的上下文与查询时间窗调用 IndustrialTelemetry 和 Maintenance 记录客户端。

- [ ] **步骤 3：运行 gateway 测试并验证 RED**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~BusinessGatewayOpenApiTests|FullyQualifiedName~BusinessGatewayAuthorizationTests|FullyQualifiedName~BusinessGatewayProxyTests"
```

预期：失败，因为设备 facade route 和客户端尚不存在。

- [ ] **步骤 4：添加 gateway 模型和下游客户端**

向 `BusinessConsoleModels.cs` 添加模型记录：

```csharp
public sealed record BusinessConsoleEquipmentContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleEquipmentAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    string? DeviceAssetIds,
    string? WorkCenterIds);

public sealed record BusinessConsoleEquipmentOverviewResponse(
    IReadOnlyCollection<BusinessConsoleEquipmentDeviceSummary> Devices,
    IReadOnlyCollection<EquipmentRuntimeAvailabilityWindowContract> ActiveBlocks);

public sealed record BusinessConsoleEquipmentDeviceSummary(
    string DeviceAssetId,
    string? CurrentState,
    bool IsSourceFresh,
    int ActiveAlarmCount,
    int ActiveBlockCount);

public sealed record BusinessConsoleEquipmentDeviceDetailResponse(
    EquipmentRuntimeCurrentStateResponse CurrentState,
    EquipmentRuntimeAvailabilityResponse Availability);

public sealed record BusinessConsoleEquipmentAlarmListResponse(
    IReadOnlyCollection<EquipmentRuntimeAlarmSummary> Items);
```

添加 `IBusinessIndustrialTelemetryClient` 和 `IBusinessMaintenanceClient` 接口及其 HTTP 实现。严格使用任务 2 和任务 3 定义的下游路径。

- [ ] **步骤 5：在 Program.cs 中注册客户端**

添加 base address：

```csharp
var industrialTelemetryBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "IndustrialTelemetry:BaseUrl", "http://localhost:5116");
var maintenanceBaseAddress = ResolveServiceBaseAddress(builder.Configuration, builder.Environment, "Maintenance:BaseUrl", "http://localhost:5117");
```

注册：

```csharp
builder.Services.AddHttpClient<IBusinessIndustrialTelemetryClient, HttpBusinessIndustrialTelemetryClient>(client =>
{
    client.BaseAddress = industrialTelemetryBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();

builder.Services.AddHttpClient<IBusinessMaintenanceClient, HttpBusinessMaintenanceClient>(client =>
{
    client.BaseAddress = maintenanceBaseAddress;
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddBusinessGatewayNonIdempotentSafeResilience();
```

- [ ] **步骤 6：添加设备 endpoint**

创建 `BusinessConsoleEquipmentEndpoints.cs`。每个 endpoint 都应继承 MES/Scheduling endpoint 使用的本地授权代理 endpoint 模式。实现：

1. `GET /api/business-console/v1/equipment/overview`
2. `GET /api/business-console/v1/equipment/devices/{deviceAssetId}`
3. `GET /api/business-console/v1/equipment/availability`
4. `GET /api/business-console/v1/equipment/alarms`

概览应调用 IndustrialTelemetry 当前状态/可用性和 Maintenance 可用性，按 `DeviceAssetId` 合并，并且只返回页面 DTO。不得持久化事实或计算报警生命周期。

- [ ] **步骤 7：运行 gateway 测试**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

预期：通过。

## Task 7：添加 Business Console 设备页面

**文件：**
- 创建：`frontend/apps/business-console/src/composables/useBusinessEquipment.ts`
- 创建：`frontend/apps/business-console/src/composables/useBusinessEquipment.test.ts`
- 创建：`frontend/apps/business-console/src/pages/equipment/index.vue`
- 创建：`frontend/apps/business-console/src/pages/equipment/alarms.vue`
- 创建：`frontend/apps/business-console/src/pages/equipment/[deviceAssetId].vue`
- 修改：codegen 后位于 `frontend/packages/api-client/src/generated/business-console/` 下的生成 API 客户端

- [ ] **步骤 1：BusinessGateway route 存在后重新生成 API 客户端**

运行：

```powershell
pnpm -C frontend generate:api
```

预期：生成的 business-console API 客户端导出 `getBusinessConsoleEquipmentOverview`、`getBusinessConsoleEquipmentDevice`、`getBusinessConsoleEquipmentAvailability` 和 `listBusinessConsoleEquipmentAlarms` 查询选项 helper。

- [ ] **步骤 2：编写失败的 composable 测试**

创建 `useBusinessEquipment.test.ts`：

```ts
import { describe, expect, it } from 'vitest'
import { describeEquipmentReason, equipmentStatusTone } from './useBusinessEquipment'

describe('business equipment helpers', () => {
  it('maps shared runtime reason codes to Chinese operator copy', () => {
    expect(describeEquipmentReason('equipment.activeAlarm')).toEqual({
      label: '设备报警未解除',
      nextStep: '处理并解除设备报警后重新检查',
    })
    expect(describeEquipmentReason('equipment.maintenanceWindow')).toEqual({
      label: '维修保养占用',
      nextStep: '调整维修窗口、等待释放或选择替代设备',
    })
  })

  it('maps status to stable UI tones', () => {
    expect(equipmentStatusTone('running')).toBe('success')
    expect(equipmentStatusTone('faulted')).toBe('danger')
    expect(equipmentStatusTone(undefined)).toBe('muted')
  })
})
```

- [ ] **步骤 3：运行前端测试并验证 RED**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console test -- useBusinessEquipment.test.ts
```

预期：失败，因为 `useBusinessEquipment.ts` 不存在。

- [ ] **步骤 4：实现设备 composable**

创建 `useBusinessEquipment.ts` 并导入生成的查询选项。导出：

```ts
export function describeEquipmentReason(code: string) {
  const copy: Record<string, { label: string, nextStep: string }> = {
    'equipment.activeAlarm': {
      label: '设备报警未解除',
      nextStep: '处理并解除设备报警后重新检查',
    },
    'equipment.stateUnavailable': {
      label: '设备状态不可运行',
      nextStep: '确认设备恢复运行后重新检查',
    },
    'equipment.downtime': {
      label: '设备停机中',
      nextStep: '关闭停机事件或改派可用设备',
    },
    'equipment.maintenanceWindow': {
      label: '维修保养占用',
      nextStep: '调整维修窗口、等待释放或选择替代设备',
    },
    'equipment.inspectionRequired': {
      label: '点检未通过',
      nextStep: '完成点检并确认结果后重新检查',
    },
    'equipment.sourceStale': {
      label: '采集数据过期',
      nextStep: '检查采集连接并刷新设备状态',
    },
    'equipment.tagMappingMissing': {
      label: '采集点未配置',
      nextStep: '补齐设备采集点映射',
    },
    'equipment.noEligibleSubstitute': {
      label: '无可替代设备',
      nextStep: '调整排程或维护设备能力配置',
    },
  }
  return copy[code] ?? { label: code, nextStep: '查看设备详情并处理来源业务单据' }
}

export function equipmentStatusTone(status: string | undefined) {
  const normalized = status?.toLowerCase()
  if (normalized === 'running' || normalized === 'ready' || normalized === 'idle')
    return 'success'
  if (normalized === 'faulted' || normalized === 'stopped' || normalized === 'offline' || normalized === 'down')
    return 'danger'
  return 'muted'
}
```

另行导出 `useBusinessEquipmentOverview`、`useBusinessEquipmentAvailability`、`useBusinessEquipmentDevice` 和 `useBusinessEquipmentAlarms` wrapper，与现有 `useBusinessMes.ts` 的 Pinia Colada 风格保持一致。

- [ ] **步骤 5：构建页面**

创建：

1. `frontend/apps/business-console/src/pages/equipment/index.vue`，包含状态看板、当前阻塞列表和行链接。
2. `frontend/apps/business-console/src/pages/equipment/alarms.vue`，包含当前/近期报警表格。
3. `frontend/apps/business-console/src/pages/equipment/[deviceAssetId].vue`，包含当前状态、当前报警、可用性时间窗，以及 facade 返回时关联的排程/工单引用。

要求：

1. 页面文本使用中文业务文案。
2. 不显示来源序列、事件 envelope、算法版本、组织/环境上下文或 connector 调试元数据。
3. 使用现有 business-console 布局、表格、badge 和按钮；不得在 `@nerv-iip/ui` 之外 deep-import shadcn 组件。

- [ ] **步骤 6：运行前端聚焦检查**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test -- useBusinessEquipment.test.ts
pnpm -C frontend --filter @nerv-iip/business-console build
```

预期：所有命令均通过。

## Task 8：更新文档、验证脚本和治理测试

**文件：**
- 修改：`docs/architecture/api-contract-and-codegen.md`
- 修改：`docs/architecture/authorization-matrix.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/frontend-navigation-map.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 创建：`scripts/verify-business-iiot-runtime-facts-aps-mes.ps1`
- 创建：`scripts/tests/business-iiot-runtime-verify-script.Tests.ps1`

- [ ] **步骤 1：根据实际代码事实更新架构文档**

任务 1-7 编译通过后，使用已实现的精确 route、schema 变更和验证命令更新文档：

1. `api-contract-and-codegen.md`：添加 BusinessGateway 设备 operation ID。
2. `authorization-matrix.md`：说明 #207 运行时可用性 facade 使用 `business.iiot.telemetry.read`、`business.iiot.alarms.read`、`business.maintenance.work-orders.read` 和 `business.maintenance.plans.read`。
3. `database-schema-catalog.md`：添加 IndustrialTelemetry 的 `source_system/source_connector` 列和 Maintenance 计划的 `window_start_utc/window_end_utc` 列。
4. `frontend-navigation-map.md`：只有后端运行时事实存在后，才将设备/IIoT 页面标记为 route-ready。
5. `implementation-readiness.md`：添加 #207 状态、限制和验证命令。

- [ ] **步骤 2：编写脚本治理测试**

创建 `scripts/tests/business-iiot-runtime-verify-script.Tests.ps1`：

```powershell
Describe "business iiot runtime verify script" {
    It "uses ScriptAutomation helpers and does not call native tools directly" {
        $script = Get-Content -Raw -Path "scripts/verify-business-iiot-runtime-facts-aps-mes.ps1"
        $script | Should -Match "Script-Governance:"
        $script | Should -Match "Invoke-DotNet"
        $script | Should -Match "Invoke-Pnpm"
        $script | Should -Not -Match "(?m)^\\s*dotnet\\s"
        $script | Should -Not -Match "(?m)^\\s*pnpm\\s"
        $script | Should -Not -Match "(?m)^\\s*pwsh\\s"
    }
}
```

- [ ] **步骤 3：创建受治理的验证脚本**

创建 `scripts/verify-business-iiot-runtime-facts-aps-mes.ps1`：

```powershell
# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores backend and frontend dependencies when SkipRestore is not set
#     - Runs #207 focused backend, gateway and business-console checks
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
#     - frontend build/test outputs
#     - artifacts/script-logs/**
#   Cleanup:
#     - None required
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Node.js >=22.18.0
#     - pnpm 11.1.2

[CmdletBinding()]
param(
    [switch] $SkipRestore,
    [switch] $SkipFrontend
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

if (-not $SkipRestore) {
    Invoke-DotNet -Name "business-iiot-runtime-backend-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

$testProjects = @(
    "backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj",
    "backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj",
    "backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj",
    "backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj",
    "backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj",
    "backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj"
)

foreach ($project in $testProjects) {
    Invoke-DotNet -Name ("business-iiot-runtime-test-" + [System.IO.Path]::GetFileNameWithoutExtension($project)) -WorkingDirectory $root -Arguments @(
        "test",
        $project,
        "--no-restore"
    ) | Out-Null
}

if (-not $SkipFrontend) {
    Invoke-Pnpm -Name "business-iiot-runtime-api-generate" -WorkingDirectory $root -Arguments @(
        "-C",
        "frontend",
        "generate:api"
    ) | Out-Null

    Invoke-Pnpm -Name "business-console-typecheck" -WorkingDirectory $root -Arguments @(
        "-C",
        "frontend",
        "--filter",
        "@nerv-iip/business-console",
        "typecheck"
    ) | Out-Null

    Invoke-Pnpm -Name "business-console-test" -WorkingDirectory $root -Arguments @(
        "-C",
        "frontend",
        "--filter",
        "@nerv-iip/business-console",
        "test"
    ) | Out-Null

    Invoke-Pnpm -Name "business-console-build" -WorkingDirectory $root -Arguments @(
        "-C",
        "frontend",
        "--filter",
        "@nerv-iip/business-console",
        "build"
    ) | Out-Null
}

Write-Host "Business IIoT runtime facts APS/MES verified."
```

- [ ] **步骤 4：运行脚本治理**

运行：

```powershell
pwsh scripts/check-script-governance.ps1
```

预期：通过。

## Task 9：最终验证

**文件：**
- 任务 1-8 修改的所有文件

- [ ] **步骤 1：运行聚焦后端测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj --no-restore
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

预期：所有命令均通过。

- [ ] **步骤 2：运行受治理验证**

运行：

```powershell
pwsh scripts/verify-business-iiot-runtime-facts-aps-mes.ps1
```

预期：通过。如果前端依赖还原受本地 Node/pnpm 前置条件阻塞，则使用 `-SkipFrontend` 重新运行，并明确报告前端前置条件缺口。

- [ ] **步骤 3：运行后端构建**

运行：

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期：构建通过且没有新增 warning。

- [ ] **步骤 4：运行最终前端聚焦检查**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
```

预期：所有命令均通过。

- [ ] **步骤 5：检查最终差异中的生成内容和文档漂移**

运行：

```powershell
git diff --check
git status --short
```

预期：`git diff --check` 通过；`git status --short` 仅显示预期的 #207 文件。
