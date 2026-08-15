# 业务主平台集成就绪实施计划

> **面向智能体执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**使最小化的主平台 SDK 和公开契约为业务平台实施做好准备，同时不把 SDK 变成业务运行时。

**架构：**将业务服务保持为独立的 CleanDDD 服务，仅通过公开 API、公开契约、集成事件和 IAM 授权上下文消费主平台能力。强化 `Sdk.Core`、`Sdk.Auth`、`Sdk.FileStorage`，添加最小化的 `Sdk.Notification` 和 `Sdk.Observability`，并为遥测及 WCS 回调添加业务连接器摄取契约。不得将 ERP、MES、WMS、MRP、PDM/PLM、IIoT 或 CMMS 领域规则放入主平台 SDK。

**技术栈：**.NET 10、HttpClient、System.Text.Json、xUnit、FastEndpoints 契约测试，以及现有 IAM、FileStorage（文件存储）、AppHub（应用中心）、Ops（运维）和 Notification（通知）边界。

---

## 制定本计划的原因

业务平台切片可以从领域服务开始，但后续切片需要稳定的主平台接入点：

1. ProductEngineering（产品工程）和 Quality（质量）需要文件存储引用以及上传/下载客户端支持。
2. IndustrialTelemetry（工业遥测）和 WMS 自动化需要 Connector Host（连接器宿主）或外部客户端写入认证。
3. BusinessApproval（业务审批）、EngineeringChange（工程变更）、MRP、Maintenance（维护）和 WMS 失败场景需要 Notification（通知）意图支持。
4. 跨服务验收需要传播关联 ID、跟踪上下文、组织/环境请求头和幂等键。
5. 业务服务需要 IAM 授权上下文和权限检查，但不得直接读取 IAM 数据表。

本计划是遥测密集型工作和全链路工作开始前的就绪门禁。MasterData（主数据）可以在本计划完全实施前启动，但全链路验收必须在本计划通过后才能开始。

## 边界

1. 不得向 SDK 模块添加业务领域概念。
2. 不得让 SDK 直接写入最终平台事实；所有写入都必须通过公开 API。
3. 不得让 Connector Host（连接器宿主）在写入遥测、报警或 WCS 回调时绕过 IAM 授权。
4. 不得通过 SDK DTO 暴露对象存储键、长期下载 URL、刷新令牌或服务数据库标识符。
5. 不得让 `PlatformGateway` 或 `Platform SDK` 持有业务平台规则。

## 文件结构图

```text
backend/common/Sdk/Nerv.IIP.Sdk.Core/
  SdkCore.cs
  PlatformApiClient.cs
  PlatformRequestContext.cs

backend/common/Sdk/Nerv.IIP.Sdk.Auth/
  SdkAuth.cs
  PlatformTokenAuthentication.cs

backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/
  FileStorageSdk.cs
  FileStorageClient.cs

backend/common/Sdk/Nerv.IIP.Sdk.Notification/
  Nerv.IIP.Sdk.Notification.csproj
  NotificationClient.cs

backend/common/Sdk/Nerv.IIP.Sdk.Observability/
  Nerv.IIP.Sdk.Observability.csproj
  ObservabilityContext.cs

backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/
  Nerv.IIP.Contracts.BusinessIntegration.csproj
  BusinessTelemetryContracts.cs
  BusinessWcsContracts.cs
  BusinessNotificationContracts.cs

backend/tests/Nerv.IIP.Sdk.Tests/
  Nerv.IIP.Sdk.Tests.csproj
  PlatformApiClientTests.cs
  PlatformTokenAuthenticationTests.cs
  FileStorageClientTests.cs
  NotificationClientTests.cs
  ObservabilityContextTests.cs
  BusinessIntegrationContractJsonTests.cs

docs/architecture/platform-sdk-baseline.md
docs/architecture/business-platform-domain-architecture.md
docs/superpowers/specs/2026-05-20-business-platform-domain-design.md
README.md
scripts/verify-business-main-platform-integration-readiness.ps1
```

## 任务 1：强化 SDK 核心请求上下文

**文件：**

