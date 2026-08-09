# BusinessScheduling APS Lite 实施计划

> **面向代理执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**通过创建 BusinessScheduling 服务、稳定的 APS lite 契约、确定性有限产能排程器、持久化/API 界面、BusinessGateway facade 和验证脚本来实施 #206。

**架构：**BusinessScheduling 是位于 `backend/services/Business/Scheduling`、使用 `scheduling` schema 的 CleanDDD 业务服务。纯排程器消费完全物化的 `SchedulingProblem` 并返回 `SchedulePlan`；服务 endpoint 和适配器负责持久化、OpenAPI、权限及事件。MES、#78 Gantt 和 BusinessGateway 消费 Scheduling 输出，不计算正式排程。

**技术栈：**.NET 10、NetCorePal CleanDDD 模板、FastEndpoints、EF Core PostgreSQL、xUnit、ADR 0011 集成事件转换、`Nerv.IIP.Testing` schema 约定辅助程序、由 Hey API 生成的 `@nerv-iip/api-client`。

---

## 规格

本计划以 `docs/superpowers/specs/2026-05-31-business-scheduling-aps-lite-design.md` 作为领域契约。ADR 0014 是 APS/MES/IIoT 边界的权威架构依据。

## 文件

- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/Nerv.IIP.Contracts.Scheduling.csproj`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs`
- 创建：`backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/SchedulingContractSerializationTests.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/Nerv.IIP.Business.Scheduling.Domain.csproj`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Nerv.IIP.Business.Scheduling.Infrastructure.csproj`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/SchedulePlanAggregate/SchedulePlan.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Auth/SchedulingPermissionCodes.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/FiniteCapacityScheduler.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/ShockAbsorberSchedulingFixture.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/*.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Queries/*.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEvents/SchedulingIntegrationEvents.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventConverters/SchedulingIntegrationEventConverters.cs`
- 创建：`backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints/Scheduling/SchedulingEndpoints.cs`
- 创建：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/SchedulePlanAggregateTests.cs`
- 创建：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/FiniteCapacitySchedulerTests.cs`
- 创建：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEndpointContractTests.cs`
- 创建：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingIntegrationEventTests.cs`
- 创建：`backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingSchemaConventionTests.cs`
- 修改：`backend/Nerv.IIP.sln`
- 修改：仅当新的 Scheduling 项目引入兄弟服务已使用的中央包版本要求时，修改 `backend/Directory.Packages.props`
- 修改：`infra/aspire/Nerv.IIP.AppHost/Program.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Scheduling/BusinessConsoleSchedulingEndpoints.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/*`
- 修改：`docs/architecture/authorization-matrix.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 创建：`scripts/verify-business-scheduling-aps-lite.ps1`

## Task 1：优先创建 Scheduling 契约

- [ ] **步骤 1：创建契约与测试项目外壳**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.Scheduling -o backend/common/Contracts/Nerv.IIP.Contracts.Scheduling --framework net10.0
dotnet new xunit -n Nerv.IIP.Contracts.Scheduling.Tests -o backend/tests/Nerv.IIP.Contracts.Scheduling.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/Nerv.IIP.Contracts.Scheduling.csproj
```

预期：契约与测试项目已存在，但排程记录尚不存在。

- [ ] **步骤 2：编写会失败的契约序列化测试**

创建 `backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/SchedulingContractSerializationTests.cs`，其中包含以下命名的测试：

```csharp
[Fact]
public void Scheduling_problem_round_trips_contract_version_and_core_inputs()
{
    var problem = SchedulingContractSamples.CreateShockAbsorberProblem();
    var json = JsonSerializer.Serialize(problem, SchedulingJson.Options);
    var roundTrip = JsonSerializer.Deserialize<SchedulingProblemContract>(json, SchedulingJson.Options);

    Assert.NotNull(roundTrip);
    Assert.Equal(1, roundTrip!.ContractVersion);
    Assert.Equal("org-001", roundTrip.OrganizationId);
    Assert.Contains(roundTrip.Orders, x => x.OrderId == "WO-RUSH-REAR-001");
    Assert.Contains(roundTrip.Resources, x => x.ResourceId == "DEV-OIL-01");
}

[Fact]
public void Schedule_plan_round_trips_assignments_conflicts_and_gantt_items()
{
    var plan = SchedulingContractSamples.CreateExpectedShockAbsorberPlan();
    var json = JsonSerializer.Serialize(plan, SchedulingJson.Options);
    var roundTrip = JsonSerializer.Deserialize<SchedulePlanContract>(json, SchedulingJson.Options);

    Assert.NotNull(roundTrip);
    Assert.Equal("aps-lite-v1", roundTrip!.AlgorithmVersion);
    Assert.NotEmpty(roundTrip.Assignments);
    Assert.NotEmpty(roundTrip.ResourceLoads);
    Assert.NotEmpty(roundTrip.GanttItems);
}
```

