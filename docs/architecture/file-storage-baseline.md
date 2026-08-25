# 文件存储基线说明

本文档定义 Nerv-IIP 主平台提供的通用文件存储能力。File Storage 是平台控制面的一部分，负责文件元数据、访问授权、上传下载会话、内部对象键、保留策略与审计挂点。当前通用文件字节仅由显式 tus 的本地存储承载；MinIO 当前只服务独立的 `VersionedArchive` 合规归档边界，不是通用文件的当前字节后端。

## 定位

1. File Storage 是主平台通用能力，不属于 Knowledge、AppHub、Ops 或某个行业扩展的私有实现。
2. File Storage 负责“文件如何被安全保存、访问和治理”，不负责解释文件的业务语义。
3. 业务服务通过 `fileId`、`FileReference` 或公开 API/SDK 使用文件能力，不直接暴露或持久化对象存储内部 key 作为业务接口。
4. UI、外部应用、Connector Host 和行业扩展不得直接访问 MinIO；上传下载必须经过 File Storage 授权后获得受控入口或短期 URL。
5. File Storage 与 IAM 协作完成组织、环境、主体、权限范围和授权授予校验。
6. 当前运行时仍以 `UploadMode`、`Provider` 和上传指令表达传输选择；这是兼容面，不表示 tus、`server-proxy` 与 S3 multipart 是长期并列的 Upload Provider。已批准的目标分轴见“已批准目标，尚未实现”。

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
| StoredFile | 文件元数据事实 | 保存组织、环境、文件名、内容类型、大小、校验和、状态、用途和保留策略等当前文件事实。 |
| UploadSession | 上传会话 | 控制一次上传的目标、大小、content type、有效期、幂等键、`uploadMode`、`provider`、完成状态和失败原因。当前这些字段仍是公开兼容面。 |
| UploadInstructions | 上传指令 | 当前 File Storage 根据 UploadSession 和运行时 provider 生成客户端上传说明；默认 `server-proxy` 只返回占位指令，显式 tus 才返回本地 tus URL。 |
| 上传传输实现 | 当前传输差异 | 当前实现有 `ServerProxyUploadProvider` 和自研本地 tus 链路；它们不是长期可扩展的目标 Upload Provider 抽象。 |
| DownloadGrant | 下载授权 | 表示一次短期下载许可，可映射为平台中转下载或对象存储预签名 URL。 |
| FileReference | 业务引用关系 | 记录 ownerService、ownerType、ownerId 与 fileId 的绑定，但不解释业务对象本身。 |

## MVP 当前子集

2026-05-21 已落地 FileStorage MVP 的 contracts、SDK、元数据 API、PostgreSQL-backed service 和本地 tus endpoint。不把 MinIO 部署联调作为前置条件。

当前公开 API 子集为：

```text
POST /api/files/v1/upload-sessions
POST /api/files/v1/upload-sessions/{uploadSessionId}/complete
GET  /api/files/v1/usage
GET  /api/files/v1/files/{fileId}
POST /api/files/v1/files/{fileId}/download-grants
```

