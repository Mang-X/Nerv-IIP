# FileStorage MVP 设计

## 目标

在不把 object-storage 部署作为首个实现切片前置条件的情况下，将 FileStorage 从边界骨架推进为可用的平台能力。

## 范围

首个 FileStorage MVP 实现由平台拥有的文件元数据与授权流程：

1. 创建上传会话。
2. 完成上传会话并生成已存储文件元数据。
3. 按 `fileId` 读取文件元数据。
4. 创建短期 download grants。
5. 在 PostgreSQL 的 `filestorage` schema 下持久化 FileStorage 事实。
6. 强制执行 AppHub、Ops 和 IAM 已使用的相同 schema 约定测试。

首个 contract 切片已验证平台 contract、持久化模型、具备授权形态的 API，以及内部 object key 不泄漏的边界。MVP 现在还包含用于二进制上传/下载的本地 tus 传输路径，且不要求部署 MinIO/S3。

## Provider 顺序

1. **第一步：server-proxy 元数据 stub**
   - 使用 `server-proxy` 作为选定的 `uploadMode` 和 provider 标签。
   - 返回由平台控制的上传指令。
   - 存储内部 `objectKey`，但绝不通过公共 API 响应暴露。
   - 这样无需部署 MinIO，API、持久化、SDK 和 Console/API-client 工作即可推进。

2. **第二步：tus**
   - 核心 FileStorage 事实稳定后，增加断点续传语义。
   - 将 tus 视为 FileStorage MVP 的完整二进制传输能力。
   - 将 tus 保持在同一个 Upload Provider 抽象之后。
   - FileStorage 继续拥有会话创建、完成校验、元数据和 grants。

3. **MVP 之后：MinIO/S3 multipart**
   - FileStorage MVP 不包含 MinIO/S3 multipart。
   - 仅在 object-storage 部署和集成测试就绪后增加。
   - 将 MinIO/S3 视为基础设施 adapter，而不是 FileStorage 公共 contract。
   - 只能使用短期指令或 presigned URLs；任何长期 object storage 凭据或 object key 都不得离开 FileStorage。

## API Contract

MVP endpoints 如下：

```text
POST /api/files/v1/upload-sessions
POST /api/files/v1/upload-sessions/{uploadSessionId}/complete
GET  /api/files/v1/files/{fileId}
POST /api/files/v1/files/{fileId}/download-grants
HEAD /api/files/v1/tus/{uploadSessionId}
PATCH /api/files/v1/tus/{uploadSessionId}
GET  /api/files/v1/download-grants/{downloadGrantId}/content
```

`CreateUploadSession` 接收 organization/environment 上下文、owner reference、文件用途、文件名、content type、预期大小和可选 checksum。它返回 `uploadSessionId`、`fileId`、`uploadMode`、provider 名称、过期时间和上传指令。

`CompleteUploadSession` 将 pending 会话标记为 completed，并创建已存储文件元数据。首个切片校验会话状态、过期时间、用途和调用方上下文。它记录内部 object key，但暂不验证 MinIO/S3 object。

`GetFileMetadata` 仅返回公共文件事实：`fileId`、organization/environment、owner reference、用途、文件名、content type、大小、checksum、scan status、status 和 timestamps。它不得返回 `objectKey`。

`CreateDownloadGrant` 返回短期平台下载 URL。使用 `FileStorage:UploadProvider=tus` 时，content endpoint 读取本地存储的 tus bytes；它不得返回 `objectKey`。

## 持久化

在同一个发布切片中增加 FileStorage PostgreSQL 持久化：

1. 在 `Nerv.IIP.FileStorage.Infrastructure` 中增加 `ApplicationDbContext`。
2. 为 stored files、upload sessions 和 download grants 增加 EF Core entity configurations。
3. 在 `filestorage` schema 下增加初始 migration。
4. 将 `__EFMigrationsHistory` 配置在 `filestorage` schema 下。
5. 使用现有 `Nerv.IIP.Testing` helpers 的 schema 约定测试。

首个 schema 至少应包含：

```text
stored_files
upload_sessions
download_grants
```

`object_key` 仅存储在 FileStorage 拥有的持久化中。公共 request/response contracts、SDK DTOs 和 Gateway facade 响应均不得暴露它。

## 边界

FileStorage 拥有通用文件事实和 access grants。除 `ownerService`、`ownerType`、`ownerId` 和 `filePurpose` 外，它不解释业务含义。

后续切片可以通过 Gateway 或 service auth 集成 IAM-backed authorization。MVP 保持 request shapes 与 organization/environment 和 principal context 兼容，以便在不改变公共 contracts 的情况下增加权限强制执行。

## 测试

首次实现必须遵循 TDD：

1. 为每个 endpoint 编写 Web tests。
2. 编写测试，证明 `objectKey` 不会出现在 metadata 或 download grant 响应中。
3. 为会话完成规则编写 Domain/application tests。
4. 为 `filestorage` 编写 PostgreSQL schema 约定测试。
5. 现有骨架边界测试应保持兼容，或替换为聚焦行为的测试。

## 验收

满足以下条件时，首个 FileStorage MVP 才可验收：

1. client 可以创建上传会话。
2. 启用 tus provider 后，client 可以在跟踪 offset 的情况下上传 bytes，并通过查询 `HEAD` 继续上传。
3. 同一会话可以完成并生成已存储文件元数据。
4. 可以按 `fileId` 读取已存储文件。
5. 可以创建 download grant，并用它读取本地 tus bytes。
6. 公共响应不暴露内部 object keys。
7. FileStorage PostgreSQL migration 和 schema 约定测试通过。
8. Backend solution 测试和 AppHost build 仍然通过。
