# First Vertical Slice Implementation Plan

> **For automated implementers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立 Nerv-IIP 第一迭代纵切：Connector Host 发现一个 Docker 运行目标，上报注册、心跳和状态快照，AppHub 沉淀应用与实例事实，PlatformGateway 能查询到最新状态。

**Architecture:** 第一迭代采用文档冻结的服务边界：IAM 先提供身份、权限、会话和 Connector Host 凭证底座；FileStorage 先提供主平台文件存储服务骨架和边界约束；AppHub 拥有应用与实例事实；PlatformGateway 只做薄 BFF，通过 AppHub 显式 HTTP/query contract 聚合数据；Connector Host 独立于 backend solution，通过 Platform SDK 的 ConnectorProtocol 客户端和共享 Connector Protocol DTO 调用平台。Ops 只创建服务骨架和健康入口，不进入第一迭代完成定义。

**Tech Stack:** .NET 10、ASP.NET Core、netcorepal-cloud-framework、FastEndpoints、PostgreSQL、RabbitMQ、Redis、FusionCache、MinIO、OpenTelemetry、Docker、xUnit 或模板默认测试框架。

---

## Scope

### In This Plan

1. `backend` 与 `connector-hosts` 两套 solution 骨架。
2. `backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol` 共享协议。
3. `backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries` 作为 Gateway 到 AppHub 的第一迭代查询契约。
4. `backend/common/Sdk/Nerv.IIP.Sdk.Core`、`Nerv.IIP.Sdk.Auth`、`Nerv.IIP.Sdk.ConnectorProtocol`、`Nerv.IIP.Sdk.FileStorage` 的最小 SDK 边界。
5. `backend/common/Caching/Nerv.IIP.Caching` 的 FusionCache 统一注册边界。
6. `backend/common/Observability/Nerv.IIP.Observability` 的日志、trace、metrics、correlation 基线。
7. IAM 最小后台管理底座：用户、角色、权限、会话、Connector Host 凭证、初始管理员 seed。
8. FileStorage 最小服务骨架：文件元数据、上传会话、上传指令、下载授权、Upload Provider 抽象、FilePurposePolicy、scanStatus 和对象存储适配边界。
9. AppHub 的 registration、heartbeat、state snapshot 写入和内部查询接口。
10. PlatformGateway 的实例列表与实例详情查询接口。
11. Connector Host、Sdk.ConnectorProtocol 客户端、Docker Connector 的最小发现与上报链路。

### Outside This Plan

1. Ops 到 Connector Host 的命令下发传输机制。
2. restart、stop、backup 等动作闭环。
3. 完整控制台 UI、菜单编排和视觉系统。
4. OAuth2/OIDC 授权服务器、SSO、MFA、WebAuthn、第三方应用市场。
5. FileStorage 的完整文件管理后台、文件预览、转码、复杂保留策略和跨服务附件工作流。
6. Sdk.Ops、Sdk.Observability 的完整实现和 SDK 多语言发布流水线。
7. Knowledge、AI Integration、复杂 autonomous workflow。

## File Structure Map

```text
backend/
  Nerv.IIP.sln
  Directory.Build.props
  Directory.Packages.props
  common/
    Contracts/
      Nerv.IIP.Contracts.ConnectorProtocol/
      Nerv.IIP.Contracts.AppHubQueries/
    Sdk/
      Nerv.IIP.Sdk.Core/
      Nerv.IIP.Sdk.Auth/
      Nerv.IIP.Sdk.ConnectorProtocol/
      Nerv.IIP.Sdk.FileStorage/
    Caching/
      Nerv.IIP.Caching/
    Observability/
      Nerv.IIP.Observability/
    Testing/
      Nerv.IIP.Testing/
  gateway/
    PlatformGateway/
      src/Nerv.IIP.PlatformGateway.Web/
      tests/Nerv.IIP.PlatformGateway.Web.Tests/
  services/
    Iam/
      src/Nerv.IIP.Iam.Web/
      src/Nerv.IIP.Iam.Domain/
      src/Nerv.IIP.Iam.Infrastructure/
      tests/Nerv.IIP.Iam.Web.Tests/
    FileStorage/
      src/Nerv.IIP.FileStorage.Web/
      src/Nerv.IIP.FileStorage.Domain/
      src/Nerv.IIP.FileStorage.Infrastructure/
      tests/Nerv.IIP.FileStorage.Web.Tests/
    AppHub/
      src/Nerv.IIP.AppHub.Web/
      src/Nerv.IIP.AppHub.Domain/
      src/Nerv.IIP.AppHub.Infrastructure/
      tests/Nerv.IIP.AppHub.Web.Tests/
      tests/Nerv.IIP.AppHub.Domain.Tests/
    Ops/
      src/Nerv.IIP.Ops.Web/
      src/Nerv.IIP.Ops.Domain/
      src/Nerv.IIP.Ops.Infrastructure/
      tests/Nerv.IIP.Ops.Web.Tests/
  tests/
    Nerv.IIP.Contracts.ConnectorProtocol.Tests/
    Nerv.IIP.Contracts.AppHubQueries.Tests/

connector-hosts/
  Nerv.IIP.ConnectorHost.sln
  src/
    Nerv.IIP.ConnectorHost.Host/
    Nerv.IIP.ConnectorHost.Application/
    Nerv.IIP.ConnectorHost.Contracts/
    Nerv.IIP.ConnectorHost.Connectors.Abstractions/
    Nerv.IIP.ConnectorHost.Connectors.Docker/
  tests/
    Nerv.IIP.ConnectorHost.Application.Tests/
    Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/
```