当前默认实现返回 `uploadMode = server-proxy`、`provider = server-proxy` 的占位上传指令；仓库没有相应的字节 `PUT` endpoint，不能把该默认值描述为可用的中转上传路径。只有显式设置 `FileStorage:UploadProvider=tus` 时，创建上传会话才返回 `uploadMode = tus`、`provider = tus` 和 `/api/files/v1/tus/{uploadSessionId}` 上传指令。FileStorage 已有 PostgreSQL `filestorage` schema 的 `stored_files`、`upload_sessions`、`download_grants` 初始 migration 和 schema convention tests；PostgreSQL-backed API service 是唯一 metadata 实现，所有环境都拒绝 `Persistence:Provider=InMemory`。当前 tus MVP 是 FileStorage-owned 自研本地传输入口：`HEAD /api/files/v1/tus/{uploadSessionId}` 查询当前 `Upload-Offset`、`Upload-Length` 和 `Upload-Expires`，`PATCH /api/files/v1/tus/{uploadSessionId}` 按 offset 追加字节，客户端可通过停止发送并再次 `HEAD` 后继续 `PATCH` 实现暂停/续传；`PATCH` 会拒绝超过上传会话声明大小的内容，支持 tus `Upload-Checksum: sha256 <base64>` chunk 校验，不匹配时返回 `460` 且不推进 offset；过期未完成的本地 tus 字节会由后台垃圾回收兜底清理，后续 `HEAD`/`PATCH` 访问过期会话时也会确定性拒绝并清理；complete 时会再次校验本地实际大小、可选 checksum，并按声明 content type/扩展名做魔数复核，若本地 tus store 配置不可用会返回服务端错误而非客户端请求错误。download grant content endpoint 可读取这条本地 tus 链路的字节。字节位置当前由 `uploadSessionId` 的 SHA-256 摘要派生，存放在本地 `FileStorage:Tus:RootPath`；上传会话 TTL 当前固定为 15 分钟，暂不接 MinIO/S3 multipart。`ObjectKey` 虽已在创建会话时生成并持久化，但当前不参与读、写、下载或删除的实际字节 I/O。当前 `PATCH` 为了进行 chunk checksum 校验会在 endpoint 内短暂缓冲单个 chunk，生产级大 chunk 流式限额写入/低 LOH 压力优化随对象存储 adapter 或后续传输优化处理。`object_key` 只允许出现在 FileStorage 持久化/内部实现中，公开 API response、SDK DTO、Gateway facade 和 Console generated client 均不得暴露。

2026-07-05 安全硬化已补齐以下运行路径：上传会话创建时按 `FileStorage:PurposePolicies:{purpose}` 校验 content type、扩展名 allowlist/blocklist，并按 `FileStorage:Quotas:OrganizationPurpose:{org}:{env}:{purpose}:MaxBytes`、`FileStorage:Quotas:Organization:{org}:{env}:MaxBytes` 或用途级 `QuotaBytes` 做配额拒绝；组织级配额使用组织/环境总用量并按 organization/environment 加锁，组织用途级和用途级配额使用对应 purpose 用量并按 organization/environment/purpose 加锁，使 usedBytes 检查和 upload session reservation 写入在当前服务进程内串行化；`GET /api/files/v1/usage` 返回匹配配额口径下的当前已存字节加未过期上传会话预留字节，以及匹配配额。只有 `status == available` 的文件可由 download grant content endpoint 兑换为字节。正式文件生命周期清理由 `FileStorage:PurposePolicies:{purpose}:RetentionSeconds` 触发软删，再按 `FileStorage:GarbageCollection:PhysicalDeleteGraceSeconds` 物理删除 `stored_files`、关联 `upload_sessions` / `download_grants` 和本地 tus 字节。

2026-08-17 起，`FileStorage:PurposePolicies` 的直接子键同时是 purpose 注册目录的单一事实源，Domain 不再维护硬编码白名单。平台内置目录为 `application-package`、`avatar`、`attachment`、`diagnostic-log`、`quality-evidence`、`maintenance-photo` 与 `engineering-document`；PostgreSQL metadata 实现和 `/internal/file-storage/v1/purposes/{purpose}` 共用同一配置解析。未注册值以 HTTP 400 返回稳定错误码 `file-purpose-not-registered`，消息包含实际 purpose 和配置路径，内部边界端点返回同一诊断。NvUI 测试和设计系统示例复用或对齐该目录；本项不引入 owner allowlist、传输 eligibility、策略版本或新的契约生成链。

2026-08-23 起，`barcode-label-template` 新增 owner allowlist，并只接受 `.json` 与 `application/vnd.nerv-iip.label-template+json`。单文件上限为 65,536 bytes；用途默认总量上限独立设为 8,388,608 bytes，可容纳 128 份达到单文件上限的不可变资产。组织+环境+用途或组织+环境配额一旦显式配置，会按既有优先级取代该用途默认值。owner 必须为 `business-barcode-label / label-template / {templateCode}`，创建与 complete 都要求相同的 `sha256:` 加 64 位十六进制声明摘要。complete 的 organization、environment 或 purpose 与会话不一致时保持失败关闭，下载 metadata 保留同一 scope、owner、purpose 与 checksum。该 purpose 明确不配置 `RetentionSeconds`，因此 FileStorage 不自动清理标签模板；引用治理与物理删除授权仍由后续 BarcodeLabel 消费链负责。UTF-8、无 BOM 与下载后实际字节摘要复核由 #2066 的 BarcodeLabel 消费链负责；FileStorage 只有 tus lane 会在 complete 时重算实际字节摘要，默认 `server-proxy` 没有字节 `PUT` endpoint。本项不修改 renderer、Gateway/OpenAPI、generated client、printer 配置、通用字节 provider 或数据库 schema。