在生产契约就绪前，示例辅助程序可以位于同一测试文件中。

- [ ] **步骤 3：运行测试并验证 RED**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj --no-restore
```

预期：FAIL，因为 `Nerv.IIP.Contracts.Scheduling` 和契约记录尚不存在。

- [ ] **步骤 4：实现最小契约**

创建 `SchedulingContracts.cs`，其中包含 public record 与 enum：

```csharp
public sealed record SchedulingProblemContract(
    int ContractVersion,
    string ProblemId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<SchedulingOrderContract> Orders,
    IReadOnlyCollection<SchedulingResourceContract> Resources,
    IReadOnlyCollection<SchedulingCalendarContract> Calendars,
    IReadOnlyCollection<SchedulingUnavailabilityWindowContract> UnavailabilityWindows,
    IReadOnlyCollection<SchedulingLockedAssignmentContract> LockedAssignments);

public sealed record SchedulePlanContract(
    int ContractVersion,
    string PlanId,
    string ProblemId,
    string ProblemFingerprint,
    string AlgorithmVersion,
    SchedulePlanStatusContract Status,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyCollection<ScheduleAssignmentContract> Assignments,
    IReadOnlyCollection<ScheduleResourceLoadContract> ResourceLoads,
    IReadOnlyCollection<ScheduleConflictContract> Conflicts,
    IReadOnlyCollection<UnscheduledOperationContract> UnscheduledOperations,
    IReadOnlyCollection<ScheduleChangeContract> ChangeSummary,
    IReadOnlyCollection<GanttScheduleItemContract> GanttItems);
```

增加订单、工序、资源、日历、不可用窗口、分配、负载、冲突、未排程工序、变更和 Gantt 项的配套 record（记录类型）。保持其为不可变 record，并且只包含 primitive/string/decimal/date-time 成员。

- [ ] **步骤 5：运行契约测试并验证 GREEN**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj --no-restore
```

预期：PASS。

## Task 2：搭建 BusinessScheduling 服务骨架

- [ ] **步骤 1：创建服务项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Scheduling -o backend/services/Business/Scheduling --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Scheduling.Domain.Tests -o backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Scheduling.Web.Tests -o backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests --framework net10.0
```

预期：Domain、Infrastructure、Web 和测试项目均已存在。

- [ ] **步骤 2：移除模板演示代码**

删除模板演示 endpoint、示例 aggregate、示例 migration、SignalR hub 和演示测试。

运行：

```powershell
Get-ChildItem -Recurse -File backend/services/Business/Scheduling | Select-String -Pattern 'OrderAggregate','DeliverRecord','LoginEndpoint','ChatHub','LockEndpoint' -SimpleMatch
```

预期：无匹配项。

- [ ] **步骤 3：将服务加入 solution**

运行：

```powershell
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/Nerv.IIP.Contracts.Scheduling.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/Nerv.IIP.Business.Scheduling.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Nerv.IIP.Business.Scheduling.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj
```

预期：所有 Scheduling 项目均已加入 `backend/Nerv.IIP.sln`。

## Task 3：通过 TDD 实现纯有限产能排程器

- [ ] **步骤 1：编写会失败的排程器测试**

创建 `FiniteCapacitySchedulerTests.cs`，覆盖：

1. `Schedule_returns_identical_plan_for_repeated_shock_absorber_input`
2. `Schedule_preserves_operation_precedence`
3. `Schedule_avoids_maintenance_window`
4. `Schedule_places_rush_order_before_normal_order_on_shared_bottleneck`
5. `Schedule_reports_due_date_conflict_when_assignment_finishes_late`
6. `Schedule_preserves_locked_assignment_and_reserves_capacity`
7. `Schedule_returns_unscheduled_reason_when_no_resource_can_run_operation`
8. `Schedule_reports_invalid_locked_assignment_when_locked_capacity_is_overbooked`
9. `Schedule_rejects_non_positive_operation_duration`
10. `Schedule_rejects_duplicate_resource_or_calendar_ids`
11. `Schedule_uses_canonical_fingerprint_for_reordered_equivalent_problem`
12. `Schedule_reports_capacity_reason_when_resource_is_saturated`
13. `Schedule_reports_calendar_reason_when_no_shift_can_fit_operation`
14. `Schedule_merges_overlapping_unavailability_when_computing_load`
15. `Schedule_reports_equipment_reason_when_all_eligible_resources_are_unavailable`
16. `Schedule_rejects_null_required_collections`

使用规格中的 fixture（测试夹具），并至少对注油/密封瓶颈工序断言精确 UTC 时间戳。

- [ ] **步骤 2：运行排程器测试并验证 RED**

运行：

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore --filter FullyQualifiedName~FiniteCapacitySchedulerTests
```

