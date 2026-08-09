# 第二阶段纵切低风险运维实施计划

> **面向智能体执行者：** 必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 建立第二条纵切：Gateway 创建低风险 restart 运维任务，Ops 记录任务、尝试与审计，Connector Host 领取并执行动作，Ops 接收结果，AppHub 继续只通过 state snapshot 表达实例最终状态。

**架构：** 本阶段采用 HTTP pull 作为本地纵切传输机制：Connector Host 通过 `Sdk.Ops` 轮询 Ops 的 pending task endpoint，执行 `lifecycle.restart` 后回传 `OperationResult`。Ops 是动作生命周期与审计事实源；Gateway 只提供控制台入口；AppHub 不接收动作结果，也不被 Ops 直接改写实例状态。

**技术栈：** .NET 10、ASP.NET Core、FastEndpoints、xUnit、Microsoft.AspNetCore.Mvc.Testing、Platform SDK Core/Auth/ConnectorProtocol/Ops、本地 in-memory stores、PowerShell verification scripts。

---

## 执行状态

已于 2026-05-15 在此 PR 中完成。

1. 范围内的全部实施项均已具备：AppHub endpoint 测试、Ops 契约和 SDK、Ops 任务/结果/审计 endpoint、Gateway 重启/详情 facade、Connector Host 操作循环、Docker 重启执行器，以及第二阶段验证脚本。
2. 最终验证已通过 `pwsh scripts/verify-second-slice-ops.ps1`；观察到的结果为：`Second vertical slice verified with operationTaskId op-000001.`
3. 下面的任务章节继续作为执行步骤保留。此分支将已完成工作打包到一个 PR 中，而不是按原始执行者指令中描述的逐任务提交。

## 第一阶段门禁

2026-05-15 重新运行验证：

```powershell
pwsh scripts/verify-first-slice.ps1
```

观察结果：

```text
backend restore/build/test: exit 0
connector-hosts restore/build/test: exit 0
First vertical slice verified with correlationId corr-first-slice.
```

根据本地纵切验收标准，未发现阻塞性的第一阶段遗漏。测试输出中仍可见一个质量缺口：`Nerv.IIP.AppHub.Web.Tests` 当前没有可发现的测试。本计划将此列为首个任务，使下一阶段从更干净的基线开始。

## 范围

### 本计划范围内

1. 为第一阶段纵切补充 AppHub Web endpoint 测试。
2. 添加 `Nerv.IIP.Contracts.Ops` 公开 DTO 和 `Nerv.IIP.Sdk.Ops` HTTP 客户端。
3. 实施 Ops 内存态操作任务、尝试、结果和审计事实。
4. 添加 Ops endpoint：
   - `POST /api/ops/v1/operation-tasks`
   - `GET /api/ops/v1/operation-tasks/{operationTaskId}`
   - `GET /api/ops/v1/operation-tasks/pending`
   - `POST /api/ops/v1/operation-results`
5. 添加面向 Gateway 控制台的重启和操作详情 endpoint。
6. 为 `lifecycle.restart` 添加 Connector Host 操作执行循环。
7. 添加 `scripts/verify-second-slice-ops.ps1`，验证本地端到端重启任务生命周期。

### 本计划范围外

1. 高风险审批和人工确认 UI。
2. 停止、备份、恢复、日志拉取和批量操作。
3. IAM/AppHub/Ops 的 PostgreSQL/CAP 持久化迁移。
4. 完整控制台 UI 和生成的前端 API 客户端。
5. 操作成功/失败的通知消息。

## 文件结构图

```text
backend/
  common/
    Contracts/
      Nerv.IIP.Contracts.Ops/
        Nerv.IIP.Contracts.Ops.csproj
        OpsContracts.cs
    Sdk/
      Nerv.IIP.Sdk.Ops/
        Nerv.IIP.Sdk.Ops.csproj
        OpsClient.cs
  tests/
    Nerv.IIP.Contracts.Ops.Tests/
      Nerv.IIP.Contracts.Ops.Tests.csproj
      OpsContractJsonTests.cs
  services/
    AppHub/tests/Nerv.IIP.AppHub.Web.Tests/
      AppHubConnectorEndpointTests.cs
    Ops/
      src/Nerv.IIP.Ops.Domain/
        OperationFacts.cs
        InMemoryOpsStateStore.cs
      src/Nerv.IIP.Ops.Web/
        Endpoints/OperationTasks/OperationTaskEndpoints.cs
        Program.cs
      tests/Nerv.IIP.Ops.Web.Tests/
        OperationTaskEndpointTests.cs
  gateway/
    PlatformGateway/
      src/Nerv.IIP.PlatformGateway.Web/
        Application/OpsClient/OpsClient.cs
        Endpoints/Operations/OperationEndpoints.cs
        Program.cs
      tests/Nerv.IIP.PlatformGateway.Web.Tests/
        GatewayOperationTests.cs

connector-hosts/
  src/
    Nerv.IIP.ConnectorHost.Connectors.Abstractions/
      ConnectorOperationAbstractions.cs
    Nerv.IIP.ConnectorHost.Connectors.Docker/
      DockerConnector.cs
    Nerv.IIP.ConnectorHost.Application/
      ConnectorOperationLoop.cs
    Nerv.IIP.ConnectorHost.Host/
      Program.cs
      Worker.cs
  tests/
    Nerv.IIP.ConnectorHost.Application.Tests/
      OperationLoopTests.cs
    Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/
      DockerConnectorOperationTests.cs

scripts/
  verify-second-slice-ops.ps1
```

## 边界规则

1. Ops 拥有 `OperationTask`、`OperationAttempt`、`AuditRecord` 和操作结果状态。
2. AppHub 拥有实例事实，并且只通过注册、心跳和状态快照更改它们。
3. Gateway 不引用 `Nerv.IIP.Ops.Domain` 或 `Nerv.IIP.Ops.Infrastructure`。
4. Connector Host 不引用后端服务的 Web、Domain 或 Infrastructure 项目。
5. `Sdk.Ops` 只依赖公开契约、`Sdk.Core`、`Sdk.Auth` 和 Connector Protocol 结果 DTO。
6. `lifecycle.restart` 是本计划唯一可执行的操作。
7. 待处理任务轮询是隐藏在 `Sdk.Ops` 后的本地 v1 HTTP 契约；调用方不得依赖 Ops 内部领域对象。
8. 平台 HTTP endpoint 继续使用 FastEndpoints，路由类位于 `Endpoints/**` 下。