2026-08-25 起，complete 的 application/PostgreSQL 协议层持久化 `open / committing / completed`、不可变提交意图、执行 owner/租约、恢复退避与永久证据失败的终止时间。Tx1 在共享 PATCH gate 内提交后才调用 `IUploadCommitStorage`；执行期间由独立 DbContext 续租，写入 Tx2、重开或失败诊断前再次核验 owner。只有本次调用前没有历史 storage-action 标记，且本次明确证明未开始任何 final 动作时，才允许回到 `open`；历史标记存在、遗留迁移记录或 final 可能存在时继续失败关闭。size/checksum 已验证但与冻结意图不一致时停止自动恢复；`committing` 会话和 tus 字节不由过期 GC 删除。当前 tus staging 的大小、可选 checksum 与文件签名校验仍在 Tx1 前执行；默认 storage seam 仍不可用，因此这些协议事实不代表 provider promote、final 回读或 canonical `ObjectKey` 已实现。

## 已批准目标，尚未实现

以下是已接受的目标架构，不是当前 API、配置、schema、脚本、生产就绪或真实基础设施证明；实现进度仍以 `docs/architecture/implementation-readiness.md`、对应交付和运行证据为准。

### 上传协议、代理和提交

依 [ADR 0023](../adr/0023-filestorage-tus-proxy-staging-final-complete-invariants.md)，通用文件上传的唯一公开协议目标是 tus，服务端目标实现是 `tusdotnet`。PlatformGateway proxy 是受控外部入口拓扑，storage provider 是字节后端；三者必须分轴建模。客户端只访问 Gateway 暴露的受控 tus URL，不取得内部 FileStorage URL、存储地址、`ObjectKey` 或长期凭据。

目标中，tus 只向独立、可续传且可过期但不可下载的 staging 写入；final 与 staging 分离，final 只由内部、provider 无关且不公开的 canonical `ObjectKey` 定位。complete 对所有 provider 都必须统一从实际物理字节证明存在性与实际 size，服务端计算并持久化 canonical SHA-256，按持久 `committing` 意图幂等 promote，再按同一 `ObjectKey` 回读 final 并复验存在性、size 和 checksum，最后原子写入唯一的 `StoredFile` available 事实与 session completed。final 已存在但 size/checksum 不一致时必须失败关闭且不得覆盖；相同内容才可幂等收敛。

### final 字节后端与 Local 语义

依 [ADR 0024](../adr/0024-filestorage-storage-provider-and-local-production-semantics.md)，`IStorageProvider` 是通用文件 final 字节面的目标基础设施边界，不是上传协议、Gateway 入口、领域聚合或 per-file provider registry。`tusdotnet` `ITusStore` 只负责 staging、offset 和 expiry；FileStorage application/PostgreSQL 仍拥有上传会话准入、共享提交栅栏、持久 `committing` 意图、Tx1/Tx2 及 completed/available 文件事实。

目标部署只允许在 LocalFileSystem 与 S3-compatible 两个 `IStorageProvider` 实现中严格二选一；同一运行实例不得热切换、按文件动态路由、dual-write、read fallback、多 placement 或多副本。LocalFileSystem 的目标能力包括显式持久 root 与 storage identity、staging/final 同 filesystem 的分区、实际操作时的路径 confinement、atomic no-overwrite promote、final 回读复验，以及 startup blocked、runtime critical/unready、capacity restricted 的健康语义。非 Development 环境缺失、未知或冲突的 provider 配置，或 preflight/identity 不可信时，目标行为是 startup fail-fast，而非回退到 `server-proxy`、系统临时目录或另一 provider。

### 通用文件与合规归档的边界

