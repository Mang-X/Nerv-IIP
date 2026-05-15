# Platform SDK 基线说明

本文档定义 Nerv-IIP Platform SDK 的定位、模块边界、版本策略和禁止事项。Platform SDK 是主平台提供给应用、Connector Host、行业扩展和前端包的公开客户端能力集合，不是新的运行时中心，也不是服务端领域模型的复制品。

## 定位

1. Platform SDK 负责降低外部演进单元接入主平台的成本。
2. Platform SDK 只封装公开 API、公开 DTO、认证上下文、错误模型、追踪上下文和少量客户端协议细节。
3. Platform SDK 不拥有任何平台事实，不保存服务端状态，不替代 IAM、AppHub、File Storage、Ops、Notification、Knowledge 或 AI Integration。
4. Platform SDK 不参与服务端领域决策；所有权限判断、事实写入、审计落库和状态转换仍在主平台服务端完成。
5. Platform SDK 与主平台采用主版本对齐、小版本向后兼容策略。

## 模块划分

推荐按模块发布或至少按命名空间隔离，避免做成一个大而全运行时：

```text
Nerv.IIP.Sdk.Core
Nerv.IIP.Sdk.Auth
Nerv.IIP.Sdk.ConnectorProtocol
Nerv.IIP.Sdk.FileStorage
Nerv.IIP.Sdk.Ops
Nerv.IIP.Sdk.Notification
Nerv.IIP.Sdk.Observability
```

### Nerv.IIP.Sdk.Core

职责：

1. HTTP transport、base URL、超时、重试和错误模型。
2. `correlationId`、`idempotencyKey`、organizationId、environmentId 等公共上下文头。
3. SDK 版本上报、兼容性检查和统一异常类型。

不做：

1. 不包含服务端领域模型。
2. 不维护缓存事实或本地状态真相源。
3. 不直接依赖任一平台服务的 Domain、Infrastructure 或数据库结构。

### Nerv.IIP.Sdk.Auth

职责：

1. client credential、Connector Host credential 或用户 token 的获取、刷新和注入。
2. 统一 Authorization header、token 过期处理和认证错误归一化。
3. 暴露当前主体、组织、环境和 scope 的客户端上下文。

不做：

1. 不在客户端做最终授权决策。
2. 不保存 refresh token、session revoke list 或授权授予事实的真相源。
3. 不复制 IAM 服务端策略、角色权限计算或会话管理实现。

### Nerv.IIP.Sdk.ConnectorProtocol

职责：

1. Connector Host 注册、心跳和状态快照客户端。
2. Connector Protocol DTO、版本字段、幂等键和错误结果处理。
3. 使用 `Sdk.Core` 与 `Sdk.Auth` 发送受 IAM 授权的请求。

不做：

1. 不发现本地资源；资源发现仍由 Connector Host 与 Connector 完成。
2. 不拥有 AppHub 的 Application、ApplicationVersion、ManagedNode 或 ApplicationInstance 事实。
3. 不改变注册、心跳、状态同步的既有边界。

### Nerv.IIP.Sdk.FileStorage

职责：

1. 创建上传会话、获取上传指令、完成上传、取消上传和生成下载授权。
2. 隐藏 tus、S3 multipart、server-proxy 等上传 provider 差异。
3. 返回 `fileId`、文件元数据和短期授权，不暴露长期对象存储凭证。

不做：

1. 不保存 `objectKey` 作为业务事实。
2. 不解释文件在 Knowledge、Ops、AppHub 或行业扩展中的业务语义。
3. 不绕过 File Storage 的完成校验、权限校验和审计挂点。

### Nerv.IIP.Sdk.Ops

职责：

1. 创建运维任务、查询任务状态、回传动作结果和提交附件引用。
2. 支持 Connector Host 或外部应用提交受控的 `OperationResult`。
3. 可以提供审计意图或操作上下文提交入口，由服务端转换为正式审计事实。

不做：

1. 不直接写 `AuditRecord` 最终事实。
2. 不绕过 Ops 的任务状态机、审批挂点或失败分类。
3. 不成为动作执行结果的唯一真相源。

### Nerv.IIP.Sdk.Notification

职责：