预期：FAIL，因为 `FiniteCapacityScheduler` 尚不存在。

- [ ] **步骤 3：实现最小纯排程器**

创建 `FiniteCapacityScheduler.cs`，其中包含一个 public 方法（公共方法）：

```csharp
public sealed class FiniteCapacityScheduler
{
    public SchedulePlanContract Schedule(SchedulingProblemContract problem, string planId, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var state = SchedulerState.From(problem, planId, generatedAtUtc);
        state.ReserveLockedAssignments();
        state.ScheduleOpenOperations();
        return state.ToPlan();
    }
}
```

实施约束：

1. 不得在算法内部读取时钟；使用 `generatedAtUtc`。
2. 不得调用数据库、HTTP client（客户端）或静态本地时间 API。
3. 使用 ADR 0014 中的确定性排序。
4. P0 中将产能视为每个资源一次执行一道工序，除非 `CapacityUnits` 大于 1。
5. 在输出中保留未排程工序，并附带明确的原因代码。
6. 生成指纹前先规范化。集合顺序不同但语义等效的问题快照必须具有相同指纹。
7. 保留锁定分配，但检测无效锁定，包括超过资源产能的重叠锁定。
8. 当失败模式可区分时，返回明确的 `capacity`、`calendar` 和 `outsideHorizon` 原因代码。
9. 排程前拒绝结构性输入错误：重复资源/日历 ID、同一订单内重复工序 ID、无效时间窗口、非正时长，以及为空/缺失的稳定标识符。
10. P0 变更摘要不计算上一计划的 `moved`；在上一计划快照成为输入契约的一部分前，保留该 enum（枚举）值。
11. 规范化前拒绝 null 顶层集合和嵌套集合，避免格式错误的 JSON payload（载荷）变为未分类的运行时失败。
12. 当所有其他方面合格的资源都被排程时域内的不可用窗口阻塞时，返回 `equipment`。

- [ ] **步骤 4：运行排程器测试并验证 GREEN**

运行：

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore --filter FullyQualifiedName~FiniteCapacitySchedulerTests
```

预期：PASS。

## Task 4：增加领域生命周期、持久化与事件

- [ ] **步骤 1：编写会失败的 aggregate 与事件测试**

创建测试以证明：

1. 已生成计划可以下达一次。
2. 已下达计划不能重新生成或变更。
3. 计划存储 `problemFingerprint`、`algorithmVersion` 和状态。
4. 事件名称必须精确为 `scheduling.SchedulePlanGenerated`、`scheduling.ScheduleConflictDetected` 和 `scheduling.SchedulePlanReleased`。

- [ ] **步骤 2：实现 aggregate 与事件 converter**

实现 `SchedulePlan` aggregate 与集成事件 converter。新 ID 使用 `Guid.CreateVersion7()`。若现有服务模式如此处理，则将 public ID 与 EF key 分开存储。

- [ ] **步骤 3：配置 schema 与 migration**

配置 `scheduling` schema、规格中的表及 migration history：

```csharp
options.MigrationsHistoryTable("__EFMigrationsHistory", "scheduling");
```

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialSchedulingSchema --project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Nerv.IIP.Business.Scheduling.Infrastructure.csproj --startup-project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj --output-dir Migrations
```

- [ ] **步骤 4：运行聚焦的 domain/web 测试**

