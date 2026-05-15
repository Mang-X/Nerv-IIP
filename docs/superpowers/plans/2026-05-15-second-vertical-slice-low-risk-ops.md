# Second Vertical Slice Low-Risk Operations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立第二条纵切：Gateway 创建低风险 restart 运维任务，Ops 记录任务、尝试与审计，Connector Host 领取并执行动作，Ops 接收结果，AppHub 继续只通过 state snapshot 表达实例最终状态。

**Architecture:** 本阶段采用 HTTP pull 作为本地纵切传输机制：Connector Host 通过 `Sdk.Ops` 轮询 Ops 的 pending task endpoint，执行 `lifecycle.restart` 后回传 `OperationResult`。Ops 是动作生命周期与审计事实源；Gateway 只提供控制台入口；AppHub 不接收动作结果，也不被 Ops 直接改写实例状态。

**Tech Stack:** .NET 10、ASP.NET Core、FastEndpoints、xUnit、Microsoft.AspNetCore.Mvc.Testing、Platform SDK Core/Auth/ConnectorProtocol/Ops、本地 in-memory stores、PowerShell verification scripts。

---

## Execution Status

Completed for this PR on 2026-05-15.

1. All in-scope implementation items are present: AppHub endpoint tests, Ops contracts and SDK, Ops task/result/audit endpoints, Gateway restart/detail facade, Connector Host operation loop, Docker restart executor, and the second-slice verification script.
2. Final verification passed with `pwsh scripts/verify-second-slice-ops.ps1`; observed result: `Second vertical slice verified with operationTaskId op-000001.`
3. The task sections below remain the execution recipe. This branch packages the completed work in one PR instead of the per-task commits described in the original worker instructions.

## First Phase Gate

Fresh verification run on 2026-05-15:

```powershell
pwsh scripts/verify-first-slice.ps1
```

Observed result:

```text
backend restore/build/test: exit 0
connector-hosts restore/build/test: exit 0
First vertical slice verified with correlationId corr-first-slice.
```

No blocking first-phase omission was found against the local vertical-slice acceptance criteria. One quality gap remains visible in the test output: `Nerv.IIP.AppHub.Web.Tests` currently has no discoverable tests. This plan makes that the first task so the next stage starts from a cleaner baseline.

## Scope

### In This Plan

1. Backfill AppHub Web endpoint tests for the first vertical slice.
2. Add `Nerv.IIP.Contracts.Ops` public DTOs and `Nerv.IIP.Sdk.Ops` HTTP client.
3. Implement Ops in-memory operation task, attempt, result and audit facts.
4. Add Ops endpoints:
   - `POST /api/ops/v1/operation-tasks`
   - `GET /api/ops/v1/operation-tasks/{operationTaskId}`
   - `GET /api/ops/v1/operation-tasks/pending`
   - `POST /api/ops/v1/operation-results`
5. Add Gateway console-facing restart and operation detail endpoints.
6. Add Connector Host operation execution loop for `lifecycle.restart`.
7. Add `scripts/verify-second-slice-ops.ps1` to verify the local end-to-end restart task lifecycle.

### Outside This Plan

1. High-risk approvals and manual confirmation UI.
2. Stop, backup, restore, log pulling and batch operations.
3. Persistent PostgreSQL/CAP storage migration for IAM/AppHub/Ops.
4. Full console UI and generated frontend API client.
5. Notification messages for operation success/failure.

## File Structure Map

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

## Boundary Rules

1. Ops owns `OperationTask`, `OperationAttempt`, `AuditRecord` and operation result status.
2. AppHub owns instance facts and only changes them through registration, heartbeat and state snapshot.
3. Gateway does not reference `Nerv.IIP.Ops.Domain` or `Nerv.IIP.Ops.Infrastructure`.
4. Connector Host does not reference backend service Web, Domain or Infrastructure projects.
5. `Sdk.Ops` depends only on public contracts, `Sdk.Core`, `Sdk.Auth` and Connector Protocol result DTOs.
6. `lifecycle.restart` is the only executable operation in this plan.
7. Pending task polling is a local v1 HTTP contract hidden behind `Sdk.Ops`; callers must not depend on Ops internal domain objects.
8. Platform HTTP endpoints continue to use FastEndpoints, with route classes under `Endpoints/**`.

---

## Task 1: Backfill AppHub Web Endpoint Tests

**Files:**

- Create: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubConnectorEndpointTests.cs`

- [ ] **Step 1: Write the endpoint tests**

Create `AppHubConnectorEndpointTests.cs`:

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

- [ ] **Step 2: Run the AppHub Web tests**

Run:

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj
```

Expected: exit code `0`, with `2` tests discovered and passed.

- [ ] **Step 3: Commit**

Run:

```powershell
git add backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests
git commit -m "test: cover apphub connector endpoints"
```

