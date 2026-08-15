# 可观测性增强实施计划

> **面向智能体工作者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**补充缺失的 SDK 可观测性上下文辅助能力和服务端 OpenTelemetry 跟踪（trace）/指标（metric）基线，不触碰 Notification 或 Ops 业务工作。

**架构：**`Nerv.IIP.Sdk.Observability` 是一个轻量的外部 SDK 模块，仅依赖 `Sdk.Core` 并公开请求上下文辅助能力。`Nerv.IIP.Observability` 仍是服务端共享库，在保留现有 Serilog/关联行为的同时，添加 OpenTelemetry 资源、跟踪、指标，以及 ASP.NET Core、HttpClient 和运行时 instrumentation（插桩）。

**技术栈：**.NET 10、xUnit、Microsoft.Extensions.DependencyInjection、Serilog、OpenTelemetry .NET SDK、Central Package Management（中央包管理）。

---

### 任务 1：SDK 可观测性 MVP

**文件：**
- 修改：`backend/common/Sdk/Nerv.IIP.Sdk.Core/SdkCore.cs`
- 新建：`backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj`
- 新建：`backend/common/Sdk/Nerv.IIP.Sdk.Observability/ObservabilityContext.cs`
- 新建：`backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj`
- 新建：`backend/tests/Nerv.IIP.Sdk.Observability.Tests/ObservabilityContextTests.cs`

- [ ] **步骤 1：编写预期失败的 SDK 测试**

创建 `backend/tests/Nerv.IIP.Sdk.Observability.Tests/ObservabilityContextTests.cs`，测试关联 ID 生成、显式关联/幂等键保留，以及把 `Activity.Current.Id` 捕获为 `TraceParent`。

- [ ] **步骤 2：运行 SDK 测试并确认失败**

运行：`dotnet test backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj`

实施前预期：由于项目/类型尚不存在而编译失败，或由于缺少相应行为而测试失败。

- [ ] **步骤 3：实现最小 SDK API**

添加 `PlatformRequestContext` 到 `Sdk.Core`：

```csharp
public sealed record PlatformRequestContext(
    string OrganizationId,
    string EnvironmentId,
    string CorrelationId,
    string? IdempotencyKey = null,
    string? TraceParent = null);
```

创建 `ObservabilityContext.CreateRequestContext(...)`：

```csharp
public static PlatformRequestContext CreateRequestContext(
    string organizationId,
    string environmentId,
    string? correlationId = null,
    string? idempotencyKey = null)
```

规则：验证组织/环境不为空；未提供关联 ID 时用 `Guid.NewGuid().ToString("n")` 生成；使用 `Activity.Current?.Id` 作为父级跟踪上下文；不得依赖服务端 `Nerv.IIP.Observability`。

- [ ] **步骤 4：运行 SDK 测试并确认通过**

运行：`dotnet test backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj`

预期：所有 SDK 可观测性测试均通过。

### 任务 2：服务端 OpenTelemetry 跟踪与指标基线

**文件：**
- 修改：`backend/Directory.Packages.props`
- 修改：`backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj`
- 修改：`backend/common/Observability/Nerv.IIP.Observability/NervIipObservability.cs`
- 新建：`backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj`
- 新建：`backend/tests/Nerv.IIP.Observability.Tests/NervIipObservabilityRegistrationTests.cs`

- [ ] **步骤 1：编写预期失败的服务端可观测性测试**

创建测试，调用 `new ServiceCollection().AddNervIipObservability(configuration, "unit-test-service")`、构建服务提供程序，并断言：

- `NervIipObservabilityOptions.ServiceName` 等于 `unit-test-service`。
- 启用可观测性时已注册 OpenTelemetry 托管服务。
- 可通过 `OpenTelemetry:Enabled=false` 禁用 OpenTelemetry，同时保留现有选项/日志注册。

- [ ] **步骤 2：运行服务端测试并确认失败**

运行：`dotnet test backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj`

实施前预期：由于测试项目尚不存在而编译失败，或由于缺少 OpenTelemetry 服务而断言失败。

- [ ] **步骤 3：添加 OpenTelemetry 依赖和注册**

添加以下中央包版本：

```xml
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.15.3" />
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.15.2" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.15.1" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />
```

从 `Nerv.IIP.Observability.csproj` 引用这些包。

在 `AddNervIipObservability` 中保留 Serilog 行为，并在 `OpenTelemetry:Enabled` 不为 `false` 时添加 OpenTelemetry 注册：

- 资源服务名
- 跟踪：ASP.NET Core、HttpClient，以及配置终结点（endpoint）时的 OTLP 导出器（exporter）
- 指标：ASP.NET Core、HttpClient、运行时（runtime），以及配置终结点时的 OTLP 导出器
- 终结点取自 `OTEL_EXPORTER_OTLP_ENDPOINT`、`OpenTelemetry:Endpoint` 或 `Logging:OpenTelemetry:Endpoint`
- 协议取自 `OpenTelemetry:Protocol` 或 `Logging:OpenTelemetry:Protocol`，保留当前的 4318/http 自动检测

- [ ] **步骤 4：运行服务端测试并确认通过**

运行：`dotnet test backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj`

预期：所有服务端可观测性测试均通过。

### 任务 3：解决方案集成与验证

**文件：**
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：将新项目加入解决方案**

运行：

```powershell
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj --solution-folder common/Sdk
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj --solution-folder tests
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj --solution-folder tests
```

- [ ] **步骤 2：运行专项验证**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Observability.Tests/Nerv.IIP.Sdk.Observability.Tests.csproj
dotnet test backend/tests/Nerv.IIP.Observability.Tests/Nerv.IIP.Observability.Tests.csproj
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期：所有命令均以退出码 0 结束。

- [ ] **步骤 3：审查合并冲突面**

运行：`git diff --name-status`

预期：改动文件仍局限于 SDK 可观测性、服务端可观测性、`Sdk.Core`、中央包、新测试、解决方案和本计划。