运行：

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore --filter FullyQualifiedName~SchedulingIntegrationEventTests
```

预期：两条命令均通过。

## Task 5：增加服务 API 与契约测试

- [ ] **步骤 1：编写会失败的 endpoint 契约测试**

创建 `SchedulingEndpointContractTests.cs`，覆盖：

1. 预览、创建、列表、详情、Gantt 和下达 endpoint 的 route 与 operation ID。
2. 每个 endpoint 均要求 `InternalServiceAuthorizationPolicy`。
3. 权限元数据包含 `business.scheduling.plans.read`、`business.scheduling.plans.manage` 或 `business.scheduling.plans.release`。
4. 预览为减震器 fixture 返回确定性计划，且不持久化下达状态。
5. 创建操作持久化已生成计划，详情返回分配/冲突。
6. 下达操作将状态改为已下达，且对同一计划的重复下达具有幂等性。
7. 不同 `organizationId`/`environmentId` 中的同一 `problemId` 创建独立快照和计划。
8. 当请求的组织/环境与持久化计划不匹配时，详情、Gantt 和下达拒绝泄漏的 `planId`。

- [ ] **步骤 2：实现 endpoint、command 与 query**

将 FastEndpoints 放入 `Endpoints/Scheduling/SchedulingEndpoints.cs`。不得在 `Program.cs` 中映射 Minimal API route。保持 request/response DTO 与 `Nerv.IIP.Contracts.Scheduling` 对齐，并暴露稳定 operation ID。计划详情、Gantt 和下达 endpoint 必须将 `organizationId` 与 `environmentId` query 参数传入应用 query/command，不得仅按 `planId` 查找。

- [ ] **步骤 3：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore
```

预期：Scheduling Web 测试通过。

## Task 6：注册共享平台界面

- [ ] **步骤 1：注册 AppHost 与 solution 依赖**

将 BusinessScheduling 加入 `infra/aspire/Nerv.IIP.AppHost/Program.cs`，使用当前位于 BusinessGateway `5119` 与 BusinessConsole `5125` 之间的空闲端口 `5120`。遵循现有业务服务注册风格，并保持默认 Development messaging 为 InMemory。

- [ ] **步骤 2：更新 IAM 种子与授权文档**

增加权限代码：

```text
business.scheduling.plans.read
business.scheduling.plans.manage
business.scheduling.plans.release
```

更新 `docs/architecture/authorization-matrix.md` 及其他业务权限使用的 IAM 种子位置。

- [ ] **步骤 3：更新 schema 目录与就绪文档**

将 `scheduling` schema 与表加入 `docs/architecture/database-schema-catalog.md`。在 `docs/architecture/implementation-readiness.md` 中更新 #206 状态、服务端口、验证命令和当前限制。

- [ ] **步骤 4：增加验证脚本**

创建 `scripts/verify-business-scheduling-aps-lite.ps1`。以 dot-source 引入 `scripts/lib/ScriptAutomation.ps1`，声明脚本分类元数据，并使用 `Invoke-DotNet` 等 helper；不得在脚本主体中直接调用原生 `dotnet`。

## Task 7：为 Gantt 消费者增加 BusinessGateway Facade

- [ ] **步骤 1：增加 Scheduling client 注册**

修改 `BusinessServiceClients.cs` 以注册 Scheduling HTTP client。对创建/下达调用使用适用于非幂等操作的安全韧性策略；若本地模式已拆分读写 client，则沿用该模式。

- [ ] **步骤 2：增加 facade endpoint 测试**

测试 `/api/business-console/v1/scheduling/plans/preview`、`/plans`、`/plans/{planId}`、`/plans/{planId}/gantt` 和 `/plans/{planId}/release` 是否强制执行 IAM 权限检查，并代理稳定 DTO，且不在 Gateway 中增加排程规则。三个带计划 ID 的 route 必须将 `organizationId` 和 `environmentId` 转发给 BusinessScheduling；BusinessGateway OpenAPI 必须将排程 enum 暴露为与 `SchedulingJson.Options` 匹配的 camel-case 字符串。

- [ ] **步骤 3：实现 facade endpoint**

创建 `BusinessConsoleSchedulingEndpoints.cs`。它可以转换页面级 route 并转发 bearer/context/internal service token，但不得持久化排程事实或计算分配结果。

- [ ] **步骤 4：运行 Gateway 测试**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

预期：BusinessGateway 测试通过。

## Task 8：最终验证

- [ ] **步骤 1：运行聚焦验证**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj --no-restore
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

预期：所有命令均通过。

- [ ] **步骤 2：运行受治理脚本检查**

运行：

```powershell
pwsh scripts/check-script-governance.ps1
pwsh scripts/verify-business-scheduling-aps-lite.ps1
```

预期：两条命令均通过。如果后续增加依赖 Docker 的检查且 Docker 不可用，必须明确报告跳过情况。

- [ ] **步骤 3：聚焦测试通过后运行后端 build**

运行：

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期：build 通过且没有新增 warning。
