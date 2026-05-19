# 一阶领域模型 V1

本文档定义 IAM、File Storage、AppHub、Ops、Notification、Knowledge、AI Integration 在首批纵切和后续平台闭环中的一阶领域模型。目标不是一次性穷尽所有聚合，而是先明确谁拥有什么事实、谁发布什么事件、谁负责任务闭环。

## 建模原则

1. 先建事实模型，再建流程模型。
2. 不把瞬时状态和持久事实混在一个聚合里。
3. 不为首批闭环过度拆聚合。
4. 先支撑注册、心跳、状态同步、可见和低风险动作，再扩展备份、告警、审批和知识治理。

## IAM 一阶模型

### 目标职责

- 提供平台身份、组织、环境、角色与权限事实。
- 为 Gateway、AppHub、Ops 提供稳定的组织上下文与权限判断基础。
- 为平台应用和外部应用提供统一授权事实，约束外部客户端可访问的组织、环境、资源和能力范围。

### 首批聚合建议

- Organization
- Environment
- User
- Role
- Membership
- UserSession
- ExternalClient
- ConnectorHostCredential
- AuthorizationGrant

### 首批主表建议

- iam_organizations
- iam_environments
- iam_users
- iam_roles
- iam_memberships
- iam_user_sessions
- iam_role_permissions
- iam_external_clients
- iam_connector_host_credentials
- iam_authorization_grants
- iam_client_permissions

### 首批事件建议

- OrganizationCreated
- EnvironmentCreated
- EnvironmentArchived
- UserActivated
- UserSessionRevoked
- RoleGrantedToUser
- PermissionSetChanged
- ExternalClientRegistered
- ConnectorHostCredentialIssued
- ConnectorHostCredentialRevoked
- ExternalAuthorizationGranted
- ExternalAuthorizationRevoked

### 首批不做

1. 复杂 ABAC 规则引擎
2. 菜单编排细节
3. 跨组织委派和临时授权流
4. 完整第三方应用市场、复杂 OAuth/OIDC 协议矩阵和细粒度 consent 页面
5. 工厂、产线、设备、站点等行业组织模型；这些属于后续领域扩展，不进入主平台 IAM 核心事实。

## File Storage 一阶模型

### 目标职责

- 提供主平台通用文件元数据、上传会话、下载授权、对象存储定位和保留策略事实。
- 为 Knowledge、Ops、AppHub、Connector Host、外部应用和行业扩展提供受 IAM 约束的文件存取能力。
- 不解释文件所属业务对象的领域语义，不替代 Knowledge 的知识源生命周期、Ops 的动作审计或 AppHub 的应用目录事实。

### 首批聚合建议

- StoredFile
- FileVersion
- UploadSession
- DownloadGrant
- FileReference
- RetentionPolicy
- FilePurposePolicy

### 首批值对象与策略建议

- UploadInstructions
- UploadMode
- UploadProvider
- ScanStatus

### 首批主表建议

- filestorage_files
- filestorage_file_versions
- filestorage_upload_sessions
- filestorage_download_grants
- filestorage_file_references
- filestorage_retention_policies
- filestorage_file_purpose_policies

### 首批事件建议

- UploadSessionCreated
- UploadSessionExpired
- StoredFileCommitted
- FileVersionAdded
- DownloadGrantIssued
- FileReferenced
- FileArchived
- FileDeleted
- FileRetentionPolicyChanged
- FileScanStatusChanged

### 关键边界

1. File Storage 拥有文件元数据、对象存储 key 和访问授权事实，但不拥有业务对象本身。
2. `objectKey`、预签名 URL 和底层存储凭证不进入长期业务契约；其它服务只保存 `fileId` 或 `FileReference`。
3. 文件上传、下载、归档和删除都必须携带组织、环境、主体和用途上下文。
4. 对象内容默认落到 MinIO 或等价对象存储，元数据和治理状态必须落到 File Storage 自身 schema。
5. Knowledge、Ops、AppHub 可以引用文件，但不能绕过 File Storage 直接把底层对象定位信息当成自己的事实源。
6. tus、S3 multipart 和 server-proxy 只作为 Upload Provider 策略存在；UploadSession 记录 provider 与 uploadMode，StoredFile 不依赖具体上传协议。
7. filePurpose、大小限制、content type allowlist、scanStatus、保留策略和过期清理属于 File Storage 治理事实，不应散落到业务服务中各自实现。

## AppHub 一阶模型

### 目标职责

- 拥有平台当前管理了哪些应用、实例、节点、版本、能力的事实模型。
- 负责接收注册、心跳、能力声明和状态同步，并沉淀为可查询事实。
- 不负责任务执行、审批与审计闭环。

### 首批聚合建议

- Application
- ApplicationVersion
- ManagedNode
- ApplicationInstance
- CapabilityManifest
- InstanceLiveness

