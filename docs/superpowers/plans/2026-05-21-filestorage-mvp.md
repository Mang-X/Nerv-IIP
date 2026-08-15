# FileStorage MVP 实施计划

> **面向智能代理工作者：** 必须使用子技能：采用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：** 使用 server-proxy 元数据路径构建首个 FileStorage MVP，并在同一阶段覆盖 PostgreSQL 迁移和 schema 约定。

**架构：** FileStorage 拥有通用文件事实、上传会话和下载授权。首个提供程序是 server-proxy 元数据桩，使 API、持久化和 SDK 工作无需部署 MinIO 即可推进。tus 在核心事实稳定后接入，并作为 MVP 的完整二进制传输路径；MinIO/S3 multipart 属于 MVP 后的部署集成。

**技术栈：** .NET 10、FastEndpoints、xUnit、EF Core、PostgreSQL、Nerv.IIP.Testing schema 约定辅助工具。

---

## 文件结构

修改：

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Domain/FileStorageBoundaries.cs` - 用 MVP 领域事实和简单策略辅助工具替换骨架记录类型。
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs` - 先注册内存 MVP 存储，之后再注册持久化。
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Boundaries/FileStorageBoundaryEndpoints.cs` - 保留或更新边界诊断端点。
4. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageSkeletonTests.cs` - 将骨架覆盖转换为聚焦行为的 API 测试。
5. `docs/architecture/file-storage-baseline.md` - 在实施证据存在后更新。

在 API 纵切中新增：

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileUploadSessionEndpoints.cs`
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileMetadataEndpoints.cs`
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Services/InMemoryFileStorageStore.cs`

在持久化纵切中新增：

1. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/ApplicationDbContext.cs`
2. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/StoredFileEntityTypeConfiguration.cs`
3. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/UploadSessionEntityTypeConfiguration.cs`
4. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/DownloadGrantEntityTypeConfiguration.cs`
5. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/*`
6. `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests` 下的 schema 约定测试。

## 任务 1：Server-Proxy 元数据 API

**文件：**
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Domain/FileStorageBoundaries.cs`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- 修改：`backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageSkeletonTests.cs`
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileUploadSessionEndpoints.cs`
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Files/FileMetadataEndpoints.cs`
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Services/InMemoryFileStorageStore.cs`

- [x] **步骤 1：编写会失败的 API 测试**

添加测试以验证：

```text
POST /api/files/v1/upload-sessions creates a server-proxy session.
POST /api/files/v1/upload-sessions/{uploadSessionId}/complete completes the session.
GET /api/files/v1/files/{fileId} returns metadata.
POST /api/files/v1/files/{fileId}/download-grants returns a short-lived grant.
Metadata and grant JSON do not contain objectKey or object_key.
```

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

实施前预期：因缺少端点或返回非成功状态而失败。

- [x] **步骤 2：实现最小内存存储和端点**

仅实现 server-proxy 元数据行为：

```text
uploadMode = server-proxy
provider = server-proxy
uploadUrl = /api/files/v1/upload-sessions/{uploadSessionId}/content
downloadUrl = /api/files/v1/download-grants/{downloadGrantId}/content
```

内部对象键可以是确定性的：

```text
{organizationId}/{fileId}
```

不得在公共响应中暴露该对象键。

- [x] **步骤 3：重新运行 FileStorage 测试**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

预期：通过。

## 任务 2：PostgreSQL 迁移与 Schema 约定

**文件：**
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Nerv.IIP.FileStorage.Infrastructure.csproj`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- 修改：`backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj`
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/ApplicationDbContext.cs`
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/EntityConfigurations/*.cs`
- 新增：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure/Migrations/*`
- 新增：FileStorage schema 约定测试。

- [x] **步骤 1：添加 DbContext 和实体配置**

使用 schema `filestorage`，并将迁移历史配置在同一 schema 下。

数据表：

```text
stored_files
upload_sessions
download_grants
```

所有业务表和业务列都需要注释。任何 JSON/text 载荷字段都必须添加 JSON/text 兼容性注释。

- [x] **步骤 2：生成初始迁移**

采用 AppHub/Ops/IAM 使用的仓库本地 EF 工具模式，并将提供程序设置为 PostgreSQL。

- [x] **步骤 3：添加 schema 约定测试**

复用 `Nerv.IIP.Testing` 辅助工具。覆盖：

```text
table comments
column comments
string ID length conventions
migrations history schema
object_key remains persistence-only
```

- [x] **步骤 4：运行 FileStorage 持久化测试**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
```

预期：通过。

## 任务 3：文档与验证

**文件：**
- 修改：`docs/architecture/file-storage-baseline.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/superpowers/plans/2026-05-21-next-stage-stabilization-and-readiness.md`

- [x] **步骤 1：根据实际差异更新文档**

记录首个 FileStorage MVP 先使用 server-proxy 元数据桩，tus 作为完整 MVP 传输路径，而 MinIO/S3 multipart 仅作为 MVP 后的部署集成。

- [x] **步骤 2：运行验证**

运行：

```powershell
dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --no-restore
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：两项均通过。