---

## 任务 1：补充 AppHub Web Endpoint 测试

**文件：**

- 创建：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubConnectorEndpointTests.cs`

- [ ] **步骤 1：编写 endpoint 测试**

创建 `AppHubConnectorEndpointTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubConnectorEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Connector_ingestion_requires_local_connector_credential()
    {
        var client = factory.CreateClient();
        var registration = CreateRegistration("missing-auth-001");

        using var response = await client.PostAsJsonAsync("/api/connectors/v1/registrations", registration);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Connector_registration_heartbeat_and_state_are_queryable()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", "connector-host-001");
        client.DefaultRequestHeaders.Add("X-Connector-Secret", "local-connector-secret");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-apphub-web-test");

        await client.PostAsJsonAsync("/api/connectors/v1/registrations", CreateRegistration("web-test-001"));
        await client.PostAsJsonAsync("/api/connectors/v1/heartbeats", CreateHeartbeat());
        await client.PostAsJsonAsync("/api/connectors/v1/state-snapshots", CreateSnapshot());

        var query = new InstanceListQuery("org-001", "env-dev", 1, 20, null);
        var list = await client.PostAsJsonAsync("/internal/apphub/v1/instances/query", query);
        var listBody = await list.Content.ReadFromJsonAsync<InstanceListResponse>();
        var detail = await client.GetFromJsonAsync<InstanceDetailResponse>("/internal/apphub/v1/instances/demo-api-001?organizationId=org-001&environmentId=env-dev");

        Assert.NotNull(listBody);
        Assert.Equal(1, listBody.TotalCount);
        Assert.NotNull(detail);
        Assert.Equal("demo-api-001", detail.InstanceKey);
        Assert.Equal("running", detail.ReportedStatus);
        Assert.Equal("healthy", detail.HealthStatus);
    }

    private static ConnectorRequestContext Context() => new("1.0", "1.0", "corr-apphub-web-test", DateTimeOffset.Parse("2026-05-15T00:00:00Z"), "org-001", "env-dev", "connector-host-001");

    private static ApplicationRegistration CreateRegistration(string idempotencyKey) =>
        new(
            Context(),
            idempotencyKey,
            "node-001",
            "local-docker",
            "docker",
            "demo-api",
            "Demo API",
            "1.0.0",
            "demo-api-001",
            "demo-api",
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())],
            new Dictionary<string, string> { ["containerId"] = "local-demo-001" });

    private static ApplicationHeartbeat CreateHeartbeat() =>
        new(Context(), "demo-api-001", DateTimeOffset.Parse("2026-05-15T00:00:05Z"), true, DateTimeOffset.Parse("2026-05-15T00:00:00Z"), 7, new Dictionary<string, string>());

    private static InstanceStateSnapshot CreateSnapshot() =>
        new(Context(), "demo-api-001", DateTimeOffset.Parse("2026-05-15T00:00:10Z"), "running", "healthy", "demo-api is running", new Dictionary<string, string>(), new Dictionary<string, decimal>(), new Dictionary<string, string> { ["containerId"] = "local-demo-001" });
}
```

- [ ] **步骤 2：运行 AppHub Web 测试**

运行：

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj
```

预期结果：退出代码为 `0`，发现并通过 `2` 个测试。

- [ ] **步骤 3：提交**

运行：

```powershell
git add backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests
git commit -m "test: cover apphub connector endpoints"
```

## 任务 2：添加 Ops 公开契约和 SDK 客户端

**文件：**

- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.Ops/OpsContracts.cs`
- 创建：`backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Contracts.Ops.Tests/OpsContractJsonTests.cs`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Ops/OpsClient.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：编写契约序列化测试**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.Ops -o backend/common/Contracts/Nerv.IIP.Contracts.Ops --framework net10.0
dotnet new xunit -n Nerv.IIP.Contracts.Ops.Tests -o backend/tests/Nerv.IIP.Contracts.Ops.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj
```

创建 `OpsContractJsonTests.cs`：

```csharp
using System.Text.Json;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Contracts.Ops.Tests;

public sealed class OpsContractJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Operation_task_response_round_trips_with_web_json_options()
    {
        var source = new OperationTaskResponse(
            "op-000001",
            "org-001",
            "env-dev",
            "docker-container-local-demo-001",
            "lifecycle.restart",
            "completed",
            "local-admin",
            DateTimeOffset.Parse("2026-05-15T00:00:00Z"),
            "attempt-000001",
            [new OperationAttemptSummary("attempt-000001", "completed", DateTimeOffset.Parse("2026-05-15T00:00:01Z"), DateTimeOffset.Parse("2026-05-15T00:00:02Z"), null)],
            [new AuditRecordSummary("audit-000001", "op-000001", "operation.completed", "connector-host-001", DateTimeOffset.Parse("2026-05-15T00:00:02Z"), "corr-ops-001")]);

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<OperationTaskResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("op-000001", result.OperationTaskId);
        Assert.Equal("completed", result.Status);
        Assert.Equal("operation.completed", result.AuditRecords.Single().Action);
    }
}
```

- [ ] **步骤 2：添加 Ops DTO**

创建 `OpsContracts.cs`：

```csharp
namespace Nerv.IIP.Contracts.Ops;

public sealed record CreateOperationTaskRequest(
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string IdempotencyKey,
    string RequestedBy,
    string Reason,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record OperationTaskResponse(
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? CurrentAttemptId,
    IReadOnlyList<OperationAttemptSummary> Attempts,
    IReadOnlyList<AuditRecordSummary> AuditRecords);

public sealed record OperationAttemptSummary(
    string AttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? FailureCode);

public sealed record AuditRecordSummary(
    string AuditRecordId,
    string OperationTaskId,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

public sealed record PendingOperationTasksResponse(IReadOnlyList<OperationTaskDispatchItem> Items);

public sealed record OperationTaskDispatchItem(
    string OperationTaskId,
    string AttemptId,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId,
    string InstanceKey,
    string OperationCode,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Parameters);
```