### 首批主表建议

- apphub_applications
- apphub_application_versions
- apphub_managed_nodes
- apphub_application_instances
- apphub_instance_capabilities
- apphub_instance_liveness
- apphub_instance_state_history

### 首批事件建议

- ApplicationRegistered
- ApplicationVersionObserved
- ManagedNodeDiscovered
- CapabilityManifestDeclared
- InstanceHeartbeatReceived
- InstanceStateSynchronized
- InstanceStatusChanged
- InstanceMarkedUnreachable

### 关键边界

1. AppHub 只维护 reported state，不维护运维 desired state。
2. AppHub 可以记录实例当前是否可达，但不拥有重启、停止、备份等动作任务生命周期。
3. AppHub 对外提供应用目录与实例事实查询，不直接生成运维任务。

## Ops 一阶模型

### 目标职责

- 拥有动作请求、任务执行、审计记录、结果回传与失败分类。
- 为所有会改变目标系统状态的动作提供统一任务闭环。
- 不拥有应用目录事实，不替代 AppHub 的实例状态模型。

### 首批聚合建议

- OperationTask
- OperationAttempt
- AuditRecord
- OperationTemplate
- ApprovalRequest

### 首批主表建议

- ops_operation_tasks
- ops_operation_attempts
- ops_audit_records
- ops_operation_templates
- ops_approval_requests

### 首批事件建议

- OperationRequested
- OperationClaimed
- OperationLeaseHeartbeat
- OperationLeaseAbandoned
- OperationCompleted
- OperationFailed
- AuditRecorded
- ApprovalRequested
- ApprovalResolved

### 关键边界

1. Ops 只描述动作生命周期，不直接改写 AppHub 的实例事实。
2. 动作执行完成后，Ops 记录结果；实例最终状态仍以 Connector Host 后续状态同步驱动 AppHub 更新为准。
3. 审计是 Ops 的一级能力，不是每个服务各自补一份日志表。

## Notification 一阶模型

### 目标职责

- 拥有平台通知、待办、接收人解析结果、通知偏好、去重合并、已读未读和投递状态事实。
- 消费 AppHub、Ops、AI Integration、Knowledge 等服务发布的平台事实或明确通知意图，生成用户可见消息。
- 不拥有业务触发规则，不替代 Ops 审计、Observability 告警或行业扩展的告警阈值模型。

### 首批聚合建议

- NotificationIntent
- NotificationMessage
- NotificationSubscription
- NotificationPreference
- DeliveryAttempt
- NotificationTemplate

### 首批主表建议

- notification_intents
- notification_messages
- notification_recipients
- notification_subscriptions
- notification_preferences
- notification_delivery_attempts
- notification_templates

### 首批事件建议

- NotificationIntentCreated
- NotificationMessageCreated
- NotificationMessageRead
- NotificationMessageArchived
- NotificationDeliveryRequested
- NotificationDeliverySucceeded
- NotificationDeliveryFailed
- NotificationSuppressed

### 关键边界

1. Notification 负责“如何通知、通知谁、是否重复、是否已读、是否投递成功”，不替业务服务决定业务事实是否成立。
2. AppHub、Ops、AI Integration、Knowledge 和行业扩展只发布领域事实、待处理事项或通知意图，不各自直连邮件、短信、企业 IM 或 Webhook。
3. Notification 通过 IAM 解析用户、角色、组织、环境和授权范围，但不维护平行权限模型。
4. 站内通知是首批默认通道；邮件、短信、企业微信、钉钉和 Webhook 只作为后续 provider 扩展。
5. 通知投递按最终一致性处理，外部通道失败不能回滚原业务事务。
6. Notification 可以保存 sourceEventId、resourceRef、correlationId 和展示摘要，但不复制 AppHub、Ops、Knowledge 或 AI Integration 的领域模型。

## Knowledge 一阶模型

### 目标职责

- 拥有知识源、来源文档、分块、嵌入任务、索引状态、检索策略和引用回显事实。
- 为 AI Integration、Gateway 和平台服务提供统一检索能力，并在返回结果前执行组织、环境与权限过滤。
- 不拥有模型调用治理、MCP 工具执行编排或文件底层存储事实。

### 首批聚合建议

- KnowledgeSource
- SourceDocument
- EmbeddingJob
- RetrievalPolicy

### 首批实体和值对象建议

- Chunk
- CitationRecord
- SourceLocator
- DocumentFingerprint
- ChunkFingerprint
- RetrievalScope

### 首批主表建议

- knowledge_sources
- knowledge_source_documents
- knowledge_chunks
- knowledge_embedding_jobs
- knowledge_retrieval_policies
- knowledge_citation_records
- knowledge_index_state_history

### 首批事件建议

