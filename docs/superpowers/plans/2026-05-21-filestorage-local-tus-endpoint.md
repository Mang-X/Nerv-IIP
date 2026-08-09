# FileStorage 本地 tus 端点实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**新增由 FileStorage 负责的最小 tus 上传端点和下载内容端点，且不引入 MinIO/S3 分段上传。

**架构：**FileStorage 继续作为上传会话创建和完成的元数据权威来源。选择 `FileStorage:UploadProvider=tus` 时，客户端可以使用 `HEAD` 读取当前上传偏移量，并使用 `PATCH` 将字节追加到本地文件系统存储。下载授权通过 FileStorage 从同一本地存储返回已完成的字节内容。

**技术栈：**.NET 10、FastEndpoints、xUnit、ASP.NET Core `WebApplicationFactory`、本地文件系统存储。

---

## 文件结构

创建：

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/Tus/LocalTusFileStore.cs`——本地临时/已完成字节存储和偏移量操作。
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/TusFileEndpoints.cs`——`HEAD` 和 `PATCH` 上传端点，以及下载内容端点。

修改：

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`——为 tus 端点暴露上传会话/文件查询，并登记已完成的本地内容。
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/PostgreSqlFileStorageService.cs`——保持编译兼容；本地 tus 端点可以依赖 `IFileStorageService` 和一个小型 tus 感知接口。
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`——注册本地 tus 存储单例。
4. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs`——新增端点工作流测试。
5. `docs/architecture/file-storage-baseline.md` 和 `docs/architecture/implementation-readiness.md`——代码验证后记录最小 tus 端点行为。

## 任务 1：最小 tus 上传端点

**文件：**
- 创建：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/Tus/LocalTusFileStore.cs`
- 创建：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/TusFileEndpoints.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- 测试：`backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs`

- [ ] **步骤 1：编写会失败的端点工作流测试**

新增一个测试：创建 tus 上传会话，调用 `HEAD /api/files/v1/tus/{uploadSessionId}` 并预期得到 `Upload-Offset: 0`；再调用 `PATCH`，携带 `Tus-Resumable: 1.0.0`、`Upload-Offset: 0`、`Content-Type: application/offset+octet-stream`，并预期 `Upload-Offset` 按字节数增加。

- [ ] **步骤 2：验证测试为红**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore --filter TusUploadEndpoint_HeadAndPatch_TracksOffset
```

预期：因 404 或缺少端点而失败。

- [ ] **步骤 3：实施本地 tus 存储和端点**

为 `LocalTusFileStore` 实施以下能力：

```csharp
public sealed class LocalTusFileStore(IConfiguration configuration)
{
    public long GetOffset(string uploadSessionId);
    public Task<long> AppendAsync(string uploadSessionId, long expectedOffset, Stream content, CancellationToken cancellationToken);
    public FileStream OpenRead(string uploadSessionId);
}
```

实施端点：

```text
HEAD  /api/files/v1/tus/{uploadSessionId}
PATCH /api/files/v1/tus/{uploadSessionId}
```

规则：

1. `HEAD` 返回 `Tus-Resumable: 1.0.0`、`Upload-Offset` 和 `Cache-Control: no-store`。
2. `PATCH` 要求 `Upload-Offset` 匹配；不匹配时返回 `409 Conflict`，并携带当前 `Upload-Offset`。
3. `PATCH` 追加字节并返回 `204 NoContent`，同时携带新的 `Upload-Offset`。
4. 端点只使用本地存储；不实施 MinIO/S3 分段上传。

- [ ] **步骤 4：验证测试为绿**

先运行上面的聚焦测试，然后运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

预期：通过。

## 任务 2：下载内容端点

**文件：**
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Files/InMemoryFileStorageService.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/TusFileEndpoints.cs`
- 测试：`backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageTusProviderTests.cs`

- [ ] **步骤 1：编写会失败的下载工作流测试**

新增一个测试：通过 tus 上传字节，完成上传会话，创建下载授权，调用 `GET /api/files/v1/download-grants/{grantId}/content`，并预期得到原始字节。

- [ ] **步骤 2：验证测试为红**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore --filter TusUploadEndpoint_CompleteAndDownload_ReturnsUploadedBytes
```

预期：因 404 或缺少内容端点而失败。

- [ ] **步骤 3：跟踪本地内容的授权到会话映射**

MVP 阶段在内存服务中保留以下内存映射：

```csharp
fileId -> uploadSessionId
downloadGrantId -> fileId
```

新增一个供端点使用的窄内部接口：

```csharp
public interface ILocalFileContentIndex
{
    bool TryGetUploadSessionIdForDownloadGrant(string downloadGrantId, out string uploadSessionId);
}
```

- [ ] **步骤 4：实施下载端点**

实施：

```text
GET /api/files/v1/download-grants/{downloadGrantId}/content
```

该端点将授权解析到已上传的本地文件，并以 `application/octet-stream` 流式返回。未知授权返回 404。

- [ ] **步骤 5：验证测试为绿**

运行聚焦测试，然后运行完整的 FileStorage Web 测试。

## 任务 3：文档和验证

**文件：**
- 修改：`docs/architecture/file-storage-baseline.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：如果状态段落不修改就会过时，则修改 `README.md`。

- [ ] **步骤 1：根据实际差异更新文档**

记录 MVP 现在为内存/本地配置档支持本地 tus `HEAD/PATCH` 上传偏移量跟踪和平台下载内容。明确保留 PostgreSQL 元数据和本地字节存储的限制。

- [ ] **步骤 2：最终验证**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：通过。

## 自审

规格覆盖：本计划覆盖上传续传偏移量、追加上传、下载内容、文档和验证。

范围检查：本计划限定在本地文件系统 tus MVP 范围内，不实施 MinIO/S3 分段上传或完整的 tus 创建协议。

占位符扫描：没有遗留占位符。
