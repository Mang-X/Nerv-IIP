# 文件存储基线说明

本文档定义 Nerv-IIP 主平台提供的通用文件存储能力。File Storage 是平台控制面的一部分，负责文件元数据、访问授权、上传下载会话、对象存储键、保留策略与审计挂点；MinIO 或等价对象存储只作为二进制内容的底层存放位置。

## 定位

1. File Storage 是主平台通用能力，不属于 Knowledge、AppHub、Ops 或某个行业扩展的私有实现。
2. File Storage 负责“文件如何被安全保存、访问和治理”，不负责解释文件的业务语义。
3. 业务服务通过 `fileId`、`FileReference` 或公开 API/SDK 使用文件能力，不直接暴露或持久化对象存储内部 key 作为业务接口。
4. UI、外部应用、Connector Host 和行业扩展不得直接访问 MinIO；上传下载必须经过 File Storage 授权后获得受控入口或短期 URL。
5. File Storage 与 IAM 协作完成组织、环境、主体、权限范围和授权授予校验。
6. 上传协议通过 Upload Provider 抽象支持，tus、S3 multipart 和平台中转上传都只是传输策略，不进入文件领域模型核心。

## 首批适用场景

优先覆盖：

1. 用户上传的知识源原始文件。
2. 运维日志包、诊断包、备份包和导出报告。
3. 应用包、发布附件、Connector Host 上报附件和截图类证据。
4. 审计记录、操作任务或审批流程中需要引用的附件。

暂不在首批展开：

1. 在线协作文档编辑。
2. 复杂网盘能力、全文预览和富媒体转码。
3. 跨租户文件共享市场。
4. 大规模 CDN 分发策略。

## 核心对象

| 对象 | 职责 | 首批说明 |
| --- | --- | --- |
| StoredFile | 文件元数据事实 | 保存组织、环境、文件名、内容类型、大小、校验和、状态、用途、保留策略和当前版本。 |
| FileVersion | 文件内容版本 | 保存对象存储 provider、bucket、objectKey、checksum、size 和创建时间；objectKey 不作为公开业务字段。 |
| UploadSession | 上传会话 | 控制一次上传的目标、大小、content type、有效期、幂等键、uploadMode、provider、完成状态和失败原因。 |
| UploadInstructions | 上传指令 | File Storage 根据 UploadSession 和 provider 生成的客户端上传说明，例如 tus endpoint、S3 multipart presigned urls 或平台中转地址。 |
| UploadProvider | 上传实现策略 | 抽象 tus、S3 multipart、server-proxy 等协议差异；它不是领域聚合，不拥有文件事实。 |
| DownloadGrant | 下载授权 | 表示一次短期下载许可，可映射为平台中转下载或对象存储预签名 URL。 |
| FileReference | 业务引用关系 | 记录 ownerService、ownerType、ownerId 与 fileId 的绑定，但不解释业务对象本身。 |

## 推荐接口

首批接口以 OpenAPI 作为事实来源，并进入 Platform SDK：

```text
POST /api/files/v1/upload-sessions
GET  /api/files/v1/upload-sessions/{uploadSessionId}/instructions
POST /api/files/v1/upload-sessions/{uploadSessionId}/complete
POST /api/files/v1/upload-sessions/{uploadSessionId}/abort
GET  /api/files/v1/files/{fileId}
POST /api/files/v1/files/{fileId}/download-grants
POST /api/files/v1/files/{fileId}/archive
```

`CreateUploadSession` 由服务端选择或校验上传模式，返回 `uploadSessionId`；`GetUploadInstructions` 返回客户端执行上传所需的短期指令。推荐首批保留以下 provider 抽象：

1. S3 multipart：File Storage 创建受限上传会话并发放 MinIO/S3 multipart 预签名 URL，客户端直传对象存储后调用 complete 完成校验。
2. tus：File Storage 或专用 tus 组件提供断点续传 endpoint，File Storage 仍负责会话创建、权限、完成校验和文件提交。
3. server-proxy：客户端把文件流提交到 File Storage，由服务写入对象存储，适合小文件或受限网络环境。

无论采用哪种模式，最终完成写入时都必须校验组织、环境、主体、用途、文件大小、content type、checksum、provider 回执和上传会话有效期。客户端只持有短期上传指令，不持有长期对象存储凭证。

## 上传安全与治理

File Storage 创建上传会话时必须先应用平台策略，不能等文件落盘后再补救。首批至少冻结以下约束：