- KnowledgeSourceCreated
- KnowledgeSourceActivated
- KnowledgeSourcePaused
- KnowledgeSourceArchived
- SourceDocumentDiscovered
- SourceDocumentIndexed
- SourceDocumentFailed
- SourceDocumentMarkedStale
- SourceDocumentDeleted
- EmbeddingJobCreated
- EmbeddingJobStarted
- EmbeddingJobCompleted
- EmbeddingJobFailed
- RetrievalPolicyChanged
- CitationRecordCreated
- KnowledgeIngestionFailed

### 核心模型说明

1. KnowledgeSource 是知识来源生命周期聚合，保存组织、环境、来源类型、权限范围、同步策略和当前状态。
2. SourceDocument 归属于 KnowledgeSource，表示一个可处理来源文档，保存来源标识、fileId 或外部来源引用、版本指纹、解析状态、索引状态和引用元数据。
3. Chunk 是 SourceDocument 下的分块实体，保存分块文本或摘要、位置、内容指纹、权限标签、索引引用和可重建元数据。
4. EmbeddingJob 承载首次导入、增量同步、重建索引、删除同步和失败重试生命周期；状态至少覆盖 Pending、Running、PartiallySucceeded、Succeeded、Failed、Cancelled。
5. RetrievalPolicy 保存检索范围、权限过滤口径、召回参数、rerank 开关和引用返回要求；它是 Knowledge 的检索治理事实，不属于 AI Integration 的 prompt 或工具治理。
6. CitationRecord 保存检索结果可回溯引用，至少能关联 KnowledgeSource、SourceDocument、Chunk、来源位置、权限上下文和可展示摘要。

### 关键边界

1. Knowledge 只通过 File Storage 的 fileId、FileReference、受控下载授权或 Platform SDK 使用文件，不保存对象存储 key、预签名 URL 或底层存储凭证作为长期事实。
2. 原始文件、派生附件、扫描状态、保留策略和物理删除归 File Storage；知识源状态、解析、分块、嵌入、索引和引用回显归 Knowledge。
3. File Storage 文件归档、隔离或删除后，Knowledge 应通过集成事件或同步检查将相关 SourceDocument 标记为 Stale、Deleted 或不可检索，而不是直接改写 File Storage 状态。
4. Knowledge 可以把可重建索引写入 Qdrant 或等价向量库，但关系库必须保留可解释的索引元数据、任务状态和引用事实。
5. AI Integration、Gateway 和平台服务只能通过 Knowledge 检索接口消费片段和引用，不绕过 Knowledge 权限过滤直接查询向量库、File Storage 或对象存储。
6. 检索返回必须携带 CitationRecord 或等价引用结构，不能只返回脱离来源的纯文本片段。

## AI Integration 一阶模型

### 目标职责

- 拥有模型提供方配置、模型画像、MCP/Skill 工具定义、工具授权、AI 执行记录、审批与人工确认、prompt 模板版本和策略快照事实。
- 作为模型调用、工具执行和高风险 AI 动作的人机治理边界，消费 IAM 的身份与授权事实，并通过 Notification 暴露待办或结果通知。
- 不托管模型，不维护知识索引，不接管 Ops、AppHub、Knowledge 或 Notification 的领域事实。

### 首批聚合建议

- ModelProviderConfig
- ModelProfile
- ToolDefinition
- ToolAuthorizationGrant
- AiExecutionRecord
- ApprovalRequest
- PromptTemplate

### 首批实体和值对象建议

- HumanConfirmation
- PromptVersion
- PolicySnapshot
- ToolRiskPolicy
- ModelCredentialRef
- ExecutionContextSnapshot

### 首批主表建议

- ai_model_provider_configs
- ai_model_profiles
- ai_tool_definitions
- ai_tool_authorization_grants
- ai_execution_records
- ai_approval_requests
- ai_human_confirmations
- ai_prompt_templates
- ai_prompt_versions
- ai_policy_snapshots

### 首批事件建议

- ModelProviderConfigRegistered
- ModelProviderConfigDisabled
- ModelProfilePublished
- ToolDefinitionRegistered
- ToolDefinitionDeprecated
- ToolAuthorizationGranted
- ToolAuthorizationRevoked
- AiExecutionRequested
- AiExecutionStarted
- AiExecutionCompleted
- AiExecutionFailed
- AiExecutionBlockedByPolicy
- AiApprovalRequested
- AiApprovalResolved
- HumanConfirmationRequested
- HumanConfirmationResolved
- PromptVersionPublished
- PolicySnapshotCaptured

### 核心模型说明