- 修改：`backend/common/Sdk/Nerv.IIP.Sdk.Core/SdkCore.cs`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Core/PlatformApiClient.cs`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Core/PlatformRequestContext.cs`
- 创建：`backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj`
- 创建：`backend/tests/Nerv.IIP.Sdk.Tests/PlatformApiClientTests.cs`
- 创建：`backend/tests/Nerv.IIP.Sdk.Tests/ObservabilityContextTests.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建 SDK 测试项目**

运行：

```powershell
dotnet new xunit -n Nerv.IIP.Sdk.Tests -o backend/tests/Nerv.IIP.Sdk.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj
```

预期结果：项目已添加到 `backend/Nerv.IIP.sln`。

- [ ] **步骤 2：编写失败的请求上下文测试**

创建测试，断言 `PlatformApiClient` 应用以下请求头：

```text
X-Nerv-IIP-Sdk-Version
X-Organization-Id
X-Environment-Id
X-Correlation-Id
Idempotency-Key
traceparent
```

测试请求上下文如下：

```csharp
var context = new PlatformRequestContext(
    OrganizationId: "org-001",
    EnvironmentId: "env-dev",
    CorrelationId: "corr-001",
    IdempotencyKey: "idem-001",
    TraceParent: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00");
```

预期结果：失败，因为 `PlatformApiClient` 和 `PlatformRequestContext` 尚不存在。

- [ ] **步骤 3：实现请求上下文和客户端辅助工具**

添加：

```csharp
public sealed record PlatformRequestContext(
    string OrganizationId,
    string EnvironmentId,
    string? CorrelationId = null,
    string? IdempotencyKey = null,
    string? TraceParent = null);

public static class PlatformApiClient
{
    public static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        PlatformApiOptions options,
        PlatformRequestContext context);
}
```

规则：

1. `OrganizationId` 和 `EnvironmentId` 为必填项。
2. 省略 `CorrelationId` 时生成该值。
3. 仅在提供值时添加 `Idempotency-Key`。
4. 仅在提供值时添加 `traceparent`。
5. `X-Nerv-IIP-Sdk-Version` 始终来自 `PlatformApiOptions.SdkVersion`。

- [ ] **步骤 4：运行核心测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter "FullyQualifiedName~PlatformApiClientTests|FullyQualifiedName~ObservabilityContextTests"
```

预期结果：通过。

- [ ] **步骤 5：提交核心就绪实现**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/common/Sdk/Nerv.IIP.Sdk.Core backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: add platform sdk request context"
```

## 任务 2：扩展 SDK 认证，使其不限于 Connector Host（连接器宿主）请求头

**文件：**

- 修改：`backend/common/Sdk/Nerv.IIP.Sdk.Auth/SdkAuth.cs`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Auth/PlatformTokenAuthentication.cs`
- 创建：`backend/tests/Nerv.IIP.Sdk.Tests/PlatformTokenAuthenticationTests.cs`

- [ ] **步骤 1：编写失败的认证测试**

测试必须涵盖：

```csharp
PlatformBearerToken.Apply(request, "access-token-001");
ExternalClientCredential.Apply(request, new ExternalClientCredential("client-001", "secret-001", "org-001", "env-dev"));
ConnectorHostAuthentication.Apply(request, new ConnectorHostCredential("host-001", "secret-001", "org-001", "env-dev"));
```

预期请求头：

| 方法 | 必需请求头 |
| --- | --- |
| `PlatformBearerToken.Apply` | `Authorization: Bearer access-token-001` |
| `ExternalClientCredential.Apply` | `Authorization: ExternalClient client-001`, `X-External-Client-Id`, `X-External-Client-Secret`、组织、环境 |
| `ConnectorHostAuthentication.Apply` | 现有 Connector Host（连接器宿主）请求头保持不变 |

预期结果：失败，因为持有者令牌/外部客户端辅助工具尚不存在。

- [ ] **步骤 2：实现认证辅助工具**

添加不可变记录和静态辅助方法。使用 `PlatformApiResult<T>.Failure(...)` 校验空白令牌、客户端密钥或连接器密钥。

- [ ] **步骤 3：运行认证测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter FullyQualifiedName~PlatformTokenAuthenticationTests
```

预期结果：通过。

- [ ] **步骤 4：提交认证就绪实现**

运行：

```powershell
git add backend/common/Sdk/Nerv.IIP.Sdk.Auth backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: extend platform sdk auth helpers"
```

## 任务 3：实现最小化文件存储 SDK 客户端

**文件：**

- 修改：`backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageSdk.cs`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageClient.cs`
- 创建：`backend/tests/Nerv.IIP.Sdk.Tests/FileStorageClientTests.cs`

- [ ] **步骤 1：编写失败的文件存储客户端测试**

使用模拟的 `HttpMessageHandler` 并断言：