1. 提交通知意图、查询通知、标记已读、归档和查询待办入口。
2. 支持外部应用或行业扩展在受 IAM 授权下表达通知意图。
3. 使用 `Sdk.Core` 与 `Sdk.Auth` 携带组织、环境、资源引用、correlationId 和幂等键。

不做：

1. 不直接调用短信、邮件、企业 IM 或 Webhook provider。
2. 不绕过 Notification 的接收人解析、偏好、去重和投递状态记录。
3. 不把 NotificationMessage 当作 Ops 审计、AppHub 状态或 Knowledge 引入事实。

### Nerv.IIP.Sdk.Observability

职责：

1. 统一 `correlationId`、W3C trace context、标准日志字段和请求范围上下文。
2. 帮助 Connector Host、外部应用和扩展模块输出可关联的结构化日志。
3. 与 `Sdk.Core` 配合把追踪上下文透传到平台 API。

不做：

1. 不采集或保存平台日志事实。
2. 不替代 Ops/Audit 的审计记录。
3. 不定义各服务日志保留策略、告警策略或观测后端实现。

## 日志与审计边界

日志和审计必须分开处理：

1. 日志是诊断数据，SDK 可以提供字段规范、trace context 和日志 scope。
2. 审计是业务与合规事实，正式 `AuditRecord` 只能由平台服务端在校验主体、权限、组织、环境和资源范围后生成。
3. SDK 可以提交 `AuditIntent`、`OperationResult` 或动作上下文，但不能直接决定审计是否成立。
4. 客户端日志缺失不应影响服务端审计事实；服务端审计失败也不能被客户端本地日志替代。

## 与服务发现注册的关系

1. 服务发现注册仍然属于 Connector Host 与 Connector Protocol 边界。
2. Connector 发现本地目标，Connector Host 生成注册、心跳和状态快照。
3. `Sdk.ConnectorProtocol` 只负责把这些公开契约可靠发送到平台。
4. File Storage、Ops、Notification、Observability 和 Auth SDK 模块不会改变 AppHub 的应用与实例事实所有权。
5. Connector Host 可以在动作结果或诊断场景中组合使用 `Sdk.FileStorage` 上传附件，再通过 `Sdk.Ops` 回传 `fileId`。

## 依赖规则

1. 所有 SDK 模块可以依赖 `Sdk.Core`。
2. 需要认证的 SDK 模块可以依赖 `Sdk.Auth` 或接收外部 token provider。
3. `Sdk.ConnectorProtocol`、`Sdk.FileStorage`、`Sdk.Ops`、`Sdk.Notification` 之间不能互相强依赖；组合编排应留在调用方。
4. `Sdk.Observability` 只能提供上下文与日志辅助，不依赖 Ops 或 File Storage。
5. SDK 不引用 backend/services、backend/gateway 下任何 Web、Domain、Infrastructure 项目。

## 版本与发布

1. SDK 主版本必须与主平台主版本对齐。
2. 同一主版本内，SDK 小版本可以低于主平台小版本；主平台应尽量兼容受支持的小版本 SDK。
3. 新增可选字段、新增客户端方法、新增错误码和新增 scope 属于同主版本兼容变更。
4. 删除字段、改变认证语义、改变授权 scope 语义、移除方法或改变事件含义属于破坏性变更，必须提升主版本。
5. 每个 SDK 请求应携带 SDK 名称与版本，便于服务端兼容性诊断和观测。

## 当前实现状态

第一、第二阶段已经落地以下最小可用 SDK 边界：

1. `Sdk.Core`：公共上下文、错误模型、HTTP transport。
2. `Sdk.Auth`：Connector Host credential 注入和 token 处理薄封装。
3. `Sdk.ConnectorProtocol`：注册、心跳、状态快照客户端。
4. `Sdk.FileStorage`：上传会话和上传指令客户端可以先落接口骨架，文件内容流转不阻塞第一条 Connector Host 纵切。
5. `Sdk.Ops`：pending task 拉取、operation result 回传和本地 Connector Host 认证头注入。

`Sdk.Notification` 和 `Sdk.Observability` 仍可以先冻结接口方向，在站内通知纵切和诊断附件场景中补齐；`Sdk.Ops` 后续需要继续补充任务创建、任务详情、附件引用、持久化 outbox 协作和完整 IAM scope 支持。