## Boundary Rules

1. PlatformGateway 不引用 `Nerv.IIP.AppHub.Domain` 或 `Nerv.IIP.AppHub.Infrastructure`。
2. Connector Host 不引用任何 backend 服务实现项目。
3. Connector Host 与平台共享的业务载荷只放在 `Nerv.IIP.Contracts.ConnectorProtocol`。
4. Gateway 到 AppHub 的第一迭代查询 DTO 放在 `Nerv.IIP.Contracts.AppHubQueries`，避免 Gateway 复制 AppHub 返回模型。
5. refresh token、session revoke list、OperationTask、AuditRecord、ApplicationInstance reported state 不使用缓存作为事实来源。
6. `--UseAdmin false` 固定传入 netcorepal 模板；IAM 使用 Nerv-IIP 自有领域模型。
7. 计划中的项目引用只服务首批单仓开发便利；发布和升级边界必须按 Platform SDK、版本化 Connector Protocol、公开 HTTP API 和 IAM 授权处理。
8. Connector Host、Connector 和示例应用的主版本必须与主平台主版本对齐；同一主版本内小版本可以低于主平台小版本。
9. FileStorage 拥有文件元数据、上传下载授权和对象存储 key；其它服务只通过 `fileId`、`FileReference` 或 Platform SDK 使用文件能力。
10. tus、S3 multipart 和 server-proxy 只作为 FileStorage Upload Provider 策略存在，业务服务和领域模型不直接依赖具体上传协议。
11. SDK 模块只封装公开 API、公开 DTO、认证上下文、错误模型和客户端传输，不引用服务端 Web、Domain、Infrastructure 或数据库模型。
12. SDK 不成为权限事实源、审计事实源、服务发现中心或文件事实源。
13. 平台 HTTP 接口统一使用 FastEndpoints；`Program.cs` 只保留服务注册、中间件和 `UseFastEndpoints()` 接线，具体接口放在各 Web 项目的 `Endpoints/**`。
14. 新增平台 HTTP 接口不得使用 Minimal API 的 `.MapGet()`、`.MapPost()`、`.MapPatch()` 等启动文件路由映射。

## Task 1: Scaffold Backend Solution And Common Projects

**Files:**

- Create: `backend/Nerv.IIP.sln`
- Create: `backend/Directory.Build.props`
- Create: `backend/Directory.Packages.props`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/Nerv.IIP.Contracts.AppHubQueries.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol/Nerv.IIP.Sdk.ConnectorProtocol.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj`
- Create: `backend/common/Caching/Nerv.IIP.Caching/Nerv.IIP.Caching.csproj`
- Create: `backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj`
- Create: `backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj`

- [ ] **Step 1: Verify template inputs**

Run:

```powershell
dotnet --version
dotnet new netcorepal-web --help
```

Expected:

- `dotnet --version` reports a .NET 10 SDK.
- `netcorepal-web` help shows `--Framework`, `--Database`, `--MessageQueue`, `--UseAspire`, `--IncludeCopilotInstructions`, and `--UseAdmin`.

- [ ] **Step 2: Create backend solution and shared projects**

Run:

```powershell
New-Item -ItemType Directory -Force -Path backend/common/Contracts | Out-Null
New-Item -ItemType Directory -Force -Path backend/common/Sdk | Out-Null
New-Item -ItemType Directory -Force -Path backend/common/Caching | Out-Null
New-Item -ItemType Directory -Force -Path backend/common/Observability | Out-Null
New-Item -ItemType Directory -Force -Path backend/common/Testing | Out-Null

dotnet new sln -n Nerv.IIP -o backend
dotnet new classlib -n Nerv.IIP.Contracts.ConnectorProtocol -o backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol --framework net10.0
dotnet new classlib -n Nerv.IIP.Contracts.AppHubQueries -o backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries --framework net10.0
dotnet new classlib -n Nerv.IIP.Sdk.Core -o backend/common/Sdk/Nerv.IIP.Sdk.Core --framework net10.0
dotnet new classlib -n Nerv.IIP.Sdk.Auth -o backend/common/Sdk/Nerv.IIP.Sdk.Auth --framework net10.0
dotnet new classlib -n Nerv.IIP.Sdk.ConnectorProtocol -o backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol --framework net10.0
dotnet new classlib -n Nerv.IIP.Sdk.FileStorage -o backend/common/Sdk/Nerv.IIP.Sdk.FileStorage --framework net10.0
dotnet new classlib -n Nerv.IIP.Caching -o backend/common/Caching/Nerv.IIP.Caching --framework net10.0
dotnet new classlib -n Nerv.IIP.Observability -o backend/common/Observability/Nerv.IIP.Observability --framework net10.0
dotnet new classlib -n Nerv.IIP.Testing -o backend/common/Testing/Nerv.IIP.Testing --framework net10.0