## Task 2: Add Ops Public Contracts And SDK Client

**Files:**

- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Ops/OpsContracts.cs`
- Create: `backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Contracts.Ops.Tests/OpsContractJsonTests.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Ops/OpsClient.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Write the contract serialization test**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.Ops -o backend/common/Contracts/Nerv.IIP.Contracts.Ops --framework net10.0
dotnet new xunit -n Nerv.IIP.Contracts.Ops.Tests -o backend/tests/Nerv.IIP.Contracts.Ops.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj
```

Create `OpsContractJsonTests.cs`:

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

- [ ] **Step 2: Add Ops DTOs**

Create `OpsContracts.cs`:

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

- [ ] **Step 3: Run the contract test and verify it fails before SDK work is added**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj
```

Expected: exit code `0`, `1` test passed.

- [ ] **Step 4: Add Sdk.Ops project and client**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Sdk.Ops -o backend/common/Sdk/Nerv.IIP.Sdk.Ops --framework net10.0
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj
```

Create `OpsClient.cs`:

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

- [ ] **Step 5: Build contract and SDK projects**

Run:

```powershell
dotnet build backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj
```

Expected: both commands exit with code `0`.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.Ops backend/common/Sdk/Nerv.IIP.Sdk.Ops backend/tests/Nerv.IIP.Contracts.Ops.Tests backend/Nerv.IIP.sln
git commit -m "feat: add ops contracts and sdk client"
```

## Task 3: Implement Ops Task And Audit Endpoints

**Files:**

- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Domain/OperationFacts.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Domain/InMemoryOpsStateStore.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/OperationTasks/OperationTaskEndpoints.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- Create: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs`

- [ ] **Step 1: Write endpoint tests**

Create `OperationTaskEndpointTests.cs`:

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

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter Operation_task_can_be_created_dispatched_and_completed
```

Expected: FAIL with `404` or missing endpoint/store types.

- [ ] **Step 3: Add Ops facts**

Create `OperationFacts.cs`:

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

- [ ] **Step 4: Add in-memory Ops state store**

Create `InMemoryOpsStateStore.cs`:

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

- [ ] **Step 5: Add Ops endpoints**

Create `OperationTaskEndpoints.cs`:

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

Modify `Program.cs`:

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

- [ ] **Step 6: Run Ops Web tests**

Run:

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj
```

Expected: exit code `0`, with the existing skeleton test and the new operation lifecycle test passing.

- [ ] **Step 7: Commit**

Run:

```powershell
git add backend/services/Ops
git commit -m "feat: add ops operation task lifecycle"
```

## Task 4: Add Gateway Restart Facade

**Files:**

- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/OpsClient/OpsClient.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Operations/OperationEndpoints.cs`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj`
- Create: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOperationTests.cs`

- [ ] **Step 1: Write Gateway operation tests**

Create `GatewayOperationTests.cs`:

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

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --filter Restart_endpoint_creates_lifecycle_restart_task
```

Expected: FAIL with missing `RestartInstanceRequest` or route.

- [ ] **Step 3: Add Ops client in Gateway**

Create `Application/OpsClient/OpsClient.cs`:

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

Modify `Program.cs` to register the typed client:

```csharp
builder.Services.AddHttpClient<IGatewayOpsClient, GatewayOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ops:BaseUrl"] ?? "http://localhost:5105");
});
```

- [ ] **Step 4: Add Gateway endpoints**

Create `OperationEndpoints.cs`:

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

- [ ] **Step 5: Add project reference**

Run:

```powershell
dotnet add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj
```

- [ ] **Step 6: Run Gateway tests**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
```

Expected: exit code `0`. The Gateway project file still must not reference `Nerv.IIP.Ops.Domain` or `Nerv.IIP.Ops.Infrastructure`.

- [ ] **Step 7: Commit**

Run:

```powershell
git add backend/gateway/PlatformGateway
git commit -m "feat: add gateway restart operation facade"
```

## Task 5: Add Connector Host Operation Execution Loop

**Files:**

- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/ConnectorOperationAbstractions.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker/DockerConnector.cs`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/ConnectorOperationLoop.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/Nerv.IIP.ConnectorHost.Application.csproj`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Worker.cs`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/OperationLoopTests.cs`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/DockerConnectorOperationTests.cs`

- [ ] **Step 1: Add failing operation loop tests**

Create `OperationLoopTests.cs`:

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

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/Nerv.IIP.ConnectorHost.Application.Tests.csproj --filter Operation_loop_executes_pending_restart_and_reports_result
```

Expected: FAIL with missing operation abstractions or `ConnectorOperationLoop`.

- [ ] **Step 3: Add operation executor abstraction**

Create `ConnectorOperationAbstractions.cs`:

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