- [ ] **步骤 3：运行契约测试并验证在添加 SDK 工作前的失败状态**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj
```

预期结果：退出代码为 `0`，`1` 个测试通过。

- [ ] **步骤 4：添加 Sdk.Ops 项目和客户端**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Sdk.Ops -o backend/common/Sdk/Nerv.IIP.Sdk.Ops --framework net10.0
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj
```

创建 `OpsClient.cs`：

```csharp
using System.Net.Http.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Auth;

namespace Nerv.IIP.Sdk.Ops;

public interface IOpsClient
{
    Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default);
    Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default);
    Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default);
    Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default);
}

public sealed class HttpOpsClient(HttpClient httpClient, ConnectorHostCredential? credential = null) : IOpsClient
{
    public async Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/ops/v1/operation-tasks", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OperationTaskResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ops returned an empty operation task response.");
    }

    public async Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<OperationTaskResponse>($"/api/ops/v1/operation-tasks/{operationTaskId}", cancellationToken)
            ?? throw new InvalidOperationException("Ops returned an empty operation task response.");
    }

    public async Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/ops/v1/operation-tasks/pending?organizationId={Uri.EscapeDataString(organizationId)}&environmentId={Uri.EscapeDataString(environmentId)}&connectorHostId={Uri.EscapeDataString(connectorHostId)}&take={take}");
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PendingOperationTasksResponse>(cancellationToken: cancellationToken)
            ?? new PendingOperationTasksResponse([]);
    }

    public async Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ops/v1/operation-results") { Content = JsonContent.Create(result) };
        ApplyCredential(request);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private void ApplyCredential(HttpRequestMessage request)
    {
        if (credential is not null)
        {
            ConnectorHostAuthentication.Apply(request, credential);
        }
    }
}
```

- [ ] **步骤 5：构建契约和 SDK 项目**

运行：

```powershell
dotnet build backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj
```

预期结果：两条命令都以代码 `0` 退出。

- [ ] **步骤 6：提交**

运行：

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.Ops backend/common/Sdk/Nerv.IIP.Sdk.Ops backend/tests/Nerv.IIP.Contracts.Ops.Tests backend/Nerv.IIP.sln
git commit -m "feat: add ops contracts and sdk client"
```

## 任务 3：实施 Ops 任务和审计 Endpoint

**文件：**

- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Domain/OperationFacts.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Domain/InMemoryOpsStateStore.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/OperationTasks/OperationTaskEndpoints.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- 创建：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs`

- [ ] **步骤 1：编写 endpoint 测试**

创建 `OperationTaskEndpointTests.cs`：

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OperationTaskEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Operation_task_can_be_created_dispatched_and_completed()
    {
        var client = factory.CreateClient();
        var create = new CreateOperationTaskRequest("org-001", "env-dev", "docker-container-local-demo-001", "lifecycle.restart", "idem-restart-001", "local-admin", "manual smoke restart", "corr-ops-test", new Dictionary<string, string>());

        var created = await (await client.PostAsJsonAsync("/api/ops/v1/operation-tasks", create)).Content.ReadFromJsonAsync<OperationTaskResponse>();
        Assert.NotNull(created);
        Assert.Equal("queued", created.Status);
        Assert.Contains(created.AuditRecords, x => x.Action == "operation.requested");

        client.DefaultRequestHeaders.Add("X-Connector-Host-Id", "connector-host-001");
        client.DefaultRequestHeaders.Add("X-Connector-Secret", "local-connector-secret");
        var pending = await client.GetFromJsonAsync<PendingOperationTasksResponse>("/api/ops/v1/operation-tasks/pending?organizationId=org-001&environmentId=env-dev&connectorHostId=connector-host-001&take=10");
        var dispatch = Assert.Single(pending!.Items);

        await client.PostAsJsonAsync("/api/ops/v1/operation-results", new OperationResult(
            new ConnectorRequestContext("1.0", "1.0", "corr-ops-test", DateTimeOffset.Parse("2026-05-15T00:00:02Z"), "org-001", "env-dev", "connector-host-001"),
            dispatch.OperationTaskId,
            dispatch.AttemptId,
            "docker-container-local-demo-001",
            "lifecycle.restart",
            DateTimeOffset.Parse("2026-05-15T00:00:01Z"),
            DateTimeOffset.Parse("2026-05-15T00:00:02Z"),
            "succeeded",
            null,
            new Dictionary<string, string> { ["message"] = "restart accepted" }));

        var completed = await client.GetFromJsonAsync<OperationTaskResponse>($"/api/ops/v1/operation-tasks/{created.OperationTaskId}");
        Assert.Equal("completed", completed!.Status);
        Assert.Contains(completed.AuditRecords, x => x.Action == "operation.dispatched");
        Assert.Contains(completed.AuditRecords, x => x.Action == "operation.completed");
    }
}
```

- [ ] **步骤 2：运行测试并验证其失败**

运行：

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter Operation_task_can_be_created_dispatched_and_completed
```

预期结果：失败，返回 `404` 或报告 endpoint/store 类型缺失。

- [ ] **步骤 3：添加 Ops 事实**

创建 `OperationFacts.cs`：

```csharp
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Ops.Domain;

public sealed record OperationTaskFact(
    string OperationTaskId,
    string OrganizationId,
    string EnvironmentId,
    string InstanceKey,
    string OperationCode,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string IdempotencyKey,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record OperationAttemptFact(
    string AttemptId,
    string OperationTaskId,
    string ConnectorHostId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    FailureReason? Failure);

public sealed record AuditRecordFact(
    string AuditRecordId,
    string OperationTaskId,
    string Action,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId);

public static class OperationTaskMapper
{
    public static OperationTaskResponse ToResponse(OperationTaskFact task, IReadOnlyList<OperationAttemptFact> attempts, IReadOnlyList<AuditRecordFact> auditRecords)
    {
        return new OperationTaskResponse(
            task.OperationTaskId,
            task.OrganizationId,
            task.EnvironmentId,
            task.InstanceKey,
            task.OperationCode,
            task.Status,
            task.RequestedBy,
            task.RequestedAtUtc,
            attempts.LastOrDefault()?.AttemptId,
            attempts.Select(x => new OperationAttemptSummary(x.AttemptId, x.Status, x.StartedAtUtc, x.FinishedAtUtc, x.Failure?.Code)).ToList(),
            auditRecords.Select(x => new AuditRecordSummary(x.AuditRecordId, x.OperationTaskId, x.Action, x.Actor, x.OccurredAtUtc, x.CorrelationId)).ToList());
    }
}
```

