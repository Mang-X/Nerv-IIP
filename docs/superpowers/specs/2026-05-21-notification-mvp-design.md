# Notification MVP 设计

> **状态：** 已批准于 2026-05-21 开始实施。

## 背景

Nerv-IIP 已在 `docs/architecture/notification-baseline.md` 中定义平台 Notification 边界，但当前主线没有 Notification service、contracts、SDK、Gateway facade 或 Console 界面。FileStorage MVP 正在另一个 worktree 中开发，本阶段只能将其作为弱引用依赖：Notification 可以存储 `fileId` 或 `resourceRef`，但不得依赖真实附件上传或下载行为。

平台已有 Notification 应复用的稳定模式：AppHub/Ops/IAM CleanDDD 服务布局、PostgreSQL 服务 schema 约定、CAP integration event 约定、Gateway Console facades、IAM-backed 权限强制执行，以及 Phase 8 Console Design System。

## 目标

构建首个 Notification 纵向切片：提交 notification intents、生成应用内用户消息和任务、消费一个 Ops failure event、通过 Gateway 查询通知，并将消息标记为已读。

## 非目标

1. 不包含 email、SMS、企业 IM、webhook 或 provider credentials。
2. 不包含用户偏好或订阅管理。
3. 不包含通知摘要、免打扰时段、throttling 或合并规则。
4. 不包含附件上传或二进制下载集成。
5. 不替代 Ops audit records、Observability alerts 或业务报警规则。
6. Console 不直接访问 Notification service；Console 使用 PlatformGateway facade。

## 推荐方案

使用独立的 `Notification` CleanDDD service，配备 PostgreSQL 持久化和应用内 delivery provider。通过公共 API 接收显式 NotificationIntent 请求，并将 `OperationTaskFailedIntegrationEvent` 作为首个事件驱动来源。为每个已解析收件人生成一条 NotificationMessage；当 intent 可操作时生成一条 NotificationTask。

该方案优于仅 Gateway projection，因为 Notification 拥有持久 delivery、已读状态、dedupe 和未来 provider attempts。它也优于等待 FileStorage 完成，因为 MVP 不需要真实附件。

## 架构

### 服务边界

创建新的后端服务：

```text
backend/services/Notification/
  src/Nerv.IIP.Notification.Domain
  src/Nerv.IIP.Notification.Infrastructure
  src/Nerv.IIP.Notification.Web
  tests/Nerv.IIP.Notification.Domain.Tests
  tests/Nerv.IIP.Notification.Web.Tests
```

创建公共 contracts 和 SDK：

```text
backend/common/Contracts/Nerv.IIP.Contracts.Notification
backend/common/Sdk/Nerv.IIP.Sdk.Notification
backend/tests/Nerv.IIP.Contracts.Notification.Tests
```

服务使用 `notification` 作为 PostgreSQL schema，并在该 schema 中拥有自己的 migrations history table。

### Domain 模型

`NotificationIntent` 记录服务或外部 client 发出的请求，用于把某个平台事实通知给相关人员。

字段：

| 字段 | 含义 |
| --- | --- |
| `intentId` | 持久 intent 标识符。 |
| `sourceService` | 来源边界，例如 `ops`、`apphub` 或 `business-extension`。 |
| `sourceEventType` | Event 或 intent 类型，例如 `ops.OperationTaskFailed`。 |
| `sourceEventId` | 来源 event 或 command 标识符。 |
| `organizationId` | 平台组织上下文。 |
| `environmentId` | 平台环境上下文。 |
| `severity` | `info`、`warning`、`critical`。 |
| `intentType` | `message` 或 `task`。 |
| `dedupeKey` | 业务幂等键。 |
| `resourceRef` | 用于 deep links 的可选资源引用。 |
| `title` | 用户可见标题。 |
| `summary` | 不含 secrets 的用户可见摘要。 |
| `createdAtUtc` | 创建时间戳。 |

`NotificationMessage` 是用户可见的应用内消息。

Fields:

| 字段 | 含义 |
| --- | --- |
| `messageId` | 持久消息标识符。 |
| `intentId` | 父 intent 标识符。 |
| `recipientRef` | 收件人，例如 `user:admin` 或 `role:ops-admin`。 |
| `status` | `unread`、`read`、`archived`、`ignored`。 |
| `title` | 用户可见标题。 |
| `summary` | 用户可见摘要。 |
| `severity` | 为查询性能从 intent 复制。 |
| `resourceRef` | 可选资源引用。 |
| `createdAtUtc` | 消息创建时间戳。 |
| `readAtUtc` | 适用时的已读时间戳。 |

`NotificationTask` 是供审批、失败处理或人工确认使用的可操作条目。

Fields:

| 字段 | 含义 |
| --- | --- |
| `taskId` | 持久任务标识符。 |
| `messageId` | 关联消息。 |
| `taskType` | `review`、`approve`、`retry`、`acknowledge`。 |
| `status` | `open`、`completed`、`cancelled`。 |
| `actionRef` | 可选链接目标或 command 引用。 |

`DeliveryAttempt` 记录本 MVP 中的应用内 delivery attempt。现在即建立该模型，使未来外部 providers 无需重塑消息创建模型。

### 收件人模型

MVP 仅支持显式建议收件人 refs：

```text
user:{userId}
role:{roleCode}
```

本阶段可在 interface 后以 stub 实现通过 IAM 展开角色。查询路径仍必须把 IAM 视为权威来源：Gateway 在转发 Console 请求前执行权限检查，Notification 绝不自行杜撰 user/role 事实。

### API 界面

Notification service 公共 API：

