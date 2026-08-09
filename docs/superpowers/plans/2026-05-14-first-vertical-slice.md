# 第一阶段纵切实施计划

> **面向自动化实施者：** 必须使用子技能：使用 superpowers:executing-plans 逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 建立 Nerv-IIP 第一迭代纵切：Connector Host 发现一个 Docker 运行目标，上报注册、心跳和状态快照，AppHub 沉淀应用与实例事实，PlatformGateway 能查询到最新状态。

**执行状态（2026-05-15）：** 第一迭代纵切骨架已经落地，并通过 `pwsh scripts/verify-first-slice.ps1` 验证。当前实现满足本计划的本地纵切验收口径：backend 与 connector-hosts 可 restore/build/test，Connector Host 可通过 Platform SDK 上报 registration、heartbeat、state snapshot，AppHub 可接收并沉淀内存态实例事实，PlatformGateway 可查询实例列表与详情。本文档保留为原始执行计划和后续补齐真实持久化、完整 IAM、FileStorage、Ops 等能力的任务来源。

**架构：** 第一迭代采用文档冻结的服务边界：IAM 先提供身份、权限、会话和 Connector Host 凭证底座；FileStorage 先提供主平台文件存储服务骨架和边界约束；AppHub 拥有应用与实例事实；PlatformGateway 只做薄 BFF，通过 AppHub 显式 HTTP/query contract 聚合数据；Connector Host 独立于 backend solution，通过 Platform SDK 的 ConnectorProtocol 客户端和共享 Connector Protocol DTO 调用平台。Ops 只创建服务骨架和健康入口，不进入第一迭代完成定义。Notification 已作为独立平台通知边界冻结，但第一迭代不创建通知服务、通知表、`Sdk.Notification` 或外部通道 provider；其它服务不得临时内置站内通知、邮件、短信、企业 IM 或 Webhook 投递逻辑。

**技术栈：** .NET 10、ASP.NET Core、netcorepal-cloud-framework、FastEndpoints、PostgreSQL、RabbitMQ、Redis、FusionCache、MinIO、OpenTelemetry、Docker、xUnit 或模板默认测试框架。

---

## 范围

### 本计划范围内

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

### 本计划范围外

1. Ops 到 Connector Host 的命令下发传输机制。
2. restart、stop、backup 等动作闭环。
3. 完整控制台 UI、菜单编排和视觉系统。
4. OAuth2/OIDC 授权服务器、SSO、MFA、WebAuthn、第三方应用市场。
5. FileStorage 的完整文件管理后台、文件预览、转码、复杂保留策略和跨服务附件工作流。
6. Sdk.Ops、Sdk.Notification、Sdk.Observability 的完整实现和 SDK 多语言发布流水线。
7. Notification 服务骨架、站内通知、待办、通知偏好、去重合并、投递状态和外部通道 provider。
8. Knowledge、AI Integration、复杂 autonomous workflow。

## 文件结构图

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

## 边界规则

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
12. SDK 不成为权限事实源、审计事实源、通知事实源、服务发现中心或文件事实源。
13. Notification 是独立平台服务边界；第一迭代内 AppHub、Ops、Gateway、Connector Host 和行业扩展不得各自创建站内通知表或直连外部通知通道。
14. 平台 HTTP 接口统一使用 FastEndpoints；`Program.cs` 只保留服务注册、中间件和 `UseFastEndpoints()` 接线，具体接口放在各 Web 项目的 `Endpoints/**`。
15. 新增平台 HTTP 接口不得使用 Minimal API 的 `.MapGet()`、`.MapPost()`、`.MapPatch()` 等启动文件路由映射。

## 任务 1：搭建后端解决方案与公共项目骨架

**文件：**