- [ ] **步骤 4：添加内存态 Ops 状态存储**

创建 `InMemoryOpsStateStore.cs`：

```csharp
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.Ops.Domain;

public sealed class InMemoryOpsStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _idempotency = new(StringComparer.Ordinal);
    private readonly List<OperationTaskFact> _tasks = [];
    private readonly List<OperationAttemptFact> _attempts = [];
    private readonly List<AuditRecordFact> _auditRecords = [];

    public OperationTaskResponse Create(CreateOperationTaskRequest request, DateTimeOffset now)
    {
        if (!string.Equals(request.OperationCode, "lifecycle.restart", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported operation code: {request.OperationCode}");
        }

        lock (_gate)
        {
            if (_idempotency.TryGetValue(request.IdempotencyKey, out var existingId))
            {
                return Get(existingId);
            }

            var taskId = $"op-{_tasks.Count + 1:000000}";
            var task = new OperationTaskFact(taskId, request.OrganizationId, request.EnvironmentId, request.InstanceKey, request.OperationCode, "queued", request.RequestedBy, now, request.IdempotencyKey, request.CorrelationId, request.Parameters);
            _tasks.Add(task);
            _idempotency[request.IdempotencyKey] = taskId;
            AddAudit(taskId, "operation.requested", request.RequestedBy, now, request.CorrelationId);
            return Get(taskId);
        }
    }

    public OperationTaskResponse Get(string operationTaskId)
    {
        lock (_gate)
        {
            var task = _tasks.Single(x => x.OperationTaskId == operationTaskId);
            return OperationTaskMapper.ToResponse(task, _attempts.Where(x => x.OperationTaskId == operationTaskId).ToList(), _auditRecords.Where(x => x.OperationTaskId == operationTaskId).ToList());
        }
    }

    public PendingOperationTasksResponse DispatchPending(string organizationId, string environmentId, string connectorHostId, int take, DateTimeOffset now)
    {
        lock (_gate)
        {
            var queued = _tasks
                .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Status == "queued")
                .Take(Math.Clamp(take, 1, 50))
                .ToList();

            var items = new List<OperationTaskDispatchItem>();
            foreach (var task in queued)
            {
                var attemptId = $"attempt-{_attempts.Count + 1:000000}";
                ReplaceTask(task with { Status = "dispatched" });
                _attempts.Add(new OperationAttemptFact(attemptId, task.OperationTaskId, connectorHostId, "started", now, null, null));
                AddAudit(task.OperationTaskId, "operation.dispatched", connectorHostId, now, task.CorrelationId);
                items.Add(new OperationTaskDispatchItem(task.OperationTaskId, attemptId, task.OrganizationId, task.EnvironmentId, connectorHostId, task.InstanceKey, task.OperationCode, task.CorrelationId, task.Parameters));
            }

            return new PendingOperationTasksResponse(items);
        }
    }

    public OperationTaskResponse RecordResult(OperationResult result)
    {
        lock (_gate)
        {
            var task = _tasks.Single(x => x.OperationTaskId == result.OperationTaskId);
            var attempt = _attempts.Single(x => x.OperationTaskId == result.OperationTaskId && x.AttemptId == result.AttemptId);
            var completed = string.Equals(result.ExecutionStatus, "succeeded", StringComparison.OrdinalIgnoreCase);
            ReplaceAttempt(attempt with { Status = completed ? "completed" : "failed", FinishedAtUtc = result.FinishedAtUtc, Failure = result.Failure });
            ReplaceTask(task with { Status = completed ? "completed" : "failed" });
            AddAudit(task.OperationTaskId, completed ? "operation.completed" : "operation.failed", result.Context.ConnectorHostId, result.FinishedAtUtc, result.Context.CorrelationId);
            return Get(task.OperationTaskId);
        }
    }

    private void ReplaceTask(OperationTaskFact task)
    {
        var index = _tasks.FindIndex(x => x.OperationTaskId == task.OperationTaskId);
        _tasks[index] = task;
    }

    private void ReplaceAttempt(OperationAttemptFact attempt)
    {
        var index = _attempts.FindIndex(x => x.AttemptId == attempt.AttemptId);
        _attempts[index] = attempt;
    }

    private void AddAudit(string taskId, string action, string actor, DateTimeOffset now, string correlationId)
    {
        _auditRecords.Add(new AuditRecordFact($"audit-{_auditRecords.Count + 1:000000}", taskId, action, actor, now, correlationId));
    }
}
```

- [ ] **步骤 5：添加 Ops endpoint**

创建 `OperationTaskEndpoints.cs`：

```csharp
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;

namespace Nerv.IIP.Ops.Web.Endpoints.OperationTasks;

[HttpPost("/api/ops/v1/operation-tasks")]
[AllowAnonymous]
public sealed class CreateOperationTaskEndpoint(InMemoryOpsStateStore store) : Endpoint<CreateOperationTaskRequest>
{
    public override async Task HandleAsync(CreateOperationTaskRequest req, CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(store.Create(req, DateTimeOffset.UtcNow), ct);
    }
}

[HttpGet("/api/ops/v1/operation-tasks/{operationTaskId}")]
[AllowAnonymous]
public sealed class GetOperationTaskEndpoint(InMemoryOpsStateStore store) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await HttpContext.Response.WriteAsJsonAsync(store.Get(Route<string>("operationTaskId")!), ct);
    }
}

[HttpGet("/api/ops/v1/operation-tasks/pending")]
[AllowAnonymous]
public sealed class GetPendingOperationTasksEndpoint(InMemoryOpsStateStore store) : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var connectorHostId = Query<string>("connectorHostId")!;
        if (!OpsConnectorAuth.ConnectorHostAuthorized(HttpContext, connectorHostId))
        {
            await OpsConnectorAuth.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        var response = store.DispatchPending(Query<string>("organizationId")!, Query<string>("environmentId")!, connectorHostId, Query<int>("take", false), DateTimeOffset.UtcNow);
        await HttpContext.Response.WriteAsJsonAsync(response, ct);
    }
}

[HttpPost("/api/ops/v1/operation-results")]
[AllowAnonymous]
public sealed class SubmitOperationResultEndpoint(InMemoryOpsStateStore store) : Endpoint<OperationResult>
{
    public override async Task HandleAsync(OperationResult req, CancellationToken ct)
    {
        if (!OpsConnectorAuth.ConnectorHostAuthorized(HttpContext, req.Context.ConnectorHostId))
        {
            await OpsConnectorAuth.WriteUnauthorizedAsync(HttpContext, ct);
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(store.RecordResult(req), ct);
    }
}

internal static class OpsConnectorAuth
{
    public static bool ConnectorHostAuthorized(HttpContext context, string connectorHostId)
    {
        return context.Request.Headers.TryGetValue("X-Connector-Host-Id", out var hostId)
            && context.Request.Headers.TryGetValue("X-Connector-Secret", out var secret)
            && hostId == connectorHostId
            && secret == "local-connector-secret";
    }

    public static async Task WriteUnauthorizedAsync(HttpContext context, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { title = "Unauthorized", detail = "Invalid Connector Host credential.", status = StatusCodes.Status401Unauthorized }, cancellationToken);
    }
}
```