dotnet sln backend/Nerv.IIP.sln add `
  backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj `
  backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/Nerv.IIP.Contracts.AppHubQueries.csproj `
  backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj `
  backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj `
  backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol/Nerv.IIP.Sdk.ConnectorProtocol.csproj `
  backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj `
  backend/common/Caching/Nerv.IIP.Caching/Nerv.IIP.Caching.csproj `
  backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj `
  backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj
```

Expected:

- All nine shared projects target `net10.0`.
- `dotnet sln backend/Nerv.IIP.sln list` shows the nine shared projects.

- [ ] **Step 3: Wire SDK project references**

Run:

```powershell
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj reference `
  backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj

dotnet add backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol/Nerv.IIP.Sdk.ConnectorProtocol.csproj reference `
  backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj `
  backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj `
  backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj

dotnet add backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj reference `
  backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj `
  backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj
```

- [ ] **Step 4: Add repo-level build props**

Create `backend/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Create `backend/Directory.Packages.props` with central package management enabled. Keep template-owned single-service package references in the generated project files until the same package is used by more than one backend project.

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Build shared projects**

Run:

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
```

Expected: both commands exit with code `0`.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/Directory.Build.props backend/Directory.Packages.props backend/common
git commit -m "chore: scaffold backend common and sdk projects"
```

## Task 2: Define Connector Protocol Contracts

**Files:**

- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/ConnectorProtocolContracts.cs`
- Create: `backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/ConnectorProtocolJsonTests.cs`

- [ ] **Step 1: Add the protocol DTOs**

Create `ConnectorProtocolContracts.cs`:

```csharp
namespace Nerv.IIP.Contracts.ConnectorProtocol;

public sealed record ConnectorRequestContext(
    string ProtocolVersion,
    string SdkVersion,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    string OrganizationId,
    string EnvironmentId,
    string ConnectorHostId);

public sealed record ApplicationRegistration(
    ConnectorRequestContext Context,
    string IdempotencyKey,
    string NodeKey,
    string NodeName,
    string DeploymentKind,
    string ApplicationKey,
    string ApplicationName,
    string Version,
    string InstanceKey,
    string InstanceName,
    IReadOnlyList<CapabilityDescriptor> Capabilities,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CapabilityDescriptor(
    string CapabilityCode,
    string CapabilityVersion,
    string Category,
    IReadOnlyList<string> SupportedOperations,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ApplicationHeartbeat(
    ConnectorRequestContext Context,
    string InstanceKey,
    DateTimeOffset HeartbeatAtUtc,
    bool Reachable,
    DateTimeOffset ConnectorHostStartedAtUtc,
    int LatencyMs,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record InstanceStateSnapshot(
    ConnectorRequestContext Context,
    string InstanceKey,
    DateTimeOffset ObservedAtUtc,
    string ReportedStatus,
    string HealthStatus,
    string Summary,
    IReadOnlyDictionary<string, string> Detail,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record OperationResult(
    ConnectorRequestContext Context,
    string OperationTaskId,
    string AttemptId,
    string InstanceKey,
    string OperationCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string ExecutionStatus,
    FailureReason? Failure,
    IReadOnlyDictionary<string, string> Output);

public sealed record FailureReason(
    string Code,
    string Message,
    string Category,
    bool Retryable,
    IReadOnlyDictionary<string, string> Detail);
```

- [ ] **Step 2: Add serialization tests**

Run:

```powershell
dotnet new xunit -n Nerv.IIP.Contracts.ConnectorProtocol.Tests -o backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests.csproj
```

Create `ConnectorProtocolJsonTests.cs`:

```csharp
using System.Text.Json;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.Contracts.ConnectorProtocol.Tests;

public sealed class ConnectorProtocolJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ApplicationRegistration_round_trips_with_web_json_options()
    {
        var context = new ConnectorRequestContext("1.0", "1.0", "corr-001", DateTimeOffset.Parse("2026-05-14T00:00:00Z"), "org-001", "env-dev", "connector-host-001");
        var source = new ApplicationRegistration(
            context,
            "idem-001",
            "node-001",
            "local-docker",
            "docker",
            "demo-api",
            "Demo API",
            "1.0.0",
            "demo-api-001",
            "demo-api",
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())],
            new Dictionary<string, string> { ["containerId"] = "abc123" });

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<ApplicationRegistration>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("demo-api", result.ApplicationKey);
        Assert.Equal("demo-api-001", result.InstanceKey);
        Assert.Equal("lifecycle.restart", result.Capabilities.Single().CapabilityCode);
    }
}
```

- [ ] **Step 3: Run contract tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests.csproj
```

Expected: test exits with code `0`.

- [ ] **Step 4: Commit**

Run:

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests backend/Nerv.IIP.sln
git commit -m "feat: define connector protocol contracts"
```

## Task 3: Define AppHub Query Contract

**Files:**

- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/AppHubQueryContracts.cs`
- Create: `backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/Nerv.IIP.Contracts.AppHubQueries.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/AppHubQueryJsonTests.cs`

- [ ] **Step 1: Add query DTOs**

Create `AppHubQueryContracts.cs`:

```csharp
namespace Nerv.IIP.Contracts.AppHubQueries;

public sealed record InstanceListQuery(
    string OrganizationId,
    string EnvironmentId,
    int PageNumber,
    int PageSize,
    string? Search);