- 创建：`backend/Nerv.IIP.sln`
- 创建：`backend/Directory.Build.props`
- 创建：`backend/Directory.Packages.props`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/Nerv.IIP.Contracts.AppHubQueries.csproj`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol/Nerv.IIP.Sdk.ConnectorProtocol.csproj`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj`
- 创建：`backend/common/Caching/Nerv.IIP.Caching/Nerv.IIP.Caching.csproj`
- 创建：`backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj`
- 创建：`backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj`

- [ ] **步骤 1：验证模板输入**

运行：

```powershell
dotnet --version
dotnet new netcorepal-web --help
```

预期结果：

- `dotnet --version` 报告 .NET 10 SDK。
- `netcorepal-web` 帮助中显示 `--Framework`、`--Database`、`--MessageQueue`、`--UseAspire`、`--IncludeCopilotInstructions` 和 `--UseAdmin`。

- [ ] **步骤 2：创建后端解决方案与共享项目**

运行：

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

预期结果：

- 全部九个共享项目都以 `net10.0` 为目标框架。
- `dotnet sln backend/Nerv.IIP.sln list` 显示这九个共享项目。

- [ ] **步骤 3：接入 SDK 项目引用**

运行：

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

- [ ] **步骤 4：添加仓库级构建属性**

创建 `backend/Directory.Build.props`：

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

创建 `backend/Directory.Packages.props` 并启用集中式包管理。在同一包被多个后端项目使用之前，模板拥有的单服务包引用继续保留在生成的项目文件中。

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

- [ ] **步骤 5：构建共享项目**

运行：

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
```

预期结果：两条命令都以代码 `0` 退出。

- [ ] **步骤 6：提交**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/Directory.Build.props backend/Directory.Packages.props backend/common
git commit -m "chore: scaffold backend common and sdk projects"
```

## 任务 2：定义 Connector Protocol 契约

**文件：**

- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/ConnectorProtocolContracts.cs`
- 创建：`backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/ConnectorProtocolJsonTests.cs`

- [ ] **步骤 1：添加协议 DTO**

创建 `ConnectorProtocolContracts.cs`：

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

- [ ] **步骤 2：添加序列化测试**

运行：

```powershell
dotnet new xunit -n Nerv.IIP.Contracts.ConnectorProtocol.Tests -o backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol/Nerv.IIP.Contracts.ConnectorProtocol.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests.csproj
```

创建 `ConnectorProtocolJsonTests.cs`：

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

- [ ] **步骤 3：运行契约测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests.csproj
```

预期结果：测试以代码 `0` 退出。

- [ ] **步骤 4：提交**

运行：

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol backend/tests/Nerv.IIP.Contracts.ConnectorProtocol.Tests backend/Nerv.IIP.sln
git commit -m "feat: define connector protocol contracts"
```

## 任务 3：定义 AppHub 查询契约

**文件：**

- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/AppHubQueryContracts.cs`
- 创建：`backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/Nerv.IIP.Contracts.AppHubQueries.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/AppHubQueryJsonTests.cs`

- [ ] **步骤 1：添加查询 DTO**

创建 `AppHubQueryContracts.cs`：

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

- [ ] **步骤 2：添加契约测试**

运行：

```powershell
dotnet new xunit -n Nerv.IIP.Contracts.AppHubQueries.Tests -o backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/Nerv.IIP.Contracts.AppHubQueries.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries/Nerv.IIP.Contracts.AppHubQueries.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/Nerv.IIP.Contracts.AppHubQueries.Tests.csproj
```

创建 `AppHubQueryJsonTests.cs`：

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

- [ ] **步骤 3：运行查询契约测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests/Nerv.IIP.Contracts.AppHubQueries.Tests.csproj
```

预期结果：测试以代码 `0` 退出。

- [ ] **步骤 4：提交**