修改 `Program.cs`：

```csharp
using FastEndpoints;
using Nerv.IIP.Observability;
using Nerv.IIP.Ops.Domain;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddFastEndpoints();
builder.Services.AddNervIipObservability(builder.Configuration, "ops");
builder.Services.AddSingleton<InMemoryOpsStateStore>();

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints();
app.Run();

public partial class Program;
```

- [ ] **步骤 6：运行 Ops Web 测试**

运行：

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj
```

预期结果：退出代码为 `0`，现有骨架测试和新的操作生命周期测试均通过。

- [ ] **步骤 7：提交**

运行：

```powershell
git add backend/services/Ops
git commit -m "feat: add ops operation task lifecycle"
```

## 任务 4：添加 Gateway 重启 Facade

**文件：**

- 创建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/OpsClient/OpsClient.cs`
- 创建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Operations/OperationEndpoints.cs`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj`
- 创建：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOperationTests.cs`

- [ ] **步骤 1：编写 Gateway 操作测试**

创建 `GatewayOperationTests.cs`：

```csharp
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;
using Nerv.IIP.PlatformGateway.Web.Endpoints.Operations;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayOperationTests
{
    [Fact]
    public async Task Restart_endpoint_creates_lifecycle_restart_task()
    {
        var fake = new FakeGatewayOpsClient();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGatewayOpsClient>();
                services.AddSingleton<IGatewayOpsClient>(fake);
            }));
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/console/v1/instances/docker-container-local-demo-001/operations/restart", new RestartInstanceRequest("org-001", "env-dev", "smoke restart", "idem-gateway-restart-001"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OperationTaskResponse>();
        Assert.NotNull(body);
        Assert.Equal("docker-container-local-demo-001", body.InstanceKey);
        Assert.Equal("lifecycle.restart", body.OperationCode);
        Assert.Equal("queued", body.Status);
        Assert.Equal("idem-gateway-restart-001", fake.LastRequest!.IdempotencyKey);
        Assert.Equal("smoke restart", fake.LastRequest.Reason);
    }

    private sealed class FakeGatewayOpsClient : IGatewayOpsClient
    {
        public CreateOperationTaskRequest? LastRequest { get; private set; }

        public Task<OperationTaskResponse> CreateTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new OperationTaskResponse("op-000001", request.OrganizationId, request.EnvironmentId, request.InstanceKey, request.OperationCode, "queued", request.RequestedBy, DateTimeOffset.UtcNow, null, [], []));
        }

        public Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new OperationTaskResponse(operationTaskId, "org-001", "env-dev", "docker-container-local-demo-001", "lifecycle.restart", "completed", "local-admin", DateTimeOffset.UtcNow, "attempt-000001", [], []));
        }
    }
}
```

- [ ] **步骤 2：运行测试并验证其失败**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --filter Restart_endpoint_creates_lifecycle_restart_task
```

预期结果：失败，报告缺少 `RestartInstanceRequest` 或路由。

- [ ] **步骤 3：在 Gateway 中添加 Ops 客户端**

创建 `Application/OpsClient/OpsClient.cs`：

```csharp
using System.Net.Http.Json;
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.PlatformGateway.Web.Application.OpsClient;

public interface IGatewayOpsClient
{
    Task<OperationTaskResponse> CreateTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken);
    Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken);
}

public sealed class GatewayOpsClient(HttpClient httpClient) : IGatewayOpsClient
{
    public async Task<OperationTaskResponse> CreateTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/ops/v1/operation-tasks", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OperationTaskResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ops returned an empty operation task response.");
    }

    public async Task<OperationTaskResponse> GetTaskAsync(string operationTaskId, CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<OperationTaskResponse>($"/api/ops/v1/operation-tasks/{operationTaskId}", cancellationToken)
            ?? throw new InvalidOperationException("Ops returned an empty operation task response.");
    }
}
```

修改 `Program.cs` 以注册强类型客户端：

```csharp
builder.Services.AddHttpClient<IGatewayOpsClient, GatewayOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ops:BaseUrl"] ?? "http://localhost:5105");
});
```

- [ ] **步骤 4：添加 Gateway endpoint**

创建 `OperationEndpoints.cs`：

```csharp
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Operations;

public sealed record RestartInstanceRequest(string OrganizationId, string EnvironmentId, string Reason, string IdempotencyKey);

[HttpPost("/api/console/v1/instances/{instanceKey}/operations/restart")]
[AllowAnonymous]
public sealed class RestartInstanceEndpoint(IGatewayOpsClient opsClient) : Endpoint<RestartInstanceRequest, OperationTaskResponse>
{
    public override async Task HandleAsync(RestartInstanceRequest req, CancellationToken ct)
    {
        var operationRequest = new CreateOperationTaskRequest(
            req.OrganizationId,
            req.EnvironmentId,
            Route<string>("instanceKey")!,
            "lifecycle.restart",
            req.IdempotencyKey,
            HttpContext.Request.Headers.TryGetValue("X-User-Id", out var userId) ? userId.ToString() : "local-admin",
            req.Reason,
            HttpContext.TraceIdentifier,
            new Dictionary<string, string>());

        await SendAsync(await opsClient.CreateTaskAsync(operationRequest, ct), cancellation: ct);
    }
}

[HttpGet("/api/console/v1/operation-tasks/{operationTaskId}")]
[AllowAnonymous]
public sealed class GetConsoleOperationTaskEndpoint(IGatewayOpsClient opsClient) : EndpointWithoutRequest<OperationTaskResponse>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendAsync(await opsClient.GetTaskAsync(Route<string>("operationTaskId")!, ct), cancellation: ct);
    }
}
```