public sealed record InstanceListResponse(
    int PageNumber,
    int PageSize,
    int TotalCount,
    IReadOnlyList<InstanceListItem> Items);

public sealed record InstanceListItem(
    string ApplicationKey,
    string ApplicationName,
    string Version,
    string NodeKey,
    string NodeName,
    string InstanceKey,
    string InstanceName,
    string ReportedStatus,
    string HealthStatus,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? LastStateObservedAtUtc);

public sealed record InstanceDetailResponse(
    string ApplicationKey,
    string ApplicationName,
    string Version,
    string NodeKey,
    string NodeName,
    string InstanceKey,
    string InstanceName,
    string ReportedStatus,
    string HealthStatus,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? LastStateObservedAtUtc,
    IReadOnlyList<CapabilitySummary> Capabilities,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CapabilitySummary(
    string CapabilityCode,
    string CapabilityVersion,
    string Category,
    IReadOnlyList<string> SupportedOperations);
```

- [ ] **Step 2: Add contract tests**

Run:

```powershell
dotnet new xunit -n Nerv.IIP.Contracts.AppHubQueries.Tests -o backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/Nerv.IIP.Contracts.AppHubQueries.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/Nerv.IIP.Contracts.AppHubQueries.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/Nerv.IIP.Contracts.AppHubQueries.Tests.csproj
```

Create `AppHubQueryJsonTests.cs`:

```csharp
using System.Text.Json;
using Nerv.IIP.Contracts.AppHubQueries;

namespace Nerv.IIP.Contracts.AppHubQueries.Tests;

public sealed class AppHubQueryJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void InstanceDetailResponse_round_trips_with_web_json_options()
    {
        var source = new InstanceDetailResponse(
            "demo-api",
            "Demo API",
            "1.0.0",
            "node-001",
            "local-docker",
            "demo-api-001",
            "demo-api",
            "running",
            "healthy",
            DateTimeOffset.Parse("2026-05-14T00:00:00Z"),
            DateTimeOffset.Parse("2026-05-14T00:00:10Z"),
            [new CapabilitySummary("lifecycle.restart", "1.0", "lifecycle", ["restart"])],
            new Dictionary<string, string> { ["containerId"] = "abc123" });

        var json = JsonSerializer.Serialize(source, JsonOptions);
        var result = JsonSerializer.Deserialize<InstanceDetailResponse>(json, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal("demo-api-001", result.InstanceKey);
        Assert.Equal("running", result.ReportedStatus);
        Assert.Equal("lifecycle.restart", result.Capabilities.Single().CapabilityCode);
    }
}
```

- [ ] **Step 3: Run query contract tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/Nerv.IIP.Contracts.AppHubQueries.Tests.csproj
```

Expected: test exits with code `0`.

- [ ] **Step 4: Commit**

Run:

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests backend/Nerv.IIP.sln
git commit -m "feat: define apphub query contracts"
```

## Task 4: Scaffold Platform Services

**Files:**

- Create: `backend/services/Iam/**`
- Create: `backend/services/FileStorage/**`
- Create: `backend/services/AppHub/**`
- Create: `backend/services/Ops/**`
- Create: `backend/gateway/PlatformGateway/**`

- [ ] **Step 1: Create Iam, FileStorage, AppHub, and Ops with netcorepal template**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Iam -o backend/services/Iam --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new netcorepal-web -n Nerv.IIP.FileStorage -o backend/services/FileStorage --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new netcorepal-web -n Nerv.IIP.AppHub -o backend/services/AppHub --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new netcorepal-web -n Nerv.IIP.Ops -o backend/services/Ops --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
```

Expected:

- Each service has `.Web`, `.Domain`, and `.Infrastructure` projects.
- No template Admin UI or Admin RBAC code is generated into Nerv-IIP service ownership.

- [ ] **Step 2: Create PlatformGateway as thin Web service**

Run:

```powershell
New-Item -ItemType Directory -Force -Path backend/gateway/PlatformGateway/src | Out-Null
New-Item -ItemType Directory -Force -Path backend/gateway/PlatformGateway/tests | Out-Null
dotnet new web -n Nerv.IIP.PlatformGateway.Web -o backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web --framework net10.0
dotnet new xunit -n Nerv.IIP.PlatformGateway.Web.Tests -o backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests --framework net10.0
dotnet add backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj reference backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj
```

- [ ] **Step 3: Add projects to backend solution**

Run:

```powershell
dotnet sln backend/Nerv.IIP.sln add `
  backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj `
  backend/services/Iam/src/Nerv.IIP.Iam.Domain/Nerv.IIP.Iam.Domain.csproj `
  backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Nerv.IIP.Iam.Infrastructure.csproj `
  backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Nerv.IIP.FileStorage.Web.csproj `
  backend/services/FileStorage/src/Nerv.IIP.FileStorage.Domain/Nerv.IIP.FileStorage.Domain.csproj `
  backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Nerv.IIP.FileStorage.Infrastructure.csproj `
  backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj `
  backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/Nerv.IIP.AppHub.Domain.csproj `
  backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj `
  backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj `
  backend/services/Ops/src/Nerv.IIP.Ops.Domain/Nerv.IIP.Ops.Domain.csproj `
  backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj `
  backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj `
  backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
```

- [ ] **Step 4: Add shared references**

Run:

```powershell
dotnet add backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj reference `
  backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj `
  backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/Nerv.IIP.Contracts.AppHubQueries.csproj `
  backend/common/Caching/Nerv.IIP.Caching/Nerv.IIP.Caching.csproj `
  backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj

dotnet add backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj reference `
  backend/common/Caching/Nerv.IIP.Caching/Nerv.IIP.Caching.csproj `
  backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj

dotnet add backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Nerv.IIP.FileStorage.Web.csproj reference `
  backend/common/Caching/Nerv.IIP.Caching/Nerv.IIP.Caching.csproj `
  backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj

dotnet add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj reference `
  backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/Nerv.IIP.Contracts.AppHubQueries.csproj `
  backend/common/Caching/Nerv.IIP.Caching/Nerv.IIP.Caching.csproj `
  backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj
```

- [ ] **Step 5: Build**

Run:

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
```

Expected: build exits with code `0`.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend
git commit -m "chore: scaffold platform services"
```

## Task 5: Implement IAM Foundation

**Files:**

- Create/Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/**`
- Create/Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/EntityConfigurations/**`
- Create/Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/**`
- Create/Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/**`
- Test: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/**`

- [ ] **Step 1: Model IAM facts**

Create aggregates and persistence mappings for:

| Aggregate | Owns |
| --- | --- |
| `Organization` | organization id, name, status |
| `Environment` | environment id, organization id, name, status |
| `User` | login name, email, password hash, enabled flag, security stamp |
| `Role` | role name, permission codes |
| `Membership` | user, organization, environment, role assignments |
| `UserSession` | refresh token hash, issued/expires/revoked timestamps, permission version |
| `ConnectorHostCredential` | connector host identity, organization, environment, capability scope, secret hash, valid range |

- [ ] **Step 2: Add first IAM endpoints**

Implement these endpoints first:

```text
POST /api/iam/v1/auth/login
POST /api/iam/v1/auth/refresh
POST /api/iam/v1/auth/logout
GET  /api/iam/v1/me
GET  /api/iam/v1/users
POST /api/iam/v1/users
PATCH /api/iam/v1/users/{userId}
POST /api/iam/v1/users/{userId}/disable
GET  /api/iam/v1/roles
POST /api/iam/v1/roles
PATCH /api/iam/v1/roles/{roleId}/permissions
GET  /api/iam/v1/sessions
POST /api/iam/v1/sessions/{sessionId}/revoke
```

Use ASP.NET Core `PasswordHasher<TUser>` through a thin adapter. Store refresh tokens and Connector Host secrets only as hashes.

Authentication uses short-lived JWT Bearer access tokens plus refresh token rotation. Do not add a separate session authentication code. `sessionId`, `securityStamp`, and `permissionVersion` claims must be validated against IAM server-side facts for protected operations.

- [ ] **Step 3: Add initial seed**

Seed:

1. One organization.
2. One environment.
3. One super administrator user.
4. One platform administrator role.
5. One Connector Host credential for the first local Connector Host.

Seed at least these first permission codes:

```text
iam.users.read
iam.users.manage
iam.roles.read
iam.roles.manage
iam.sessions.read
iam.sessions.revoke
connectors.registrations.write
connectors.heartbeats.write
connectors.state-snapshots.write
apphub.instances.read
files.upload
files.read
files.download-grants.create
files.archive
ops.tasks.create
ops.tasks.read
ops.results.write
ops.audit.read
```

The seed must be idempotent by natural keys, not by generated database ids.

- [ ] **Step 4: Add tests**

Cover these cases:

1. Super administrator can login and receives access token plus refresh token.
2. Refresh rotates the refresh token and invalidates the old refresh token.
3. Logout revokes the active session.
4. Disabled user cannot refresh and cannot access protected endpoints.
5. Connector Host credential can be validated as `principalType = connector-host` with organization and environment scope.
6. Protected endpoints reject stale `permissionVersion` or revoked `sessionId`.

- [ ] **Step 5: Commit**

Run:

```powershell
git add backend/services/Iam
git commit -m "feat: add iam foundation"
```

## Task 6: Implement AppHub Domain And Connector Host Write APIs

**Files:**

- Create/Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/AggregatesModel/**`
- Create/Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/DomainEvents/**`
- Create/Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/**`
- Create/Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Commands/**`
- Create/Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Queries/**`
- Create/Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Endpoints/**`
- Test: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/**`
- Test: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/**`

- [ ] **Step 1: Model AppHub facts**

Implement these facts:

| Aggregate or entity | Required behavior |
| --- | --- |
| `Application` | Created or updated by `applicationKey`; tracks display name and observed versions. |
| `ApplicationVersion` | Created by `applicationKey + version`; does not replace prior versions. |
| `ManagedNode` | Created or updated by `nodeKey`; tracks deployment kind and node name. |
| `ApplicationInstance` | Created or updated by `instanceKey`; owns current reported status and health status. |
| `CapabilityManifest` | Replaced by latest registration for the same instance. |
| `InstanceLiveness` | Updated only by heartbeat; keeps last heartbeat time and reachability. |
| `InstanceStateHistory` | Appended by state snapshot; state-change event only when status changes. |

- [ ] **Step 2: Add AppHub write endpoints**

Implement:

```text
POST /api/connectors/v1/registrations
POST /api/connectors/v1/heartbeats
POST /api/connectors/v1/state-snapshots
```

Each endpoint:

1. Requires Connector Host authentication after IAM task is available.
2. Accepts DTOs from `Nerv.IIP.Contracts.ConnectorProtocol`.
3. Writes `correlationId`, `organizationId`, `environmentId`, `connectorHostId`, and `sdkVersion`.
4. Returns a ProblemDetails-compatible failure for invalid organization, environment, connector host, or instance context.
5. Requires the matching permission code: `connectors.registrations.write`, `connectors.heartbeats.write`, or `connectors.state-snapshots.write`.

- [ ] **Step 3: Enforce idempotency and state rules**

Implement these rules:

1. Duplicate `ApplicationRegistration.IdempotencyKey` returns the same logical registration result.
2. Heartbeat updates `InstanceLiveness` and does not change `ApplicationInstance.ReportedStatus`.
3. State snapshot updates `ReportedStatus`, `HealthStatus`, and state history.
4. `InstanceStatusChanged` publishes only when the reported status changes.
5. Any externally published IntegrationEvent must use the CAP outbox or equivalent reliable publishing path; if first iteration defers external consumers, keep the converter and outbox shape ready.

- [ ] **Step 4: Add AppHub internal query endpoints**

Implement internal endpoints for Gateway:

```text
POST /internal/apphub/v1/instances/query
GET  /internal/apphub/v1/instances/{instanceKey}
```

Responses use `Nerv.IIP.Contracts.AppHubQueries`.

- [ ] **Step 5: Add tests**

Cover these cases:

1. Registration creates application, version, node, instance, and capability rows.
2. Repeating the same registration idempotency key does not create duplicates.
3. Heartbeat changes liveness and preserves reported status.
4. First state snapshot writes state history.
5. Second snapshot with same status does not publish `InstanceStatusChanged`.
6. Snapshot with changed status publishes one `InstanceStatusChanged`.
7. Internal instance query returns application, version, node, liveness, state, and capabilities.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend/services/AppHub
git commit -m "feat: add apphub connector ingestion"
```

## Task 7: Implement PlatformGateway Instance Queries

**Files:**

- Create/Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/AppHubClient/**`
- Create/Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Instances/**`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- Test: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/**`

- [ ] **Step 1: Add AppHub HTTP client**

Create a typed client that calls:

```text
POST /internal/apphub/v1/instances/query
GET  /internal/apphub/v1/instances/{instanceKey}
```

Use DTOs from `Nerv.IIP.Contracts.AppHubQueries`. The client belongs to Gateway Web/Application and does not reference AppHub Domain or Infrastructure.

- [ ] **Step 2: Add Gateway public endpoints**

Implement:

```text
GET /api/console/v1/instances
GET /api/console/v1/instances/{instanceKey}
```

The list endpoint accepts:

```text
organizationId
environmentId
pageNumber
pageSize
search
```

The detail endpoint accepts:

```text
organizationId
environmentId
instanceKey
```

- [ ] **Step 3: Add caching on read side**

Cache the instance list and detail responses through `Nerv.IIP.Caching` with keys following:

```text
gateway:instance-list:{organizationId}:{environmentId}:query:{hash}:v1
gateway:instance-detail:{organizationId}:{environmentId}:instance:{instanceKey}:v1
```

TTL must be short enough for console freshness; start with a small default and keep explicit invalidation available when AppHub events are later consumed by Gateway.

- [ ] **Step 4: Add tests**

Cover these cases:

1. Gateway list endpoint maps query parameters to `InstanceListQuery`.
2. Gateway detail endpoint returns AppHub detail response without leaking AppHub internal route names.
3. Gateway returns a diagnostic failure if AppHub is unavailable.
4. Gateway project file does not reference `Nerv.IIP.AppHub.Domain` or `Nerv.IIP.AppHub.Infrastructure`.

- [ ] **Step 5: Commit**

Run:

```powershell
git add backend/gateway/PlatformGateway
git commit -m "feat: add gateway instance queries"
```

## Task 8: Add Caching And Observability Shared Libraries

**Files:**

- Create/Modify: `backend/common/Caching/Nerv.IIP.Caching/**`
- Create/Modify: `backend/common/Observability/Nerv.IIP.Observability/**`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Program.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`

- [ ] **Step 1: Add FusionCache registration boundary**

`Nerv.IIP.Caching` exposes one public service registration method:

```csharp
services.AddNervIipCaching(configuration, serviceName);
```

It owns:

1. FusionCache registration.
2. Redis distributed cache configuration.
3. Redis backplane configuration.
4. System.Text.Json serializer options.
5. OpenTelemetry hooks for cache behavior.
6. Cache key helper methods.

- [ ] **Step 2: Add cache key helpers**

Expose helpers for:

```text
AppHub instance list
AppHub instance detail
Gateway instance list
Gateway instance detail
IAM permission snapshot
```

Each helper must require service, organization id, environment id, resource, stable id or normalized query hash, and schema version.

- [ ] **Step 3: Add Observability registration boundary**

`Nerv.IIP.Observability` exposes one public registration method:

```csharp
services.AddNervIipObservability(configuration, serviceName);
```

It owns:

1. OpenTelemetry traces, metrics, and logs.
2. ASP.NET Core instrumentation.
3. HTTP client instrumentation.
4. netcorepal and CAP instrumentation supplied by the selected template package set.
5. Correlation id enrichment.
6. Health and build info conventions.

- [ ] **Step 4: Wire services**

Call the shared registration methods from Iam, AppHub, and PlatformGateway. Keep service-specific settings in appsettings or environment configuration, not hard-coded in endpoint classes.

- [ ] **Step 5: Add tests**

Cover these cases:

1. Cache key helpers generate different keys for different organizations.
2. Cache key helpers generate different keys for different schema versions.
3. Gateway cached read returns the updated result after explicit invalidation.
4. A request with `correlationId` appears in logs and trace activity tags.

- [ ] **Step 6: Commit**

Run:

```powershell
git add backend/common/Caching backend/common/Observability backend/services/Iam backend/services/AppHub backend/gateway/PlatformGateway
git commit -m "feat: add shared caching and observability"
```

## Task 9: Scaffold Connector Host And Docker Connector

**Files:**

- Create: `connector-hosts/Nerv.IIP.ConnectorHost.sln`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/**`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Application/**`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts/**`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/**`
- Create: `connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker/**`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/**`
- Create: `connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/**`

- [ ] **Step 1: Create connector host solution and projects**

Run:

```powershell
dotnet new sln -n Nerv.IIP.ConnectorHost -o connector-hosts
dotnet new worker -n Nerv.IIP.ConnectorHost.Host -o connector-hosts/src/Nerv.IIP.ConnectorHost.Host --framework net10.0
dotnet new classlib -n Nerv.IIP.ConnectorHost.Application -o connector-hosts/src/Nerv.IIP.ConnectorHost.Application --framework net10.0
dotnet new classlib -n Nerv.IIP.ConnectorHost.Contracts -o connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts --framework net10.0
dotnet new classlib -n Nerv.IIP.ConnectorHost.Connectors.Abstractions -o connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions --framework net10.0
dotnet new classlib -n Nerv.IIP.ConnectorHost.Connectors.Docker -o connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker --framework net10.0
dotnet new xunit -n Nerv.IIP.ConnectorHost.Application.Tests -o connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.ConnectorHost.Connectors.Docker.Tests -o connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests --framework net10.0
```

- [ ] **Step 2: Add references**

Run:

```powershell
dotnet add connector-hosts/src/Nerv.IIP.ConnectorHost.Application/Nerv.IIP.ConnectorHost.Application.csproj reference `
  backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj `
  backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj `
  backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol/Nerv.IIP.Sdk.ConnectorProtocol.csproj `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts/Nerv.IIP.ConnectorHost.Contracts.csproj `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/Nerv.IIP.ConnectorHost.Connectors.Abstractions.csproj

dotnet add connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker/Nerv.IIP.ConnectorHost.Connectors.Docker.csproj reference `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/Nerv.IIP.ConnectorHost.Connectors.Abstractions.csproj

dotnet add connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Nerv.IIP.ConnectorHost.Host.csproj reference `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Application/Nerv.IIP.ConnectorHost.Application.csproj `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker/Nerv.IIP.ConnectorHost.Connectors.Docker.csproj

dotnet sln connector-hosts/Nerv.IIP.ConnectorHost.sln add `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Nerv.IIP.ConnectorHost.Host.csproj `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Application/Nerv.IIP.ConnectorHost.Application.csproj `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts/Nerv.IIP.ConnectorHost.Contracts.csproj `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/Nerv.IIP.ConnectorHost.Connectors.Abstractions.csproj `
  connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker/Nerv.IIP.ConnectorHost.Connectors.Docker.csproj `
  connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/Nerv.IIP.ConnectorHost.Application.Tests.csproj `
  connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests.csproj
```

- [ ] **Step 3: Define connector abstraction**

`Nerv.IIP.ConnectorHost.Connectors.Abstractions` exposes:

```text
IConnector
ConnectorTarget
ConnectorCapability
ConnectorStateSnapshot
```

The abstraction returns stable `nodeKey`, `applicationKey`, and `instanceKey` values.

- [ ] **Step 4: Implement Docker Connector**

Docker Connector maps one discovered container to:

1. `nodeKey`: stable local Docker node key.
2. `applicationKey`: image/repository-derived key.
3. `version`: image tag or digest-derived version.
4. `instanceKey`: container id based stable key.
5. capabilities: at least `runtime`, `log`, and `lifecycle.restart`.

- [ ] **Step 5: Implement Connector Host reporting loop**

Connector Host:

1. Discovers targets through registered connectors.
2. Converts discovered targets to `ApplicationRegistration`.
3. Sends registration to AppHub through `Nerv.IIP.Sdk.ConnectorProtocol`.
4. Sends heartbeat on a fixed interval through `Nerv.IIP.Sdk.ConnectorProtocol`.
5. Sends state snapshot on startup and when observed state changes through `Nerv.IIP.Sdk.ConnectorProtocol`.
6. Uses IAM Connector Host credential to authenticate AppHub requests.
7. Logs correlation id for each registration, heartbeat, and state snapshot.

- [ ] **Step 6: Add tests**

Cover these cases:

1. Connector output maps to stable Connector Protocol registration fields.
2. Reporting loop sends registration before heartbeat.
3. Reporting loop sends state snapshot after registration.
4. Failed AppHub request is logged with correlation id and retried on the next cycle.

- [ ] **Step 7: Commit**

Run:

```powershell
git add connector-hosts
git commit -m "feat: add connector host docker connector"
```

## Task 10: Verify The First Vertical Slice

**Files:**

- Create/Modify: `infra/docker-compose.dev.yml`
- Create/Modify: `scripts/verify-first-slice.ps1`
- Create/Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/appsettings.Development.json`
- Create/Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/appsettings.Development.json`
- Create/Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/appsettings.Development.json`
- Create/Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json`
- Create/Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`

- [ ] **Step 1: Add development dependency compose**

Compose includes:

```text
PostgreSQL
Redis
RabbitMQ
MinIO
OpenTelemetry collector
```

Qdrant can be present in the broader development stack, but this first slice does not depend on its runtime behavior. MinIO is included for FileStorage readiness; the Connector Host registration slice does not require object content flow.

- [ ] **Step 2: Add verification script**

`scripts/verify-first-slice.ps1` executes:

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
dotnet test backend/Nerv.IIP.sln
dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln
dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
```

Then it starts the required services in the selected execution environment and verifies:

1. AppHub accepts registration.
2. AppHub accepts heartbeat.
3. AppHub accepts state snapshot.
4. PlatformGateway list endpoint returns the registered instance.
5. PlatformGateway detail endpoint returns reported status, health status, heartbeat timestamp, and capabilities.

- [ ] **Step 3: Run vertical slice verification**

Run:

```powershell
pwsh scripts/verify-first-slice.ps1
```

Expected:

1. All restore, build, and test commands exit with code `0`.
2. One Docker target appears in `GET /api/console/v1/instances`.
3. Detail query returns the same `instanceKey` sent by Connector Host.
4. Connector Host, AppHub, and Gateway logs share the same correlation id for one request chain.

- [ ] **Step 4: Commit**

Run:

```powershell
git add infra scripts backend connector-hosts
git commit -m "test: verify first vertical slice"
```

## Execution Order

1. Task 1 must be first.
2. Task 2 and Task 3 can run after Task 1.
3. Task 4 depends on Task 1.
4. Task 5 can run after Task 4.
5. Task 6 depends on Task 2, Task 3, Task 4, and IAM auth hooks from Task 5.
6. Task 7 depends on Task 3, Task 4, Task 6, and Task 8.
7. Task 8 can run after Task 4, but Gateway caching acceptance is verified after Task 7.
8. Task 9 depends on Task 1 SDK projects, Task 2 contracts, and the Connector Host credential seed from Task 5.
9. Task 10 depends on Tasks 1 through 9.

Recommended parallelization:

1. One worker implements Task 5 IAM.
2. One worker implements Task 6 AppHub after contracts are ready.
3. One worker implements Task 9 Connector Host after SDK projects and contracts are ready.
4. Gateway waits for AppHub query contract and can proceed with a fake AppHub HTTP handler until AppHub is running.

## First Iteration Completion Definition

The first iteration is complete when all statements are true:

1. `dotnet restore`, `dotnet build`, and `dotnet test` pass for backend solution.
2. `dotnet restore`, `dotnet build`, and `dotnet test` pass for connector-hosts solution.
3. IAM can seed an administrator and one Connector Host credential.
4. Platform SDK Core/Auth/ConnectorProtocol/FileStorage projects exist and do not reference backend service Web, Domain, Infrastructure, or database models.
5. FileStorage service exists as a health/build-info skeleton with file metadata, upload session, upload instructions, download grant, Upload Provider abstraction, FilePurposePolicy, scanStatus, and object storage adapter boundaries documented in code structure.
6. Connector Host can authenticate to AppHub as `principalType = connector-host`.
7. Connector Host can send registration, heartbeat, and state snapshot through `Nerv.IIP.Sdk.ConnectorProtocol`.
8. AppHub persists application, version, node, instance, capability, liveness, and state history facts.
9. PlatformGateway can return instance list and detail through AppHub HTTP/query contract.
10. Gateway does not reference AppHub Domain or Infrastructure projects.
11. Logs and traces can be correlated across Connector Host, AppHub, and Gateway.
12. Ops service exists only as a health/build-info skeleton in this iteration.

## Self Review

Spec coverage:

1. Architecture boundary: covered by Boundary Rules, Task 4, Task 6, Task 7, and Task 9.
2. Implementability: covered by concrete scaffold commands, file map, task ordering, and verification script.
3. Maintainability and extensibility: covered by common contracts, modular SDK boundary, caching boundary, observability boundary, and Gateway/AppHub query contract separation.
4. Complexity control: Ops action loop, full UI, OAuth/OIDC, SSO, MFA, Knowledge, AI Integration, and advanced file-management workflows are outside this plan.
5. Basic admin timing: IAM foundation starts in Task 5, before AppHub Connector Host authentication and before Gateway protected queries.

Plan self-check:

1. No task relies on an undefined service boundary.
2. No first-iteration acceptance item depends on Ops action dispatch.
3. Query contract names match Task 3, Task 6, and Task 7.