```csharp
await client.CreateUploadSessionAsync(new CreateUploadSessionRequest(
    "engineering-document",
    "pump.dwg",
    "application/acad",
    1024), context, cancellationToken);

await client.CompleteUploadAsync("upload-session-001", "file-001", context, cancellationToken);

await client.CreateDownloadGrantAsync("file-001", "engineering-preview", context, cancellationToken);
```

预期路由：

| 方法 | 路由 |
| --- | --- |
| POST | `/api/files/v1/upload-sessions` |
| POST | `/api/files/v1/upload-sessions/{uploadSessionId}/complete` |
| POST | `/api/files/v1/files/{fileId}/download-grants` |

预期结果：失败，因为 `FileStorageClient` 不存在。

- [ ] **步骤 2：实现文件存储客户端契约**

使用以下请求/响应记录：

```csharp
public sealed record CreateUploadSessionRequest(string Purpose, string FileName, string ContentType, long SizeBytes);
public sealed record UploadSessionResponse(string UploadSessionId, IReadOnlyCollection<UploadInstruction> Instructions);
public sealed record CompleteUploadRequest(string FileId);
public sealed record CreateDownloadGrantRequest(string Purpose);
```

返回现有 `FileReference`、`UploadInstruction` 和 `DownloadGrant` 记录。不得暴露对象存储键或长期 URL。

- [ ] **步骤 3：运行文件存储 SDK 测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter FullyQualifiedName~FileStorageClientTests
```

预期结果：通过。

- [ ] **步骤 4：提交文件存储就绪实现**

运行：

```powershell
git add backend/common/Sdk/Nerv.IIP.Sdk.FileStorage backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: add file storage sdk client"
```

## 任务 4：添加最小化通知和可观测性 SDK

**文件：**

- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Notification/NotificationClient.cs`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj`
- 创建：`backend/common/Sdk/Nerv.IIP.Sdk.Observability/ObservabilityContext.cs`
- 创建：`backend/tests/Nerv.IIP.Sdk.Tests/NotificationClientTests.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建 SDK 项目**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Sdk.Notification -o backend/common/Sdk/Nerv.IIP.Sdk.Notification --framework net10.0
dotnet new classlib -n Nerv.IIP.Sdk.Observability -o backend/common/Sdk/Nerv.IIP.Sdk.Observability --framework net10.0
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj
dotnet add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj
dotnet add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj
```

- [ ] **步骤 2：编写失败的通知客户端测试**

断言 `NotificationClient.SubmitIntentAsync(...)` 使用以下请求向 `/api/notifications/v1/intents` 发起 POST：

```csharp
public sealed record SubmitNotificationIntentRequest(
    string IntentType,
    string Severity,
    string ResourceType,
    string ResourceId,
    string Title,
    string Summary,
    IReadOnlyCollection<string> SuggestedRecipientRefs);
```

客户端必须包含来自 `PlatformRequestContext` 的组织、环境、关联和幂等请求头。

- [ ] **步骤 3：实现通知和可观测性 SDK**

`ObservabilityContext` 提供：

```csharp
public static PlatformRequestContext CreateRequestContext(
    string organizationId,
    string environmentId,
    string? idempotencyKey = null);
```

它将 `Activity.Current?.Id` 读入 `TraceParent`，并在调用方未提供关联 ID 时生成该值。

- [ ] **步骤 4：运行测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter "FullyQualifiedName~NotificationClientTests|FullyQualifiedName~ObservabilityContextTests"
```

预期结果：通过。

- [ ] **步骤 5：提交通知和可观测性就绪实现**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/common/Sdk/Nerv.IIP.Sdk.Notification backend/common/Sdk/Nerv.IIP.Sdk.Observability backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: add notification and observability sdk minimum"
```

## 任务 5：为连接器场景添加业务集成契约

**文件：**

- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/Nerv.IIP.Contracts.BusinessIntegration.csproj`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/BusinessTelemetryContracts.cs`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/BusinessWcsContracts.cs`
- 创建：`backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/BusinessNotificationContracts.cs`
- 创建：`backend/tests/Nerv.IIP.Sdk.Tests/BusinessIntegrationContractJsonTests.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建契约项目**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.BusinessIntegration -o backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration --framework net10.0
dotnet add backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/Nerv.IIP.Contracts.BusinessIntegration.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/Nerv.IIP.Contracts.BusinessIntegration.csproj
```

- [ ] **步骤 2：添加遥测契约**

创建：

