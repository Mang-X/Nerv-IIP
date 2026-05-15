# 一阶领域模型 V1

本文档定义 IAM、File Storage、AppHub、Ops、Notification 在首批纵切和后续平台闭环中的一阶领域模型。目标不是一次性穷尽所有聚合，而是先明确谁拥有什么事实、谁发布什么事件、谁负责任务闭环。

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
- OperationDispatched
- OperationStarted
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