- [ ] **Step 4: Add connector operation loop**

Create `ConnectorOperationLoop.cs`:

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

- [ ] **Step 5: Add Docker restart executor behavior**

Modify `DockerConnector.cs` so the class implements `IConnectorOperationExecutor`:

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

- [ ] **Step 6: Wire host services and cycle timing**

Modify `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`:

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

Modify `Worker.cs` so each cycle runs reporting and operations:

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

- [ ] **Step 7: Add SDK reference to connector-hosts**

Run:

```powershell
dotnet add connector-hosts/src/Nerv.IIP.ConnectorHost.Application/Nerv.IIP.ConnectorHost.Application.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj backend/common/Sdk/Nerv.IIP.Sdk.Ops/Nerv.IIP.Sdk.Ops.csproj
dotnet add connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/Nerv.IIP.ConnectorHost.Connectors.Abstractions.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Ops/Nerv.IIP.Contracts.Ops.csproj
```

- [ ] **Step 8: Run connector-host tests**

Run:

```powershell
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
```

Expected: exit code `0`, including the operation loop test and Docker operation test.

- [ ] **Step 9: Commit**

Run:

```powershell
git add connector-hosts
git commit -m "feat: execute restart tasks in connector host"
```

## Task 6: Add Second Slice Verification Script

**Files:**

- Create: `scripts/verify-second-slice-ops.ps1`

- [ ] **Step 1: Add the verification script**

Create `verify-second-slice-ops.ps1`:

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

- [ ] **Step 2: Run second slice verification**

Run:

```powershell
pwsh scripts/verify-second-slice-ops.ps1
```

Expected:

```text
backend restore/build/test: exit 0
connector-hosts restore/build/test: exit 0
Second vertical slice verified with operationTaskId op-000001.
```

- [ ] **Step 3: Commit**

Run:

```powershell
git add scripts/verify-second-slice-ops.ps1
git commit -m "test: verify second ops vertical slice"
```

## Execution Order

1. Task 1 must be first because it closes the only test gap observed in first-phase verification.
2. Task 2 must finish before Ops, Gateway or Connector Host code references `Nerv.IIP.Contracts.Ops` or `Nerv.IIP.Sdk.Ops`.
3. Task 3 depends on Task 2 contracts.
4. Task 4 depends on Task 3 Ops endpoints.
5. Task 5 depends on Task 2 SDK and Task 3 Ops endpoints.
6. Task 6 depends on Tasks 1 through 5.

Recommended parallelization after Task 2:

1. One worker implements Task 3 Ops endpoints.
2. One worker implements Task 5 Connector Host operation loop against the new SDK interface.
3. Gateway Task 4 can start after Ops endpoint routes and response contracts are stable.

## Second Iteration Completion Definition

The second iteration is complete when all statements are true:

1. `dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj` discovers and passes AppHub Web endpoint tests.
2. `Nerv.IIP.Contracts.Ops` serializes operation task responses with web JSON options.
3. `Nerv.IIP.Sdk.Ops` can create operation tasks, read task detail, poll pending tasks and submit operation results without referencing service internals.
4. Ops can create an idempotent `lifecycle.restart` task and records `operation.requested`.
5. Ops pending polling creates an attempt, marks the task as `dispatched` and records `operation.dispatched`.
6. Connector Host can execute a pending `lifecycle.restart` task and submit `OperationResult`.
7. Ops records `operation.completed` or `operation.failed` and exposes attempts plus audit records in task detail.
8. Gateway can create a restart task through `/api/console/v1/instances/{instanceKey}/operations/restart`.
9. Gateway can return task detail through `/api/console/v1/operation-tasks/{operationTaskId}`.
10. AppHub state is not directly changed by Ops operation result handling.
11. `pwsh scripts/verify-second-slice-ops.ps1` exits with code `0`.

## Self Review

Spec coverage:

1. Low-risk action loop: covered by Tasks 2 through 6.
2. Audit boundary: covered by Task 3 facts, endpoints and tests.
3. Gateway entrypoint: covered by Task 4.
4. Connector Host execution: covered by Task 5.
5. AppHub/Ops boundary: covered by Boundary Rules and completion item 10.
6. First-phase test gap: covered by Task 1.

Placeholder scan:

1. No unresolved markers or undefined fill-in work remains.
2. All file paths are explicit.
3. Each task has commands and expected outputs.

Type consistency:

1. `OperationTaskId`, `AttemptId`, `OperationCode`, `CorrelationId` and `ConnectorHostId` names match across contracts, Ops, SDK, Gateway and Connector Host.
2. `lifecycle.restart` is the single operation code used in Gateway, Ops and Connector Host tests.
3. `OperationResult` remains in `Nerv.IIP.Contracts.ConnectorProtocol`, and `Sdk.Ops` reuses it for result submission.