- [ ] **步骤 5：添加项目引用**

运行：

```powershell
dotnet add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj
```

- [ ] **步骤 6：运行 Gateway 测试**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
```

预期结果：退出代码为 `0`。Gateway 项目文件仍不得引用 `Nerv.IIP.Ops.Domain` 或 `Nerv.IIP.Ops.Infrastructure`。

- [ ] **步骤 7：提交**

运行：

```powershell
git add backend/gateway/PlatformGateway
git commit -m "feat: add gateway restart operation facade"
```

## 任务 5：添加 Connector Host 操作执行循环

**文件：**

- 创建：`connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/ConnectorOperationAbstractions.cs`
- 修改：`connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker/DockerConnector.cs`
- 创建：`connector-hosts/src/Nerv.IIP.ConnectorHost.Application/ConnectorOperationLoop.cs`
- 修改：`connector-hosts/src/Nerv.IIP.ConnectorHost.Application/Nerv.IIP.ConnectorHost.Application.csproj`
- 修改：`connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`
- 修改：`connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Worker.cs`
- 创建：`connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/OperationLoopTests.cs`
- 创建：`connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/DockerConnectorOperationTests.cs`

- [ ] **步骤 1：添加预期失败的操作循环测试**

创建 `OperationLoopTests.cs`：

```csharp
using Nerv.IIP.ConnectorHost.Application;
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Ops;

namespace Nerv.IIP.ConnectorHost.Application.Tests;

public sealed class OperationLoopTests
{
    [Fact]
    public async Task Operation_loop_executes_pending_restart_and_reports_result()
    {
        var ops = new RecordingOpsClient();
        var executor = new SuccessfulRestartExecutor();
        var loop = new ConnectorOperationLoop([executor], ops, ConnectorHostRuntimeContext.DefaultLocal);

        await loop.RunCycleAsync(CancellationToken.None);

        Assert.Single(ops.Results);
        Assert.Equal("op-000001", ops.Results.Single().OperationTaskId);
        Assert.Equal("succeeded", ops.Results.Single().ExecutionStatus);
    }

    private sealed class SuccessfulRestartExecutor : IConnectorOperationExecutor
    {
        public bool CanExecute(OperationTaskDispatchItem task) => task.OperationCode == "lifecycle.restart";