1. 每种 filePurpose 需要配置最大文件大小、允许的 content type、允许的扩展名和默认保留策略。
2. 上传会话必须有短有效期，过期会话不能 complete，底层临时对象需要异步清理。
3. 客户端声明的 content type、文件名和扩展名只能作为输入，最终以服务端校验和对象存储元数据为准。
4. complete 时必须校验 size、checksum、provider 回执、part 列表和对象实际存在性。
5. 可执行文件、脚本、压缩包等高风险类型默认不进入普通预览或知识引入流程。
6. 预留恶意文件扫描/隔离状态：未扫描或扫描失败的文件不得生成普通下载授权，不得进入 Knowledge ingestion。
7. 上传、下载授权、归档和删除都必须写入可审计事件；审计事实最终由服务端生成。
8. 每个组织和环境应预留容量配额与单日上传量限制；首批可以先建配置口径，不要求完整计费或配额后台。

## Upload Provider 抽象

Upload Provider 是 File Storage 的基础设施扩展点，建议以平台语义定义接口，而不是把 tus 或 S3 API 直接上抛到业务层：

```text
CreateUploadSession
GetUploadInstructions
CompleteUploadSession
AbortUploadSession
VerifyUploadedObject
```

首批 provider 能力建议：

1. `S3MultipartUploadProvider`：对接 MinIO/S3，生成 multipart uploadId、part presigned urls，完成后校验 ETag、size 和 checksum。
2. `TusUploadProvider`：对接 tus endpoint，支持断点续传和续传状态查询，完成后将上传结果提交给 File Storage 校验。
3. `ServerProxyUploadProvider`：保留平台中转上传路径，用于小文件、内网限制或需要服务端扫描的场景。

provider 选择可以由文件用途、大小、客户端能力、网络环境或部署配置决定，但选择结果必须记录在 `UploadSession` 中，便于审计和故障诊断。

## 存储与元数据边界

1. File Storage 元数据写入 PostgreSQL，二进制内容写入 MinIO 或等价对象存储。
2. 数据库中保存 provider、uploadMode、bucket、objectKey、checksum、size、contentType、retentionUntil、createdBy、ownerService 等治理字段。
3. `objectKey` 是内部存储定位信息，不能出现在前端、外部应用或 Connector Host 的长期业务契约中。
4. 业务服务只保存 `fileId` 或 `FileReference`，不能保存预签名 URL 作为长期事实。
5. 文件归档或删除必须先改变 File Storage 事实状态，再按保留策略异步清理底层对象。
6. 临时对象、未完成分片和过期上传会话必须有后台清理任务，清理动作应具备幂等性和可诊断日志。

## 权限与审计

1. 所有文件必须携带 organizationId 与 environmentId。
2. 创建上传会话、完成上传、读取元数据、生成下载授权和归档文件都需要服务端权限校验。
3. File Storage 负责通用文件访问授权；业务服务仍负责判断当前主体是否有权访问对应业务对象。
4. 生成下载授权、完成上传、归档和删除文件应写入审计事件或由 Ops/Audit 能力记录可追踪事实。
5. 下载授权必须短期有效，可撤销，且不能扩大原始主体的组织、环境或资源范围。

## 与其它服务的关系

1. Knowledge 使用 File Storage 管理原始文件和派生附件，但知识源状态、解析任务、分块和索引仍由 Knowledge 拥有。
2. Ops 可通过 File Storage 保存日志包、备份包、诊断包和审计附件，但动作任务和审计事实仍由 Ops 拥有。
3. AppHub 可引用应用包、发布附件或实例证据文件，但应用目录、版本和实例事实仍由 AppHub 拥有。
4. PlatformGateway 只聚合文件元数据和下载授权入口，不绕过 File Storage 直连对象存储。
5. Connector Host 如需上传日志或诊断包，必须使用 IAM 授权后的 File Storage API/SDK。

## 首批验收标准

1. 能创建上传会话并完成一个文件写入。
2. 能通过 Upload Provider 抽象生成 S3 multipart、tus 或 server-proxy 中至少一种上传指令，且接口不泄漏长期对象存储凭证。
3. 能通过 `fileId` 查询文件元数据，响应中不暴露内部 objectKey。
4. 能为有权限主体生成短期下载授权。
5. 文件元数据包含组织、环境、ownerService、ownerType、ownerId、contentType、size、checksum、uploadMode、provider、filePurpose、scanStatus 和状态。
6. 上传会话过期后不能 complete，过期临时对象可以被后台任务安全清理。
7. Knowledge、Ops 或 AppHub 至少一个服务能以 `fileId` 形式引用文件，不直接保存对象存储 key 作为业务事实。
