# FileStorage MVP 补全实施计划

> **面向智能代理工作者：** 必须使用子技能：采用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：** 在元数据/schema 基线之后补全 FileStorage MVP：加入稳定的公共契约、由 PostgreSQL 支持的 API 行为，并以 tus 作为 MVP 二进制传输路径。

**架构：** FileStorage 保留 `server-proxy` 元数据桩作为首个纵切，现转向持久化的 PostgreSQL 事实与稳定的 SDK 边界。tus 是 MVP 的完整上传/下载传输能力；MinIO/S3 multipart 明确属于 MVP 后的部署集成。公共契约绝不暴露 `objectKey` 或 `object_key`。

**技术栈：** .NET 10、FastEndpoints、EF Core PostgreSQL、xUnit、Platform SDK、tus 协议/提供程序抽象、Nerv.IIP.Testing schema 约定辅助工具。

---

## 文件结构

新增：

1. `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/Nerv.IIP.Contracts.FileStorage.csproj` - 公共 FileStorage DTO 软件包。
2. `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/FileStorageContracts.cs` - 由 SDK 和 Web 边界测试共享的请求/响应 DTO。
3. `backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj` - JSON 契约测试项目。
4. `backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/FileStorageContractJsonTests.cs` - 验证 Web JSON 名称以及不暴露 `objectKey`。
5. `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageClient.cs` - `IFileStorageClient` 和 `HttpFileStorageClient`。
6. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs` - 现有 API 行为的 PostgreSQL 持久化实现。
7. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/UploadProviders/FileStorageUploadProvider.cs` - 供 server-proxy 和 tus 使用的提供程序抽象。
8. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/UploadProviders/TusUploadProvider.cs` - MVP tus 提供程序形状。
9. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStoragePostgreSqlServiceTests.cs` - 持久化行为测试；如可用，使用 EF 内存 SQLite 或轻量提供程序测试 DbContext。
10. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs` - tus 指令与完成行为测试。

修改：

1. `backend/Nerv.IIP.sln` - 添加 FileStorage 契约和测试。
2. `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj` - 引用 `Contracts.FileStorage`。
3. `backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageSdk.cs` - 保留向后兼容的别名，或将骨架记录类型移到契约之后。
4. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs` - 使用契约，或与契约 DTO 名称保持一致。
5. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileStorageEndpoints.cs` - 使用契约 DTO。
6. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs` - 根据 `Persistence:Provider` 选择内存或 PostgreSQL 服务；注册上传提供程序。
7. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Records/*.cs` - 添加 PostgreSQL 服务所需的工厂方法/构造函数。
8. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/*.cs` - 仅在服务实现表明确实缺少列或关系时更新模型。
9. `docs/architecture/file-storage-baseline.md` - 仅在代码证据存在后更新。
10. `docs/architecture/platform-sdk-baseline.md` - 契约/客户端落地后更新 SDK 状态。

## 任务 1：契约与 SDK 边界

**文件：**
- 新增：`backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/Nerv.IIP.Contracts.FileStorage.csproj`
- 新增：`backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/FileStorageContracts.cs`
- 新增：`backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj`
- 新增：`backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/FileStorageContractJsonTests.cs`
- 修改：`backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj`
- 修改：`backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageSdk.cs`
- 新增：`backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/FileStorageClient.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：编写会失败的契约 JSON 测试**

添加 `FileStorageContractJsonTests`，分别对 `CreateUploadSessionResponse`、`FileMetadataResponse` 和 `DownloadGrantResponse` 做一次往返测试。断言 Web JSON 包含 `uploadSessionId`、`uploadMode`、`download`、`fileId`，且不包含 `objectKey` 或 `object_key`。

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj --no-restore
```

预期：失败，因为契约项目尚不存在。

- [ ] **步骤 2：添加公共契约**

创建 DTO：

```csharp
namespace Nerv.IIP.Contracts.FileStorage;

public sealed record OwnerReference(string OwnerService, string OwnerType, string OwnerId);
public sealed record TransferInstructions(string Url, IReadOnlyDictionary<string, string> Headers);

public sealed record CreateUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string? Checksum);

public sealed record CompleteUploadSessionRequest(
    string OrganizationId,
    string EnvironmentId,
    string FilePurpose,
    string? Checksum = null,
    long? SizeBytes = null);

public sealed record CreateDownloadGrantRequest(string OrganizationId, string EnvironmentId);

public sealed record CreateUploadSessionResponse(
    string UploadSessionId,
    string FileId,
    string UploadMode,
    string Provider,
    DateTimeOffset ExpiresAtUtc,
    TransferInstructions Upload);

public sealed record FileMetadataResponse(
    string FileId,
    string OrganizationId,
    string EnvironmentId,
    OwnerReference Owner,
    string FilePurpose,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Checksum,
    string ScanStatus,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record DownloadGrantResponse(
    string FileId,
    DateTimeOffset ExpiresAtUtc,
    TransferInstructions Download);
```

- [ ] **步骤 3：添加 SDK 客户端**

添加：

```csharp
public interface IFileStorageClient
{
    Task<CreateUploadSessionResponse> CreateUploadSessionAsync(CreateUploadSessionRequest request, CancellationToken cancellationToken = default);
    Task<FileMetadataResponse> CompleteUploadSessionAsync(string uploadSessionId, CompleteUploadSessionRequest request, CancellationToken cancellationToken = default);
    Task<FileMetadataResponse> GetFileMetadataAsync(string fileId, CancellationToken cancellationToken = default);
    Task<DownloadGrantResponse> CreateDownloadGrantAsync(string fileId, CreateDownloadGrantRequest request, CancellationToken cancellationToken = default);
}
```

`HttpFileStorageClient` 必须调用现有四个 `/api/files/v1/**` 端点，并使用 `Uri.EscapeDataString` 转义路由 ID。

- [ ] **步骤 4：验证契约和 SDK 构建**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj --no-restore
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.FileStorage/Nerv.IIP.Sdk.FileStorage.csproj --no-restore
```

预期：通过。

## 任务 2：在 FileStorage Web 边界使用契约

**文件：**
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileStorageEndpoints.cs`
- 修改：`backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageSkeletonTests.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Nerv.IIP.FileStorage.Web.csproj`

- [ ] **步骤 1：添加会失败的 Web 契约对齐测试**

更新 API 测试，使用 `Nerv.IIP.Contracts.FileStorage` DTO 反序列化响应。实施前预期：编译失败，因为 Web 尚未引用契约。

- [ ] **步骤 2：用契约替换本地公共 DTO**

在 FastEndpoints 和服务接口中使用契约请求/响应类型。`FileStorageResult<T>` 保持为 Web 内部类型。内部领域 `OwnerReference` 和 `FileMetadata` 映射保持私有；公共响应使用契约 `OwnerReference`。

- [ ] **步骤 3：重新运行 FileStorage 测试**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

预期：现有 4 个测试全部通过。

## 任务 3：由 PostgreSQL 支持的 API 服务

**文件：**
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Records/*.cs`
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- 新增：`backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStoragePostgreSqlServiceTests.cs`

- [ ] **步骤 1：编写会失败的持久化行为测试**

添加使用真实 EF 服务提供程序和 `ApplicationDbContext` 的测试，并断言：

```text
CreateUploadSession persists upload_sessions.
CompleteUploadSession marks upload session completed and inserts stored_files.
GetFileMetadata reads stored_files.
CreateDownloadGrant inserts download_grants.
object_key is not present in public response JSON.
```

实施前预期：失败，因为尚无 PostgreSQL 持久化服务。

- [ ] **步骤 2：添加记录类型工厂方法**

为 `StoredFileRecord`、`UploadSessionRecord` 和 `DownloadGrantRecord` 添加显式公共静态工厂方法；赋值器保持私有，供 EF 具体化使用。

- [ ] **步骤 3：实现 `PostgreSqlFileStorageService`**

通过 `ApplicationDbContext` 使用异步 EF 调用和取消令牌。行为与当前内存服务保持等价：验证、过期检查、上下文不匹配检查、内部对象键生成，以及 server-proxy 占位 URL。

- [ ] **步骤 4：按提供程序注册**

在 `Program.cs` 中注册：

```text
Persistence:Provider=PostgreSQL -> PostgreSqlFileStorageService
default/InMemory -> InMemoryFileStorageService
```

默认测试不得要求真实 PostgreSQL 连接。

- [ ] **步骤 5：验证**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：通过。

## 任务 4：Tus MVP 提供程序形状

**文件：**
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/UploadProviders/FileStorageUploadProvider.cs`
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/UploadProviders/TusUploadProvider.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs`
- 新增：`backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs`

- [ ] **步骤 1：编写会失败的 tus 提供程序测试**

断言选择 tus 提供程序后返回：

```text
uploadMode = tus
provider = tus
upload.url = /api/files/v1/tus/{uploadSessionId}
headers include x-nerv-upload-mode = tus
```

实施前预期：失败，因为当前仅存在 server-proxy。

- [ ] **步骤 2：添加提供程序抽象**

定义：

```csharp
public interface IFileStorageUploadProvider
{
    string Provider { get; }
    string UploadMode { get; }
    TransferInstructions CreateUploadInstructions(string uploadSessionId, string fileId);
}
```

- [ ] **步骤 3：实现 tus 提供程序**

添加仅创建平台自有 tus 指令的 `TusUploadProvider`。不得添加 MinIO/S3 multipart。不得暴露对象键。

- [ ] **步骤 4：接入选择逻辑**

默认提供程序保持为 `server-proxy`，直到配置指定 `FileStorage:UploadProvider=tus`。默认情况下保持现有测试不变。

- [ ] **步骤 5：验证**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：通过。

## 任务 5：文档与最终验证

**文件：**
- 修改：`README.md`
- 修改：`docs/architecture/file-storage-baseline.md`
- 修改：`docs/architecture/platform-sdk-baseline.md`
- 修改：`docs/architecture/api-contract-and-codegen.md`
- 修改：`docs/architecture/implementation-readiness.md`

- [ ] **步骤 1：根据实际差异更新文档**

只记录已完成的行为：

```text
Contracts/SDK landed.
PostgreSQL-backed API service landed if Task 3 completed.
tus landed if Task 4 completed.
MinIO/S3 multipart remains post-MVP.
```

- [ ] **步骤 2：运行最终验证**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.FileStorage.Tests/Nerv.IIP.Contracts.FileStorage.Tests.csproj --no-restore
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：全部通过。

## 自查

规格覆盖：

1. 公共契约和 SDK 边界由任务 1 覆盖。
2. Web 边界对齐由任务 2 覆盖。
3. PostgreSQL 持久化 API 行为由任务 3 覆盖。
4. tus 作为 MVP 完整传输路径由任务 4 覆盖。
5. 所有实施任务都排除 MinIO/S3 multipart，并将其记录为 MVP 后事项。
6. 文档与验证由任务 5 覆盖。

占位符扫描：没有遗留 TODO/TBD 占位符；每项任务都有具体文件、行为和命令。

类型一致性：DTO 名称符合现有 API 语义，并确保公共契约中没有 `objectKey/object_key`。