通用文件 provider 目标与 `VersionedArchive` 是两条独立边界。后者当前经 `IVersionedObjectStore`/`MinioVersionedObjectStore` 直接使用 MinIO versioning、object lock 与 legal hold，不参与通用文件 LocalFileSystem/S3-compatible 选择器、上传会话、`StoredFile` 或 download grant。依 [ADR 0027](../adr/0027-filestorage-offline-migration-cutover-and-rollback.md) 及其[离线迁移与切换运行手册](file-storage-offline-migration-runbook.md)，两条轨道的迁移、验证、切换和清理也必须分别取证；不得把合规语义引入通用文件面。

## 上传安全与治理

File Storage 创建上传会话时必须先应用平台策略，不能等文件落盘后再补救。首批至少冻结以下约束：

1. 每种 filePurpose 需要配置最大文件大小、允许的 content type、允许的扩展名和保留策略；允许像不可变标签模板这样明确配置为不自动保留清理的用途。
2. 上传会话必须有短有效期，过期会话不能 complete，底层临时对象需要异步清理。
3. 客户端声明的 content type、文件名和扩展名只能作为输入，最终以服务端校验和对象存储元数据为准。
4. 当前显式 tus 的 complete 会校验本地实际大小和可选 checksum；对所有 provider 的实际字节存在性、size、服务端 canonical SHA-256、幂等 promote 与 final 回读复验，是“已批准目标，尚未实现”中的统一不变量。
5. 可执行文件、脚本、压缩包等高风险类型默认不进入普通预览或知识引入流程。
6. 上传、下载授权、归档和删除都必须写入可审计事件；审计事实最终由服务端生成。
7. 每个组织和环境应预留容量配额与单日上传量限制；首批可以先建配置口径，不要求完整计费或配额后台。

当前配置口径：

1. `FileStorage:PurposePolicies` 的直接子键定义已注册 purpose；子键下的 `AllowedContentTypes`、`AllowedExtensions`、`BlockedExtensions` 控制声明校验，`MaximumFileSizeBytes` 控制单文件声明上限，`RequiredOwnerService` / `RequiredOwnerType` 控制用途专属 owner，`RequireSha256Checksum` 控制声明摘要形状与创建/complete 一致性，`QuotaBytes` 控制用途默认总量。如果某个 allowlist 或可选约束未配置，则对应维度保持兼容放行，但 blocked extension 始终优先；整个 purpose 子键未注册时，在创建上传会话前失败关闭。
2. tus complete 对 zip、png、jpeg 和 pdf 做魔数复核；普通文本/日志类按声明策略治理。
3. 配额优先级为组织+环境+用途、组织+环境、用途默认；超配额在上传会话创建阶段返回冲突，不创建临时会话。

## 存储与元数据边界

1. 当前 File Storage 元数据由 PostgreSQL 承载；当前字节传输只在显式 tus 时经 FileStorage-owned 本地 tus store 落盘。已批准目标中的 LocalFileSystem/S3-compatible final 后端尚未实现。
2. 当前 `stored_files` 持久化 `fileId`、组织/环境、owner、用途、文件名、内容类型、`sizeBytes`、`checksum`、`ObjectKey`、状态及创建/完成/删除/物理清理时间；`upload_sessions` 持久化上传会话 ID、目标文件、同一上下文、预期大小、`checksum`、`ObjectKey`、`provider`、创建/过期/完成状态与时间。
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

1. 显式 tus 配置下，能创建上传会话并完成一个本地文件写入；默认 `server-proxy` 占位指令不具备该字节写入能力。
2. 当前默认 `server-proxy` 仅生成占位上传指令；显式 tus 可生成并使用当前自研的本地 `HEAD`/`PATCH` 上传指令。长期唯一公开协议与 final provider 的目标见“已批准目标，尚未实现”。
3. 能通过 `fileId` 查询文件元数据，响应中不暴露内部 objectKey。
4. 能为有权限主体生成短期下载授权。
5. 当前 `FileMetadataResponse` 包含 `fileId`、组织、环境、`OwnerReference`、`filePurpose`、文件名、内容类型、`sizeBytes`、`checksum`、状态、创建时间和完成时间；`uploadMode`、`provider` 只属于当前 `CreateUploadSessionResponse`，不属于文件元数据响应。
6. 上传会话过期后不能 complete，过期临时对象可以被后台任务安全清理。
7. Knowledge、Ops 或 AppHub 至少一个服务能以 `fileId` 形式引用文件，不直接保存对象存储 key 作为业务事实。