```csharp
public sealed record CreateTelemetryTagRequest(string DeviceAssetId, string TagKey, string ValueType, string Unit, string SamplingPolicy);
public sealed record RecordTelemetrySampleRequest(string TagId, string Value, DateTimeOffset OccurredAtUtc, string SourceSequence);
public sealed record RaiseAlarmRequest(string DeviceAssetId, string AlarmCode, string Severity, DateTimeOffset OccurredAtUtc, string ExternalAlarmId);
```

这些契约供 IndustrialTelemetry（工业遥测）公开 API 使用。它们不包含 PLC/DCS 控制命令。

- [ ] **步骤 3：添加 WCS 回调契约**

创建：

```csharp
public sealed record DispatchWcsTaskRequest(string WarehouseTaskId, string AdapterType, string PayloadJson);
public sealed record CompleteWcsTaskRequest(string ExternalTaskId, string ResultCode, DateTimeOffset OccurredAtUtc, string? DiagnosticMessage);
public sealed record FailWcsTaskRequest(string ExternalTaskId, string FailureCode, string DiagnosticMessage, DateTimeOffset OccurredAtUtc);
```

这些契约供 WMS 公开 API 和连接器回调使用。它们不建模 WCS 内部调度。

- [ ] **步骤 4：添加 JSON 兼容性测试**

使用 `JsonSerializerDefaults.Web` 序列化每个契约。断言 JSON 包含 `deviceAssetId`、`sourceSequence`、`externalTaskId`、`failureCode` 等 camelCase 名称。

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore --filter FullyQualifiedName~BusinessIntegrationContractJsonTests
```

预期结果：通过。

- [ ] **步骤 5：提交契约**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration backend/tests/Nerv.IIP.Sdk.Tests
git commit -m "feat: add business integration connector contracts"
```

## 任务 6：更新文档和验证

**文件：**

- 修改：`docs/architecture/platform-sdk-baseline.md`
- 修改：`docs/architecture/business-platform-domain-architecture.md`
- 修改：`docs/superpowers/specs/2026-05-20-business-platform-domain-design.md`
- 创建：`scripts/verify-business-main-platform-integration-readiness.ps1`
- 修改：`README.md`

- [ ] **步骤 1：更新 SDK 基线文档**

记录以下项目的当前实施状态：

1. `Sdk.Core` 请求上下文和请求头注入。
2. `Sdk.Auth` 持有者令牌、外部客户端和 Connector Host（连接器宿主）辅助工具。
3. `Sdk.FileStorage` 上传/下载客户端。
4. `Sdk.Notification` 通知意图客户端。
5. `Sdk.Observability` 关联和跟踪上下文辅助工具。
6. `Contracts.BusinessIntegration` 遥测和 WCS 回调契约。

- [ ] **步骤 2：更新业务平台文档**

在业务平台架构和规格交接说明中添加一句话：业务切片通过本就绪计划消费主平台能力，并且不得引用主平台服务的领域或基础设施项目。

- [ ] **步骤 3：添加验证脚本**

脚本运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Sdk.Tests/Nerv.IIP.Sdk.Tests.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Auth/Nerv.IIP.Sdk.Auth.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Observability/Nerv.IIP.Sdk.Observability.csproj --no-restore
dotnet build backend/common/Contracts/Nerv.IIP.Contracts.BusinessIntegration/Nerv.IIP.Contracts.BusinessIntegration.csproj --no-restore
```

- [ ] **步骤 4：运行最终验证**

运行：

```powershell
scripts/verify-business-main-platform-integration-readiness.ps1
git diff --check
```

预期结果：两条命令均以 `0` 退出。

- [ ] **步骤 5：提交文档和验证**

运行：

```powershell
git add docs/architecture/platform-sdk-baseline.md docs/architecture/business-platform-domain-architecture.md docs/superpowers/specs/2026-05-20-business-platform-domain-design.md scripts/verify-business-main-platform-integration-readiness.ps1 README.md
git commit -m "docs: record business platform integration readiness"
```

## 自审清单

1. 任何 SDK 模块都不得引用 `backend/services/*` 或 `backend/gateway/*` 的 Web、领域或基础设施项目。
2. 除遥测和 WCS 回调的中性集成契约外，任何业务领域类型都不得出现在 SDK 模块名或 DTO 名称中。
3. 文件存储 SDK 仅返回文件 ID、引用和短期授权。
4. 通知 SDK 仅提交意图；它不实现交付提供程序。
5. Connector Host（连接器宿主）和外部客户端场景仍需要通过 IAM 认证的公开 API。
6. 全链路验收将此就绪脚本列为前置条件。