```text
POST /api/notifications/v1/intents
GET  /api/notifications/v1/messages
GET  /api/notifications/v1/tasks
POST /api/notifications/v1/messages/{messageId}/read
POST /api/notifications/v1/messages/read-batch
```

Gateway Console facade：

```text
POST /api/console/v1/notifications/intents
GET  /api/console/v1/notifications/messages
GET  /api/console/v1/notifications/tasks
POST /api/console/v1/notifications/messages/{messageId}/read
POST /api/console/v1/notifications/messages/read-batch
```

该 facade 转发 bearer token、organization/environment 上下文、correlation ID 和 idempotency key。

### Contracts

`Nerv.IIP.Contracts.Notification` 拥有以下 request 和 response DTOs：

1. `SubmitNotificationIntentRequest`
2. `NotificationIntentResponse`
3. `NotificationMessageListResponse`
4. `NotificationTaskListResponse`
5. `MarkNotificationMessageReadResponse`

SDK 只封装这些公共 DTOs 和 routes。它不实现收件人解析、delivery providers 或已读状态策略。

### Event 消费

首个 event consumer 处理：

```text
Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent
```

映射：

| 来源 | Notification 字段 |
| --- | --- |
| `EventId` | `sourceEventId` |
| `EventType` | `sourceEventType` |
| `SourceService` | `sourceService` |
| `OrganizationId` | `organizationId` |
| `EnvironmentId` | `environmentId` |
| `IdempotencyKey` | `dedupeKey` |
| `Payload.OperationTaskId` | `resourceRef.resourceId` |
| `Payload.OperationCode` | `title`/`summary` 上下文 |
| `Payload.FailureCode` | `summary` 上下文 |

MVP 默认建议收件人为 `role:ops-admin`。

### Console 界面

增加紧凑的通知页面和 shell indicator：

```text
frontend/apps/console/src/pages/notifications/index.vue
frontend/apps/console/src/composables/useNotifications.ts
```

页面显示未读/全部筛选、severity、标题、摘要、创建时间、资源链接文本和标记已读操作。Tasks 可以作为同一页面的 tab 显示。shell indicator 可以是从 messages query 获取的简单未读数量。

UI 使用现有 `@nerv-iip/ui` primitives 和 Calm Control Plane 蓝色 semantic tokens。本 MVP 不包含新的 Design System 决策。

## 数据流

1. 外部调用方提交 intent，或 Ops 发布 `OperationTaskFailedIntegrationEvent`。
2. Notification 校验 organization/environment、source、severity、intent type、title、summary、recipients 和 dedupe key。
3. Notification 针对每个 dedupe key 只插入一次 `NotificationIntent`。
4. Notification 解析显式收件人并创建消息。
5. Notification 仅为 `intentType = task` 创建 tasks。
6. Notification 写入状态为成功的应用内 `DeliveryAttempt` 行。
7. Gateway 检查 IAM 权限并转发 Console 通知查询/操作。
8. Console 通过 Gateway 渲染消息并将消息标记为已读。

## 持久化

表：

```text
notification.notification_intents
notification.notification_messages
notification.notification_tasks
notification.delivery_attempts
notification.processed_integration_events
notification.__EFMigrationsHistory
```

索引：

1. 唯一 intent dedupe：`organization_id`、`environment_id`、`source_service`、`source_event_type`、`dedupe_key`。
2. 消息列表：`recipient_ref`、`status`、`created_at_utc desc`。
3. 任务列表：`recipient_ref`、`status`、`created_at_utc desc`。
4. 已处理 event：`consumer_name`、`event_id`。

所有业务表必须具有表注释和列注释。JSON/text 兼容性规则遵循 AppHub/Ops/IAM schema 约定测试。

## 错误处理

1. 重复 intents 返回现有 intent 结果，而不创建更多消息。
2. 缺少 organization/environment、title、summary、source 或 recipient refs 时返回 validation error。
3. 不支持的 severity 或 intent type 返回 validation error。
4. 查询或修改调用方收件人上下文之外的消息时，依据现有 Gateway/IAM facade 约定返回 not found 或 forbidden。
5. `processed_integration_events` 记录成功后，重复 integration events 将被忽略。
6. Event 处理失败日志记录 event ID、event type、correlation ID、organization ID、environment ID 和 consumer name。

## 测试策略

1. Domain tests 覆盖 intent 创建、dedupe、消息生成、任务生成和已读状态转换。
2. Contract JSON tests 覆盖 camelCase 名称和稳定 DTO shape。
3. Web tests 覆盖 intent 提交、消息列表、任务列表、标记已读和重复 intent 行为。
4. PostgreSQL profile tests 覆盖 migrations 和 schema conventions。
5. Gateway tests 覆盖 IAM-backed 权限强制执行和 bearer 转发。
6. Frontend tests 使用 mock API 响应覆盖 notifications composable 和页面渲染。

## 验收标准

1. `dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj` 通过。
2. `dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj` 通过。
3. `dotnet test backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj` 通过。
4. Gateway facade 变更后，`dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj` 通过。
5. Console 变更后，`pnpm -C frontend typecheck` 和 focused notification tests 通过。
6. AppHost 接线后，`dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore` 通过。

## 合并协调

避免在本分支编辑 FileStorage MVP 文件。将大范围 readiness 文档编辑延后到实现完成，以减少与 File MVP worktree 的冲突。可能冲突的文件包括：

1. `backend/Nerv.IIP.sln`
2. `docs/architecture/implementation-readiness.md`
3. `docs/superpowers/plans/2026-05-21-next-stage-stabilization-and-readiness.md`
4. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