1. ModelProviderConfig 保存模型提供方、凭证引用、组织环境范围、启用状态和治理参数；敏感凭证只保存引用，不进入领域表明文。
2. ModelProfile 表示可被选择的模型能力画像，保存模型用途、能力标签、上下文限制、成本口径、默认参数和启用范围。
3. ToolDefinition 表示 MCP Server、Skill 或平台服务能力暴露出来的受治理工具，保存工具类型、输入输出契约、风险等级、目标服务和审计要求。
4. ToolAuthorizationGrant 表示某个主体、外部客户端、角色或组织环境范围可使用哪些工具；授权判断必须消费 IAM 的身份、权限、组织、环境和外部授权事实。
5. AiExecutionRecord 记录一次模型调用、工具调用或组合执行的请求上下文、模型画像、工具引用、结果摘要、token 或成本口径、失败分类、correlationId 和审计元数据。
6. ApprovalRequest 表示 AI Integration 自身的工具执行审批挂点；HumanConfirmation 表示执行前、执行中或结果采纳前的人工确认动作。
7. PromptTemplate 拥有 PromptVersion；PromptVersion 发布时应捕获 PolicySnapshot，保证后续执行可以解释当时的 prompt、工具授权、模型画像和风险策略。

### 关键边界

1. AI Integration 通过 IAM 获取身份、组织、环境、角色、权限和外部授权事实，但不维护平行权限模型。
2. 工具授权、模型选择策略和 prompt 版本归 AI Integration；用户、角色、会话、外部客户端和基础授权授予仍归 IAM。
3. 需要用户处理的审批、确认和执行失败应发布 AiApprovalRequested、HumanConfirmationRequested、AiExecutionFailed 或明确通知意图，由 Notification 解析接收人、偏好、去重和投递。
4. Notification 可以保存待办和展示摘要，但不复制 ToolAuthorizationGrant、AiExecutionRecord、PromptVersion 或 PolicySnapshot。
5. AI Integration 调用 Knowledge 时只消费检索片段和 CitationRecord，不创建或维护私有向量索引。
6. AI Integration 调用 Ops 或 AppHub 工具时只负责工具治理、授权、审批和执行记录；动作任务事实仍归 Ops，应用实例事实仍归 AppHub。
7. 高风险工具执行必须先形成 ApprovalRequest 或 HumanConfirmation，并在 AiExecutionRecord 中保留决策结果和策略快照。

## 首批纵切映射

### 应用注册链路

1. Connector Host 发送注册契约。
2. AppHub 创建或更新 Application、ApplicationVersion、ManagedNode、ApplicationInstance。
3. Gateway 查询到新增实例事实。

### 心跳链路

1. Connector Host 上报心跳。
2. AppHub 更新 InstanceLiveness 投影。
3. 若超时未收到心跳，再由 AppHub 触发不可达事件或标记。

### 状态同步链路

1. Connector Host 上报实例状态。
2. AppHub 更新 ApplicationInstance.reportedStatus 与状态历史。
3. 只有状态发生变化时才发 InstanceStatusChanged 事件。

### 重启动作链路

1. 控制台或 Gateway 发起 restart。
2. Ops 创建 OperationTask 与 AuditRecord。
3. Connector Host 执行动作并回传结果。
4. Ops 记录 OperationCompleted 或 OperationFailed。
5. AppHub 仍等待后续状态同步来更新最终实例事实。

### 通知链路

1. AppHub、Ops、AI Integration 或 Knowledge 发布平台事实或明确通知意图。
2. Notification 解析接收范围、用户偏好和去重键。
3. Notification 创建站内 NotificationMessage 或待办，并按通道策略创建 DeliveryAttempt。
4. Gateway 查询用户未读、全部、待办和资源相关通知。

## 当前冻结结论

1. AppHub 拥有应用与实例事实，Ops 拥有动作与审计事实。
2. 心跳和状态同步是两类不同契约，不能合并成一个万能上报接口。
3. AppHub 记录 reported state，Ops 不维护实例事实真相源。
4. restart 这类动作的成功与否由 Ops 记录，实例最终状态是否恢复由 AppHub 后续状态同步确认。
5. IAM 首批先服务于组织、环境、权限上下文、用户会话、Connector Host 凭证和外部授权事实基线，不先扩展复杂委派、临时授权或完整第三方应用市场模型。
6. File Storage 拥有文件元数据、对象存储定位、上传下载授权和保留策略；Knowledge、Ops、AppHub 只通过 fileId 或 FileReference 引用文件，不直接保存对象存储 key 作为业务事实。
7. Notification 是主平台通用能力，拥有通知与待办事实；业务服务只表达事实或通知意图，不各自实现通知表或外部通道投递。
8. Knowledge 拥有知识源、来源文档、分块、嵌入任务、检索策略和引用回显事实；AI Integration、Gateway 和平台服务只通过 Knowledge 检索接口消费知识。
9. AI Integration 拥有模型接入、工具治理、AI 执行记录、审批确认、prompt 版本和策略快照事实；IAM 与 Notification 分别只提供身份授权事实和通知待办投递能力。