运行：

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.AppHubQueries backend/tests/Nerv.IIP.Contracts.AppHubQueries.Tests backend/Nerv.IIP.sln
git commit -m "feat: define apphub query contracts"
```

## 任务 4：搭建平台服务骨架

**文件：**

- 创建：`backend/services/Iam/**`
- 创建：`backend/services/FileStorage/**`
- 创建：`backend/services/AppHub/**`
- 创建：`backend/services/Ops/**`
- 创建：`backend/gateway/PlatformGateway/**`

- [ ] **步骤 1：使用 netcorepal 模板创建 Iam、FileStorage、AppHub 和 Ops**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Iam -o backend/services/Iam --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new netcorepal-web -n Nerv.IIP.FileStorage -o backend/services/FileStorage --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new netcorepal-web -n Nerv.IIP.AppHub -o backend/services/AppHub --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new netcorepal-web -n Nerv.IIP.Ops -o backend/services/Ops --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
```

预期结果：

- 每个服务都有 `.Web`、`.Domain` 和 `.Infrastructure` 项目。
- 不在 Nerv-IIP 服务所有权范围中生成模板 Admin UI 或 Admin RBAC 代码。

- [ ] **步骤 2：将 PlatformGateway 创建为轻薄 Web 服务**

运行：

```powershell
New-Item -ItemType Directory -Force -Path backend/gateway/PlatformGateway/src | Out-Null
New-Item -ItemType Directory -Force -Path backend/gateway/PlatformGateway/tests | Out-Null
dotnet new web -n Nerv.IIP.PlatformGateway.Web -o backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web --framework net10.0
dotnet new xunit -n Nerv.IIP.PlatformGateway.Web.Tests -o backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests --framework net10.0
dotnet add backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj reference backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj
```

- [ ] **步骤 3：将项目添加到后端解决方案**

运行：

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

- [ ] **步骤 4：添加共享引用**

运行：

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

- [ ] **步骤 5：构建**

运行：

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
```

预期结果：构建以代码 `0` 退出。

- [ ] **步骤 6：提交**

运行：

```powershell
git add backend
git commit -m "chore: scaffold platform services"
```

## 任务 5：实施 IAM 基础

**文件：**

- 创建/修改：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/AggregatesModel/**`
- 创建/修改：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/EntityConfigurations/**`
- 创建/修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/**`
- 创建/修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/**`
- 测试：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/**`

- [ ] **步骤 1：建模 IAM 事实**

为以下对象创建聚合和持久化映射：

| 聚合 | 拥有内容 |
| --- | --- |
| `Organization` | organization id、名称、状态 |
| `Environment` | environment id、organization id、名称、状态 |
| `User` | 登录名、邮箱、密码哈希、启用标志、security stamp |
| `Role` | 角色名称、权限代码 |
| `Membership` | 用户、组织、环境、角色分配 |
| `UserSession` | refresh token 哈希、签发/到期/吊销时间戳、权限版本 |
| `ConnectorHostCredential` | Connector Host 身份、组织、环境、能力范围、secret 哈希、有效范围 |

- [ ] **步骤 2：添加首批 IAM endpoint**

优先实施以下 endpoint：

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

通过轻薄 adapter 使用 ASP.NET Core `PasswordHasher<TUser>`。refresh token 和 Connector Host secret 仅以哈希形式存储。

认证使用短期 JWT Bearer access token 加 refresh token 轮换。不得添加独立的会话认证代码。对于受保护操作，必须根据 IAM 服务端事实验证 `sessionId`、`securityStamp` 和 `permissionVersion` claim。

- [ ] **步骤 3：添加初始种子**

播种：

1. 一个组织。
2. 一个环境。
3. 一个超级管理员用户。
4. 一个平台管理员角色。
5. 为首个本地 Connector Host 创建一个 Connector Host 凭证。

至少播种以下首批权限代码：

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

种子必须按自然键幂等，而不是按生成的数据库 ID 幂等。

- [ ] **步骤 4：添加测试**

覆盖以下场景：

1. 超级管理员可以登录，并收到 access token 和 refresh token。
2. 刷新会轮换 refresh token 并使旧 refresh token 失效。
3. 登出会吊销活动会话。
4. 已禁用用户无法刷新，也无法访问受保护 endpoint。
5. Connector Host 凭证可以验证为 `principalType = connector-host`，并带组织和环境范围。
6. 受保护 endpoint 拒绝过期的 `permissionVersion` 或已吊销的 `sessionId`。

- [ ] **步骤 5：提交**

运行：

```powershell
git add backend/services/Iam
git commit -m "feat: add iam foundation"
```

## 任务 6：实施 AppHub Domain 和 Connector Host 写入 API

**文件：**

- 创建/修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/AggregatesModel/**`
- 创建/修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/DomainEvents/**`
- 创建/修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/**`
- 创建/修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Commands/**`
- 创建/修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Queries/**`
- 创建/修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Endpoints/**`
- 测试：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/**`
- 测试：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/**`

- [ ] **步骤 1：建模 AppHub 事实**

实施以下事实：

| 聚合或实体 | 必需行为 |
| --- | --- |
| `Application` | 按 `applicationKey` 创建或更新；跟踪显示名称和观察到的版本。 |
| `ApplicationVersion` | 按 `applicationKey + version` 创建；不替换先前版本。 |
| `ManagedNode` | 按 `nodeKey` 创建或更新；跟踪部署类型和节点名称。 |
| `ApplicationInstance` | 按 `instanceKey` 创建或更新；拥有当前上报状态和健康状态。 |
| `CapabilityManifest` | 由同一实例的最新注册替换。 |
| `InstanceLiveness` | 仅由心跳更新；保留最近心跳时间和可达性。 |
| `InstanceStateHistory` | 由状态快照追加；仅在状态变化时产生状态变更事件。 |

- [ ] **步骤 2：添加 AppHub 写入 endpoint**

实施：

```text
POST /api/connectors/v1/registrations
POST /api/connectors/v1/heartbeats
POST /api/connectors/v1/state-snapshots
```

每个 endpoint：

1. IAM 任务可用后要求 Connector Host 认证。
2. 接收 `Nerv.IIP.Contracts.ConnectorProtocol` 中的 DTO。
3. 写入 `correlationId`、`organizationId`、`environmentId`、`connectorHostId` 和 `sdkVersion`。
4. 对无效组织、环境、Connector Host 或实例上下文返回与 ProblemDetails 兼容的失败。
5. 要求匹配的权限代码：`connectors.registrations.write`、`connectors.heartbeats.write` 或 `connectors.state-snapshots.write`。

- [ ] **步骤 3：强制执行幂等和状态规则**

实施以下规则：

1. 重复的 `ApplicationRegistration.IdempotencyKey` 返回相同的逻辑注册结果。
2. 心跳更新 `InstanceLiveness`，且不更改 `ApplicationInstance.ReportedStatus`。
3. 状态快照更新 `ReportedStatus`、`HealthStatus` 和状态历史。
4. 仅当上报状态变化时发布 `InstanceStatusChanged`。
5. 任何对外发布的 IntegrationEvent 都必须使用 CAP outbox 或等效可靠发布路径；如果第一次迭代延后外部消费者，保持 converter 和 outbox 形态就绪。

- [ ] **步骤 4：添加 AppHub 内部查询 endpoint**

为 Gateway 实施内部 endpoint：

```text
POST /internal/apphub/v1/instances/query
GET  /internal/apphub/v1/instances/{instanceKey}
```

响应使用 `Nerv.IIP.Contracts.AppHubQueries`。

- [ ] **步骤 5：添加测试**

覆盖以下场景：

1. 注册创建应用、版本、节点、实例和能力行。
2. 使用相同注册幂等 key 重复注册不会创建重复项。
3. 心跳更改存活性并保持上报状态。
4. 第一次状态快照写入状态历史。
5. 状态相同的第二次快照不发布 `InstanceStatusChanged`。
6. 状态发生变化的快照发布一个 `InstanceStatusChanged`。
7. 内部实例查询返回应用、版本、节点、存活性、状态和能力。

- [ ] **步骤 6：提交**

运行：

```powershell
git add backend/services/AppHub
git commit -m "feat: add apphub connector ingestion"
```

## 任务 7：实施 PlatformGateway 实例查询

**文件：**

- 创建/修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/AppHubClient/**`
- 创建/修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Instances/**`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- 测试：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/**`

- [ ] **步骤 1：添加 AppHub HTTP 客户端**

创建调用以下接口的强类型客户端：

```text
POST /internal/apphub/v1/instances/query
GET  /internal/apphub/v1/instances/{instanceKey}
```

使用 `Nerv.IIP.Contracts.AppHubQueries` 中的 DTO。客户端属于 Gateway Web/Application，不引用 AppHub Domain 或 Infrastructure。

- [ ] **步骤 2：添加 Gateway 公开 endpoint**

实施：

```text
GET /api/console/v1/instances
GET /api/console/v1/instances/{instanceKey}
```

列表 endpoint 接收：

```text
organizationId
environmentId
pageNumber
pageSize
search
```

详情 endpoint 接收：

```text
organizationId
environmentId
instanceKey
```

- [ ] **步骤 3：在读取侧添加缓存**

通过 `Nerv.IIP.Caching` 缓存实例列表和详情响应，key 遵循：

```text
gateway:instance-list:{organizationId}:{environmentId}:query:{hash}:v1
gateway:instance-detail:{organizationId}:{environmentId}:instance:{instanceKey}:v1
```

TTL 必须足够短以保持控制台新鲜度；从较小默认值开始，并在 Gateway 后续消费 AppHub 事件时保留显式失效能力。

- [ ] **步骤 4：添加测试**

覆盖以下场景：

1. Gateway 列表 endpoint 将查询参数映射到 `InstanceListQuery`。
2. Gateway 详情 endpoint 返回 AppHub 详情响应，且不泄漏 AppHub 内部路由名称。
3. 如果 AppHub 不可用，Gateway 返回诊断失败。
4. Gateway 项目文件不引用 `Nerv.IIP.AppHub.Domain` 或 `Nerv.IIP.AppHub.Infrastructure`。

- [ ] **步骤 5：提交**

运行：

```powershell
git add backend/gateway/PlatformGateway
git commit -m "feat: add gateway instance queries"
```

## 任务 8：添加缓存和可观测性共享库

**文件：**

- 创建/修改：`backend/common/Caching/Nerv.IIP.Caching/**`
- 创建/修改：`backend/common/Observability/Nerv.IIP.Observability/**`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Program.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`

- [ ] **步骤 1：添加 FusionCache 注册边界**

`Nerv.IIP.Caching` 公开一个服务注册方法：

```csharp
services.AddNervIipCaching(configuration, serviceName);
```

它拥有：

1. FusionCache 注册。
2. Redis 分布式缓存配置。
3. Redis backplane 配置。
4. System.Text.Json 序列化器选项。
5. 缓存行为的 OpenTelemetry hook。
6. 缓存 key 辅助方法。

- [ ] **步骤 2：添加缓存 key 辅助函数**

公开以下辅助函数：

```text
AppHub instance list
AppHub instance detail
Gateway instance list
Gateway instance detail
IAM permission snapshot
```

每个辅助函数必须要求 service、organization id、environment id、resource、stable id 或规范化查询哈希，以及 schema 版本。

- [ ] **步骤 3：添加可观测性注册边界**

`Nerv.IIP.Observability` 公开一个注册方法：

```csharp
services.AddNervIipObservability(configuration, serviceName);
```

它拥有：

1. OpenTelemetry trace、metric 和日志。
2. ASP.NET Core instrumentation。
3. HTTP 客户端 instrumentation。
4. 所选模板包集提供的 netcorepal 和 CAP instrumentation。
5. Correlation id 丰富。
6. 健康和构建信息约定。

- [ ] **步骤 4：接入服务**

从 Iam、AppHub 和 PlatformGateway 调用共享注册方法。服务特定设置保留在 appsettings 或环境配置中，不得硬编码在 endpoint 类中。

- [ ] **步骤 5：添加测试**

覆盖以下场景：

1. 缓存 key 辅助函数为不同组织生成不同 key。
2. 缓存 key 辅助函数为不同 schema 版本生成不同 key。
3. 显式失效后，Gateway 缓存读取返回更新后的结果。
4. 带 `correlationId` 的请求出现在日志和 trace activity tag 中。

- [ ] **步骤 6：提交**

运行：

```powershell
git add backend/common/Caching backend/common/Observability backend/services/Iam backend/services/AppHub backend/gateway/PlatformGateway
git commit -m "feat: add shared caching and observability"
```

## 任务 9：搭建 Connector Host 和 Docker Connector 骨架

**文件：**

- 创建：`connector-hosts/Nerv.IIP.ConnectorHost.sln`
- 创建：`connector-hosts/src/Nerv.IIP.ConnectorHost.Host/**`
- 创建：`connector-hosts/src/Nerv.IIP.ConnectorHost.Application/**`
- 创建：`connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts/**`
- 创建：`connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions/**`
- 创建：`connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker/**`
- 创建：`connector-hosts/tests/Nerv.IIP.ConnectorHost.Application.Tests/**`
- 创建：`connector-hosts/tests/Nerv.IIP.ConnectorHost.Connectors.Docker.Tests/**`

- [ ] **步骤 1：创建 Connector Host 解决方案和项目**

运行：

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

- [ ] **步骤 2：添加引用**

运行：

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

- [ ] **步骤 3：定义 Connector 抽象**

`Nerv.IIP.ConnectorHost.Connectors.Abstractions` 公开：

```text
IConnector
ConnectorTarget
ConnectorCapability
ConnectorStateSnapshot
```

该抽象返回稳定的 `nodeKey`、`applicationKey` 和 `instanceKey` 值。

- [ ] **步骤 4：实施 Docker Connector**

Docker Connector 将一个发现的容器映射为：

1. `nodeKey`：稳定的本地 Docker 节点 key。
2. `applicationKey`：由镜像/repository 派生的 key。
3. `version`：由镜像 tag 或 digest 派生的版本。
4. `instanceKey`：基于容器 id 的稳定 key。
5. capabilities：至少包括 `runtime`、`log` 和 `lifecycle.restart`。

- [ ] **步骤 5：实施 Connector Host 上报循环**

Connector Host：

1. 通过已注册的 Connector 发现目标。
2. 将发现的目标转换为 `ApplicationRegistration`。
3. 通过 `Nerv.IIP.Sdk.ConnectorProtocol` 向 AppHub 发送注册。
4. 通过 `Nerv.IIP.Sdk.ConnectorProtocol` 按固定间隔发送心跳。
5. 启动时以及观察到状态变化时，通过 `Nerv.IIP.Sdk.ConnectorProtocol` 发送状态快照。
6. 使用 IAM Connector Host 凭证认证 AppHub 请求。
7. 为每次注册、心跳和状态快照记录 correlation id。

- [ ] **步骤 6：添加测试**

覆盖以下场景：

1. Connector 输出映射到稳定的 Connector Protocol 注册字段。
2. 上报循环先发送注册，再发送心跳。
3. 上报循环在注册后发送状态快照。
4. 失败的 AppHub 请求会带 correlation id 记录，并在下一循环重试。

- [ ] **步骤 7：提交**

运行：

```powershell
git add connector-hosts
git commit -m "feat: add connector host docker connector"
```

## 任务 10：验证第一阶段纵切

**文件：**

- 创建/修改：`infra/docker-compose.dev.yml`
- 创建/修改：`scripts/verify-first-slice.ps1`
- 创建/修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/appsettings.Development.json`
- 创建/修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/appsettings.Development.json`
- 创建/修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/appsettings.Development.json`
- 创建/修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json`
- 创建/修改：`connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`

- [ ] **步骤 1：添加开发依赖 Compose**

Compose 包含：

```text
PostgreSQL
Redis
RabbitMQ
MinIO
OpenTelemetry collector
```

Qdrant 可以存在于更广泛的开发栈中，但第一阶段纵切不依赖其运行时行为。纳入 MinIO 是为了 FileStorage 就绪；Connector Host 注册纵切不要求对象内容流。

- [ ] **步骤 2：添加验证脚本**

`scripts/verify-first-slice.ps1` 执行：

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
dotnet test backend/Nerv.IIP.sln
dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln
dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
```

然后它在所选执行环境中启动必需服务并验证：

1. AppHub 接受注册。
2. AppHub 接受心跳。
3. AppHub 接受状态快照。
4. PlatformGateway 列表 endpoint 返回已注册实例。
5. PlatformGateway 详情 endpoint 返回上报状态、健康状态、心跳时间戳和能力。

- [ ] **步骤 3：运行纵切验证**

运行：

```powershell
pwsh scripts/verify-first-slice.ps1
```

预期结果：

1. 所有还原、构建和测试命令都以代码 `0` 退出。
2. `GET /api/console/v1/instances` 中出现一个 Docker 目标。
3. 详情查询返回 Connector Host 发送的同一 `instanceKey`。
4. Connector Host、AppHub 和 Gateway 日志为一条请求链共享相同 correlation id。

- [ ] **步骤 4：提交**

运行：

```powershell
git add infra scripts backend connector-hosts
git commit -m "test: verify first vertical slice"
```

## 执行顺序

1. 必须首先执行任务 1。
2. 任务 2 和任务 3 可以在任务 1 之后运行。
3. 任务 4 依赖任务 1。
4. 任务 5 可以在任务 4 之后运行。
5. 任务 6 依赖任务 2、任务 3、任务 4 和任务 5 的 IAM 认证 hook。
6. 任务 7 依赖任务 3、任务 4、任务 6 和任务 8。
7. 任务 8 可以在任务 4 之后运行，但 Gateway 缓存验收在任务 7 之后验证。
8. 任务 9 依赖任务 1 的 SDK 项目、任务 2 的契约和任务 5 的 Connector Host 凭证种子。
9. 任务 10 依赖任务 1 至任务 9。

建议并行执行：

1. 一名执行者实施任务 5 的 IAM。
2. 契约就绪后，一名执行者实施任务 6 的 AppHub。
3. SDK 项目和契约就绪后，一名执行者实施任务 9 的 Connector Host。
4. Gateway 等待 AppHub 查询契约；在 AppHub 运行前，可以使用虚假的 AppHub HTTP handler 继续工作。

## 第一次迭代完成定义

满足以下全部条件时，第一次迭代才算完成：

1. 后端解决方案的 `dotnet restore`、`dotnet build` 和 `dotnet test` 通过。
2. connector-hosts 解决方案的 `dotnet restore`、`dotnet build` 和 `dotnet test` 通过。
3. IAM 可以播种一个管理员和一个 Connector Host 凭证。
4. Platform SDK Core/Auth/ConnectorProtocol/FileStorage 项目存在，且不引用后端服务 Web、Domain、Infrastructure 或数据库模型；`Sdk.Notification` 仍在第一次迭代范围外。
5. FileStorage 服务以 health/build-info 骨架存在，其代码结构记录文件元数据、上传会话、上传指令、下载授权、Upload Provider 抽象、FilePurposePolicy、scanStatus 和对象存储 adapter 边界。
6. Connector Host 可以作为 `principalType = connector-host` 向 AppHub 认证。
7. Connector Host 可以通过 `Nerv.IIP.Sdk.ConnectorProtocol` 发送注册、心跳和状态快照。
8. AppHub 持久化应用、版本、节点、实例、能力、存活性和状态历史事实。
9. PlatformGateway 可以通过 AppHub HTTP/query 契约返回实例列表和详情。
10. Gateway 不引用 AppHub Domain 或 Infrastructure 项目。
11. Connector Host、AppHub 和 Gateway 之间的日志和 trace 可以关联。
12. Ops 服务在此次迭代中仅以 health/build-info 骨架存在。
13. 第一次迭代的任何服务均不实施临时通知表、通知偏好、投递尝试，或直接调用短信/邮件/企业 IM/Webhook provider。

## 自检

规范覆盖：

1. 架构边界：由“边界规则”、任务 4、任务 6、任务 7 和任务 9 覆盖。
2. 可实施性：由具体骨架命令、文件图、任务顺序和验证脚本覆盖。
3. 可维护性和可扩展性：由公共契约、模块化 SDK 边界、缓存边界、可观测性边界，以及 Gateway/AppHub 查询契约分离覆盖。
4. 复杂度控制：Ops 操作闭环、Notification 实施、完整 UI、OAuth/OIDC、SSO、MFA、Knowledge、AI Integration 和高级文件管理工作流均在本计划范围外。
5. 基础管理时序：IAM 基础从任务 5 开始，早于 AppHub Connector Host 认证和 Gateway 受保护查询。

计划自检：

1. 没有任务依赖未定义的服务边界。
2. 第一次迭代的验收项均不依赖 Ops 操作调度。
3. 第一次迭代的验收项均不依赖 Notification 实施。
4. 查询契约名称与任务 3、任务 6 和任务 7 一致。