        public Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken)
        {
            return Task.FromResult(ConnectorOperationExecution.Succeeded(new Dictionary<string, string> { ["message"] = "restart accepted" }));
        }
    }

    private sealed class RecordingOpsClient : IOpsClient
    {
        public List<OperationResult> Results { get; } = [];

        public Task<OperationTaskResponse> CreateOperationTaskAsync(CreateOperationTaskRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationTaskResponse> GetOperationTaskAsync(string operationTaskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PendingOperationTasksResponse> GetPendingOperationTasksAsync(string organizationId, string environmentId, string connectorHostId, int take, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PendingOperationTasksResponse([new OperationTaskDispatchItem("op-000001", "attempt-000001", organizationId, environmentId, connectorHostId, "docker-container-local-demo-001", "lifecycle.restart", "corr-op-loop-test", new Dictionary<string, string>())]));
        }

        public Task SendOperationResultAsync(OperationResult result, CancellationToken cancellationToken = default)
        {
            Results.Add(result);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **步骤 2：运行测试并验证其失败**

运行：

```powershell
dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/Nerv.IIP.ConnectorHost.Application.Tests.csproj --filter Operation_loop_executes_pending_restart_and_reports_result
```

预期结果：失败，报告缺少操作抽象或 `ConnectorOperationLoop`。

- [ ] **步骤 3：添加操作执行器抽象**

创建 `ConnectorOperationAbstractions.cs`：

```csharp
using Nerv.IIP.Contracts.Ops;

namespace Nerv.IIP.ConnectorHost.Connectors.Abstractions;

public interface IConnectorOperationExecutor
{
    bool CanExecute(OperationTaskDispatchItem task);
    Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken);
}

public sealed record ConnectorOperationExecution(
    bool Succeeded,
    string? FailureCode,
    string? FailureMessage,
    string? FailureCategory,
    bool Retryable,
    IReadOnlyDictionary<string, string> Output)
{
    public static ConnectorOperationExecution Succeeded(IReadOnlyDictionary<string, string> output) => new(true, null, null, null, false, output);
    public static ConnectorOperationExecution Failed(string code, string message, string category, bool retryable, IReadOnlyDictionary<string, string> output) => new(false, code, message, category, retryable, output);
}
```

- [ ] **步骤 4：添加 Connector 操作循环**

创建 `ConnectorOperationLoop.cs`：

```csharp
using Nerv.IIP.ConnectorHost.Connectors.Abstractions;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Sdk.Ops;

namespace Nerv.IIP.ConnectorHost.Application;

public sealed class ConnectorOperationLoop(
    IReadOnlyList<IConnectorOperationExecutor> executors,
    IOpsClient opsClient,
    ConnectorHostRuntimeContext runtimeContext)
{
    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var pending = await opsClient.GetPendingOperationTasksAsync(runtimeContext.OrganizationId, runtimeContext.EnvironmentId, runtimeContext.ConnectorHostId, 10, cancellationToken);
        foreach (var task in pending.Items)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var execution = await ExecuteAsync(task, cancellationToken);
            var finishedAt = DateTimeOffset.UtcNow;
            var context = new ConnectorRequestContext(runtimeContext.ProtocolVersion, runtimeContext.SdkVersion, task.CorrelationId, finishedAt, task.OrganizationId, task.EnvironmentId, runtimeContext.ConnectorHostId);
            var failure = execution.Succeeded ? null : new FailureReason(execution.FailureCode ?? "operation.failed", execution.FailureMessage ?? "Operation failed.", execution.FailureCategory ?? "runtime", execution.Retryable, new Dictionary<string, string>());
            var result = new OperationResult(context, task.OperationTaskId, task.AttemptId, task.InstanceKey, task.OperationCode, startedAt, finishedAt, execution.Succeeded ? "succeeded" : "failed", failure, execution.Output);
            await opsClient.SendOperationResultAsync(result, cancellationToken);
        }
    }

    private async Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken)
    {
        var executor = executors.FirstOrDefault(x => x.CanExecute(task));
        if (executor is null)
        {
            return ConnectorOperationExecution.Failed("operation.unsupported", $"No connector can execute {task.OperationCode} for {task.InstanceKey}.", "validation", false, new Dictionary<string, string>());
        }

        return await executor.ExecuteAsync(task, cancellationToken);
    }
}
```

- [ ] **步骤 5：添加 Docker 重启执行器行为**

修改 `DockerConnector.cs`，使该类实施 `IConnectorOperationExecutor`：

```csharp
public sealed class DockerConnector(IReadOnlyList<DockerContainerDescriptor>? containers = null) : IConnector, IConnectorOperationExecutor
{
    private readonly IReadOnlyList<DockerContainerDescriptor> _containers = containers ?? [];

    public bool CanExecute(OperationTaskDispatchItem task)
    {
        return task.OperationCode == "lifecycle.restart" && _containers.Any(container => $"docker-container-{container.ContainerId}" == task.InstanceKey);
    }

    public Task<ConnectorOperationExecution> ExecuteAsync(OperationTaskDispatchItem task, CancellationToken cancellationToken)
    {
        if (!CanExecute(task))
        {
            return Task.FromResult(ConnectorOperationExecution.Failed("docker.container.not_found", $"Container for {task.InstanceKey} was not found.", "validation", false, new Dictionary<string, string>()));
        }

        return Task.FromResult(ConnectorOperationExecution.Succeeded(new Dictionary<string, string>
        {
            ["message"] = "restart accepted",
            ["instanceKey"] = task.InstanceKey
        }));
    }

    // Keep the existing DiscoverAsync and Map methods below this point.
}
```

- [ ] **步骤 6：接入宿主服务和循环时序**

修改 `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`：

```csharp
builder.Services.AddSingleton<DockerConnector>(_ => new DockerConnector([
    new DockerContainerDescriptor("local-demo-001", "nerv/demo-api:1.0.0", "demo-api", "running")
]));
builder.Services.AddSingleton<IConnector>(sp => sp.GetRequiredService<DockerConnector>());
builder.Services.AddSingleton<IConnectorOperationExecutor>(sp => sp.GetRequiredService<DockerConnector>());
builder.Services.AddHttpClient<IOpsClient, HttpOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Platform:OpsBaseUrl"] ?? "http://localhost:5105");
});
builder.Services.AddSingleton<ConnectorOperationLoop>();
```

修改 `Worker.cs`，使每个循环都运行上报和操作：

```csharp
public class Worker(ILogger<Worker> logger, Application.ConnectorReportingLoop reportingLoop, Application.ConnectorOperationLoop operationLoop, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cycleSeconds = configuration.GetValue("ConnectorHost:CycleSeconds", 30);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await reportingLoop.RunCycleAsync(stoppingToken);
                await operationLoop.RunCycleAsync(stoppingToken);
                logger.LogInformation("Connector Host cycle completed at {time}", DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Connector Host cycle failed and will be retried.");
            }

            await Task.Delay(TimeSpan.FromSeconds(cycleSeconds), stoppingToken);
        }
    }
}
```

- [ ] **步骤 7：为 connector-hosts 添加 SDK 引用**

运行：

```powershell
dotnet add connector-hosts/src/Nerv.IIP.ConnectorHost.Application/Nerv.IIP.ConnectorHost.Application.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj
dotnet add connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/Nerv.IIP.ConnectorHost.Connectors.Abstractions.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj
```

- [ ] **步骤 8：运行 connector-host 测试**

运行：

```powershell
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
```

预期结果：退出代码为 `0`，包括操作循环测试和 Docker 操作测试。

- [ ] **步骤 9：提交**

运行：

```powershell
git add connector-hosts
git commit -m "feat: execute restart tasks in connector host"
```

## 任务 6：添加第二阶段验证脚本

**文件：**

- 创建：`scripts/verify-second-slice-ops.ps1`

- [ ] **步骤 1：添加验证脚本**

创建 `verify-second-slice-ops.ps1`：

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-Healthy {
  param([string]$Uri)
  $deadline = (Get-Date).AddSeconds(30)
  do {
    try {
      $result = Invoke-RestMethod -Method Get -Uri $Uri
      if ($result -eq "Healthy") { return }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
  } while ((Get-Date) -lt $deadline)
  throw "Service did not become healthy at $Uri"
}

function Wait-TaskCompleted {
  param([string]$GatewayUrl, [string]$OperationTaskId)
  $deadline = (Get-Date).AddSeconds(30)
  do {
    $task = Invoke-RestMethod -Method Get -Uri "$GatewayUrl/api/console/v1/operation-tasks/$OperationTaskId"
    if ($task.status -eq "completed") { return $task }
    Start-Sleep -Milliseconds 500
  } while ((Get-Date) -lt $deadline)
  throw "Operation task $OperationTaskId did not complete."
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln --no-restore
dotnet test backend/Nerv.IIP.sln --no-build
dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln
dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln --no-build

$appHubUrl = "http://127.0.0.1:58103"
$gatewayUrl = "http://127.0.0.1:58104"
$opsUrl = "http://127.0.0.1:58105"
$appHubProject = Join-Path $root "backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"
$opsProject = Join-Path $root "backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj"
$connectorHostProject = Join-Path $root "connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Nerv.IIP.ConnectorHost.Host.csproj"

$appHubJob = $null
$gatewayJob = $null
$opsJob = $null
$connectorHostJob = $null
try {
  $appHubJob = Start-Job -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_URLS = $url
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $appHubProject, $appHubUrl
  Wait-Healthy "$appHubUrl/health"

  $opsJob = Start-Job -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_URLS = $url
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $opsProject, $opsUrl
  Wait-Healthy "$opsUrl/health"

  $gatewayJob = Start-Job -ScriptBlock {
    param($project, $url, $appHub, $ops)
    $env:ASPNETCORE_URLS = $url
    $env:AppHub__BaseUrl = $appHub
    $env:Ops__BaseUrl = $ops
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $gatewayProject, $gatewayUrl, $appHubUrl, $opsUrl
  Wait-Healthy "$gatewayUrl/health"

  $connectorHostJob = Start-Job -ScriptBlock {
    param($project, $appHub, $ops)
    $env:Platform__AppHubBaseUrl = $appHub
    $env:Platform__OpsBaseUrl = $ops
    $env:ConnectorHost__CycleSeconds = "1"
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $connectorHostProject, $appHubUrl, $opsUrl

  Start-Sleep -Seconds 3

  $restart = @{
    organizationId = "org-001"
    environmentId = "env-dev"
    reason = "verify second slice restart"
    idempotencyKey = "verify-second-slice-restart-001"
  }
  $created = Invoke-RestMethod -Method Post -Uri "$gatewayUrl/api/console/v1/instances/docker-container-local-demo-001/operations/restart" -Body ($restart | ConvertTo-Json -Depth 5) -ContentType "application/json"
  $completed = Wait-TaskCompleted $gatewayUrl $created.operationTaskId

  if ($completed.status -ne "completed") {
    throw "Operation task did not complete."
  }
  if (-not ($completed.auditRecords | Where-Object { $_.action -eq "operation.requested" })) {
    throw "Operation task is missing request audit record."
  }
  if (-not ($completed.auditRecords | Where-Object { $_.action -eq "operation.completed" })) {
    throw "Operation task is missing completion audit record."
  }

  Write-Host "Second vertical slice verified with operationTaskId $($created.operationTaskId)."
}
finally {
  if ($connectorHostJob) { Stop-Job $connectorHostJob -ErrorAction SilentlyContinue; Remove-Job $connectorHostJob -Force -ErrorAction SilentlyContinue }
  if ($gatewayJob) { Stop-Job $gatewayJob -ErrorAction SilentlyContinue; Remove-Job $gatewayJob -Force -ErrorAction SilentlyContinue }
  if ($opsJob) { Stop-Job $opsJob -ErrorAction SilentlyContinue; Remove-Job $opsJob -Force -ErrorAction SilentlyContinue }
  if ($appHubJob) { Stop-Job $appHubJob -ErrorAction SilentlyContinue; Remove-Job $appHubJob -Force -ErrorAction SilentlyContinue }
}
```

- [ ] **步骤 2：运行第二阶段验证**

运行：

```powershell
pwsh scripts/verify-second-slice-ops.ps1
```

预期结果：

```text
backend restore/build/test: exit 0
connector-hosts restore/build/test: exit 0
Second vertical slice verified with operationTaskId op-000001.
```

- [ ] **步骤 3：提交**

运行：

```powershell
git add scripts/verify-second-slice-ops.ps1
git commit -m "test: verify second ops vertical slice"
```

## 执行顺序

1. 必须首先执行任务 1，因为它关闭第一阶段验证中观察到的唯一测试缺口。
2. 在 Ops、Gateway 或 Connector Host 代码引用 `Nerv.IIP.Contracts.Ops` 或 `Nerv.IIP.Sdk.Ops` 之前，必须完成任务 2。
3. 任务 3 依赖任务 2 的契约。
4. 任务 4 依赖任务 3 的 Ops endpoint。
5. 任务 5 依赖任务 2 的 SDK 和任务 3 的 Ops endpoint。
6. 任务 6 依赖任务 1 至任务 5。

任务 2 之后建议并行执行：

1. 一名执行者实施任务 3 的 Ops endpoint。
2. 一名执行者依据新 SDK 接口实施任务 5 的 Connector Host 操作循环。
3. Ops endpoint 路由和响应契约稳定后，可以开始 Gateway 任务 4。

## 第二次迭代完成定义

满足以下全部条件时，第二次迭代才算完成：

1. `dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj` 能发现并通过 AppHub Web endpoint 测试。
2. `Nerv.IIP.Contracts.Ops` 使用 Web JSON 选项序列化操作任务响应。
3. `Nerv.IIP.Sdk.Ops` 能创建操作任务、读取任务详情、轮询待处理任务并提交操作结果，且不引用服务内部实现。
4. Ops 能创建幂等的 `lifecycle.restart` 任务并记录 `operation.requested`。
5. Ops 待处理轮询创建一次尝试，将任务标记为 `dispatched` 并记录 `operation.dispatched`。
6. Connector Host 能执行待处理的 `lifecycle.restart` 任务并提交 `OperationResult`。
7. Ops 记录 `operation.completed` 或 `operation.failed`，并在任务详情中公开尝试和审计记录。
8. Gateway 能通过 `/api/console/v1/instances/{instanceKey}/operations/restart` 创建重启任务。
9. Gateway 能通过 `/api/console/v1/operation-tasks/{operationTaskId}` 返回任务详情。
10. Ops 操作结果处理不会直接更改 AppHub 状态。
11. `pwsh scripts/verify-second-slice-ops.ps1` 以代码 `0` 退出。

## 自检

规范覆盖：

1. 低风险操作闭环：由任务 2 至任务 6 覆盖。
2. 审计边界：由任务 3 的事实、endpoint 和测试覆盖。
3. Gateway 入口：由任务 4 覆盖。
4. Connector Host 执行：由任务 5 覆盖。
5. AppHub/Ops 边界：由“边界规则”和完成项 10 覆盖。
6. 第一阶段测试缺口：由任务 1 覆盖。

占位符扫描：

1. 不保留未解决标记或未定义的待填工作。
2. 所有文件路径均明确。
3. 每个任务都有命令和预期输出。

类型一致性：

1. `OperationTaskId`、`AttemptId`、`OperationCode`、`CorrelationId` 和 `ConnectorHostId` 的名称在契约、Ops、SDK、Gateway 和 Connector Host 之间一致。
2. `lifecycle.restart` 是 Gateway、Ops 和 Connector Host 测试使用的唯一操作代码。
3. `OperationResult` 继续位于 `Nerv.IIP.Contracts.ConnectorProtocol`，`Sdk.Ops` 复用它提交结果。
