# Notification MVP 实施计划

> **面向智能体工作者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**新增首个 Notification 纵切：站内通知意图、消息、任务、Ops 失败事件消费、Gateway 外观层，以及 Console 已读状态界面。

**架构：**新增 CleanDDD `Notification` 服务，包含 `Domain`、`Infrastructure` 和 `Web` 项目。公开 DTO 保留在 `Nerv.IIP.Contracts.Notification`，客户端辅助能力保留在 `Nerv.IIP.Sdk.Notification`，Console 访问经 PlatformGateway 路由并执行 IAM 支撑的权限检查。本阶段 FileStorage 仅作为 `resourceRef` 或 `fileId` 形式的弱引用。

**技术栈：**.NET 10、FastEndpoints、netcorepal/CleanDDD、EF Core PostgreSQL 迁移、CAP 集成事件、xUnit、Vue 3、Pinia Colada 生成的 API 客户端、shadcn-vue。

---

## 文件结构图

```text
backend/common/Contracts/Nerv.IIP.Contracts.Notification/
  Nerv.IIP.Contracts.Notification.csproj
  NotificationContracts.cs

backend/tests/Nerv.IIP.Contracts.Notification.Tests/
  Nerv.IIP.Contracts.Notification.Tests.csproj
  NotificationContractJsonTests.cs

backend/common/Sdk/Nerv.IIP.Sdk.Notification/
  Nerv.IIP.Sdk.Notification.csproj
  NotificationClient.cs

backend/services/Notification/src/Nerv.IIP.Notification.Domain/
  AggregatesModel/NotificationIntentAggregate/NotificationIntent.cs
  DomainEvents/NotificationDomainEvents.cs

backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/
  ApplicationDbContext.cs
  EntityConfigurations/NotificationIntentEntityTypeConfiguration.cs
  EntityConfigurations/NotificationMessageEntityTypeConfiguration.cs
  EntityConfigurations/NotificationTaskEntityTypeConfiguration.cs
  EntityConfigurations/DeliveryAttemptEntityTypeConfiguration.cs
  Repositories/NotificationIntentRepository.cs
  Migrations/

backend/services/Notification/src/Nerv.IIP.Notification.Web/
  Application/Commands/Notifications/SubmitNotificationIntentCommand.cs
  Application/Commands/Notifications/MarkNotificationMessageReadCommand.cs
  Application/Commands/Notifications/MarkNotificationMessagesReadCommand.cs
  Application/Queries/Notifications/ListNotificationMessagesQuery.cs
  Application/Queries/Notifications/ListNotificationTasksQuery.cs
  Application/IntegrationEventHandlers/OperationTaskFailedIntegrationEventHandlerForNotification.cs
  Endpoints/Notifications/SubmitNotificationIntentEndpoint.cs
  Endpoints/Notifications/ListNotificationMessagesEndpoint.cs
  Endpoints/Notifications/ListNotificationTasksEndpoint.cs
  Endpoints/Notifications/MarkNotificationMessageReadEndpoint.cs
  Endpoints/Notifications/MarkNotificationMessagesReadEndpoint.cs
  Program.cs

backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/
  NotificationIntentTests.cs

backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/
  NotificationEndpointTests.cs
  NotificationPostgresProfileTests.cs
  NotificationSchemaConventionTests.cs
  OperationTaskFailedNotificationConsumerTests.cs

backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/
  Endpoints/Console/Notifications/

frontend/apps/console/src/
  composables/useNotifications.ts
  pages/notifications/index.vue
```

## 任务 1：契约与 SDK 最小集

**文件：**

- 新建：`backend/common/Contracts/Nerv.IIP.Contracts.Notification/Nerv.IIP.Contracts.Notification.csproj`
- 新建：`backend/common/Contracts/Nerv.IIP.Contracts.Notification/NotificationContracts.cs`
- 新建：`backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj`
- 新建：`backend/tests/Nerv.IIP.Contracts.Notification.Tests/NotificationContractJsonTests.cs`
- 新建：`backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj`
- 新建：`backend/common/Sdk/Nerv.IIP.Sdk.Notification/NotificationClient.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建项目**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.Notification -o backend/common/Contracts/Nerv.IIP.Contracts.Notification --framework net10.0
dotnet new xunit -n Nerv.IIP.Contracts.Notification.Tests -o backend/tests/Nerv.IIP.Contracts.Notification.Tests --framework net10.0
dotnet new classlib -n Nerv.IIP.Sdk.Notification -o backend/common/Sdk/Nerv.IIP.Sdk.Notification --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Notification/Nerv.IIP.Contracts.Notification.csproj
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj reference backend/common/Sdk/Nerv.IIP.Sdk.Core/Nerv.IIP.Sdk.Core.csproj
dotnet add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Notification/Nerv.IIP.Contracts.Notification.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.Notification/Nerv.IIP.Contracts.Notification.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj
```

预期：项目已创建并加入后端解决方案。

- [ ] **步骤 2：编写预期失败的 JSON 契约测试**

在 `NotificationContractJsonTests.cs` 中创建序列化以下内容的测试：

```csharp
new SubmitNotificationIntentRequest(
    SourceService: "ops",
    SourceEventType: "ops.OperationTaskFailed",
    SourceEventId: "event-001",
    IntentType: "task",
    Severity: "critical",
    DedupeKey: "ops.OperationTaskFailed:task-001",
    Resource: new NotificationResourceRef("operation-task", "task-001", null),
    Title: "Restart failed",
    Summary: "Instance restart failed with timeout.",
    SuggestedRecipientRefs: ["role:ops-admin"]);
```

断言 JSON 包含 `sourceService`、`sourceEventType`、`dedupeKey`、`suggestedRecipientRefs`，以及嵌套的 `resourceId`。

预期：失败，因为契约尚不存在。

- [ ] **步骤 3：添加契约**

在 `NotificationContracts.cs` 中创建以下记录类型：

```csharp
namespace Nerv.IIP.Contracts.Notification;

public sealed record SubmitNotificationIntentRequest(
    string SourceService,
    string SourceEventType,
    string SourceEventId,
    string IntentType,
    string Severity,
    string DedupeKey,
    NotificationResourceRef? Resource,
    string Title,
    string Summary,
    IReadOnlyCollection<string> SuggestedRecipientRefs);

public sealed record NotificationResourceRef(string ResourceType, string ResourceId, string? FileId);

public sealed record NotificationIntentResponse(
    string IntentId,
    bool Duplicate,
    IReadOnlyCollection<NotificationMessageResponse> Messages);

public sealed record NotificationMessageResponse(
    string MessageId,
    string IntentId,
    string RecipientRef,
    string Status,
    string Severity,
    string Title,
    string Summary,
    NotificationResourceRef? Resource,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);

public sealed record NotificationMessageListResponse(IReadOnlyCollection<NotificationMessageResponse> Items);

public sealed record NotificationTaskResponse(
    string TaskId,
    string MessageId,
    string RecipientRef,
    string TaskType,
    string Status,
    string? ActionRef,
    DateTimeOffset CreatedAtUtc);

public sealed record NotificationTaskListResponse(IReadOnlyCollection<NotificationTaskResponse> Items);

public sealed record MarkNotificationMessageReadResponse(string MessageId, string Status, DateTimeOffset ReadAtUtc);

public static class NotificationContractConstants
{
    public const string IntentTypeMessage = "message";
    public const string IntentTypeTask = "task";
    public const string SeverityInfo = "info";
    public const string SeverityWarning = "warning";
    public const string SeverityCritical = "critical";
}
```

- [ ] **步骤 4：编写预期失败的 SDK 路由测试**

为 `NotificationClient.SubmitIntentAsync` 创建测试，并使用模拟的 `HttpMessageHandler`。断言它发送 `POST /api/notifications/v1/intents`、序列化契约请求，并通过现有 SDK 核心请求上下文应用组织、环境、关联与幂等请求头。

预期：失败，因为 `NotificationClient` 不存在。

- [ ] **步骤 5：实现 SDK 客户端**

首先只实现 `SubmitIntentAsync`：

```csharp
public sealed class NotificationClient(HttpClient httpClient)
{
    public async Task<NotificationIntentResponse?> SubmitIntentAsync(
        SubmitNotificationIntentRequest request,
        PlatformRequestContext context,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = PlatformApiClient.CreateRequest(
            HttpMethod.Post,
            "/api/notifications/v1/intents",
            PlatformApiOptions.Default,
            context);
        httpRequest.Content = JsonContent.Create(request);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken);
    }
}
```

如果当前 `Sdk.Core` 尚未公开 `PlatformApiClient`，则添加最小的本地辅助类，或将 SDK 实现延后到业务集成就绪分支。SDK 不得引用 Notification 服务项目。

- [ ] **步骤 6：运行测试**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj
```

预期：通过。

## 任务 2：领域模型

**文件：**

- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj`
- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Domain/AggregatesModel/NotificationIntentAggregate/NotificationIntent.cs`
- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Domain/DomainEvents/NotificationDomainEvents.cs`
- 新建：`backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj`
- 新建：`backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/NotificationIntentTests.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建领域项目和测试项目**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Notification.Domain -o backend/services/Notification/src/Nerv.IIP.Notification.Domain --framework net10.0
dotnet new xunit -n Nerv.IIP.Notification.Domain.Tests -o backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests --framework net10.0
dotnet add backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj reference backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj
```

- [ ] **步骤 2：编写预期失败的领域测试**

添加以下测试：

1. 创建任务意图时，为每个收件人创建一条消息，并为每条消息创建一个任务。
2. 创建消息意图时创建消息，但不创建任务。
3. 将未读消息标记为已读时，把状态设为 `read` 并记录 `ReadAtUtc`。
4. 再次将已读消息标记为已读具备幂等性。
5. 标题、摘要、组织、环境、来源或收件人引用为空时抛出 `KnownException`。

预期：失败，因为聚合尚不存在。

- [ ] **步骤 3：实现聚合和事件**

使用 CleanDDD 聚合风格：

```csharp
public partial record NotificationIntentId : IGuidStronglyTypedId;
public partial record NotificationMessageId : IGuidStronglyTypedId;
public partial record NotificationTaskId : IGuidStronglyTypedId;

public class NotificationIntent : Entity<NotificationIntentId>, IAggregateRoot
{
    protected NotificationIntent() { }

    public NotificationIntent(/* required fields */)
    {
        // validate required fields
        // assign fields
        // create messages/tasks
        this.AddDomainEvent(new NotificationIntentSubmittedDomainEvent(this));
    }

    public IReadOnlyCollection<NotificationMessage> Messages => _messages;
    public IReadOnlyCollection<NotificationTask> Tasks => _tasks;

    public NotificationMessage MarkRead(NotificationMessageId messageId, DateTimeOffset now)
    {
        // find message, transition to read, raise event only on first transition
    }
}
```

领域事件：

```csharp
public sealed record NotificationIntentSubmittedDomainEvent(NotificationIntent Intent) : IDomainEvent;
public sealed record NotificationMessageReadDomainEvent(NotificationIntent Intent, NotificationMessage Message) : IDomainEvent;
```

- [ ] **步骤 4：运行领域测试**

运行：

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj
```

预期：通过。

## 任务 3：持久化与数据库 schema 约定

**文件：**

- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj`
- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/ApplicationDbContext.cs`
- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/EntityConfigurations/*.cs`
- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Repositories/NotificationIntentRepository.cs`
- 新建：`backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/NotificationSchemaConventionTests.cs`
- 新建：`backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/NotificationPostgresProfileTests.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建基础设施项目**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Notification.Infrastructure -o backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure --framework net10.0
dotnet add backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj reference backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj
```

- [ ] **步骤 2：编写预期失败的 schema 约定测试**

使用现有 `Nerv.IIP.Testing` 数据库 schema 约定辅助类。断言：

1. 默认数据库 schema 为 `notification`。
2. `__EFMigrationsHistory` 位于 `notification`。
3. 业务表带有表注释。
4. 业务列带有注释。
5. 字符串 ID 和文本/JSON 兼容性遵循仓库规则。

预期：失败，因为 DbContext 和映射尚不存在。

- [ ] **步骤 3：实现 DbContext 和映射**

创建 `ApplicationDbContext`：

```csharp
public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator)
{
    public DbSet<NotificationIntent> NotificationIntents => Set<NotificationIntent>();
}
```

配置：

```text
notification_intents
notification_messages
notification_tasks
delivery_attempts
processed_integration_events
```

为每个业务表和业务列添加注释。按规格配置唯一去重索引，以及收件人/状态查询索引。

- [ ] **步骤 4：生成初始迁移**

使用 PostgreSQL 配置档（profile）运行：

```powershell
$env:Persistence__Provider="PostgreSQL"
$env:ConnectionStrings__NotificationDb="Host=localhost;Port=15432;Database=nerv_iip_notification_dev;Username=postgres;Password=postgres"
dotnet tool run dotnet-ef migrations add InitialNotificationSchema --project backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj --startup-project backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj --output-dir Migrations
```

预期：迁移创建 `notification` 数据库 schema 的表和历史记录配置。

- [ ] **步骤 5：运行持久化测试**

运行：

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --filter "FullyQualifiedName~NotificationSchemaConventionTests|FullyQualifiedName~NotificationPostgresProfileTests"
```

预期：Docker/PostgreSQL 可用时通过；否则将 Docker/PostgreSQL 记录为环境阻塞项。

## 任务 4：Notification Web API

**文件：**

- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj`
- 新建：`backend/services/Notification/src/Nerv.IIP.Notification.Web/Program.cs`
- 新建：文件结构图中列出的命令、查询和 API 端点文件
- 新建：`backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj`
- 新建：`backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/NotificationEndpointTests.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建 Web 项目和测试项目**

运行：

```powershell
dotnet new web -n Nerv.IIP.Notification.Web -o backend/services/Notification/src/Nerv.IIP.Notification.Web --framework net10.0
dotnet new xunit -n Nerv.IIP.Notification.Web.Tests -o backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests --framework net10.0
dotnet add backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj reference backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj
dotnet add backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj reference backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj
dotnet add backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Notification/Nerv.IIP.Contracts.Notification.csproj
dotnet add backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj reference backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj
```

- [ ] **步骤 2：编写预期失败的 API 端点测试**

将 `WebApplicationFactory<Program>` 与 InMemory 提供程序配合使用。覆盖：

1. `POST /api/notifications/v1/intents` 为收件人创建消息。
2. 提交相同的去重键会返回 `Duplicate = true`。
3. `GET /api/notifications/v1/messages?recipientRef=user:admin&status=unread` 返回未读消息。
4. `POST /api/notifications/v1/messages/{messageId}/read` 将消息标记为已读。
5. `GET /api/notifications/v1/tasks?recipientRef=user:admin&status=open` 返回可执行任务。

预期：失败，因为 API 端点尚不存在。

- [ ] **步骤 3：实现命令、查询和 API 端点**

实现：

```text
SubmitNotificationIntentCommand
ListNotificationMessagesQuery
ListNotificationTasksQuery
MarkNotificationMessageReadCommand
MarkNotificationMessagesReadCommand
```

API 端点规则：

1. 使用 FastEndpoints 特性，而不是 `Configure()`。
2. 使用构造函数注入的中介器。
3. 返回 `ResponseData<T>`，并通过 `.AsResponseData()` 包装。
4. 使用 FluentValidation 验证 DTO 字段。
5. 命令处理器不得手动调用 `SaveChanges`。

- [ ] **步骤 4：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --filter FullyQualifiedName~NotificationEndpointTests
```

预期：通过。

## 任务 5：Ops 失败事件消费者

**文件：**

- 修改：`backend/services/Notification/src/Nerv.IIP.Notification.Web/Application/IntegrationEventHandlers/OperationTaskFailedIntegrationEventHandlerForNotification.cs`
- 新建：`backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/OperationTaskFailedNotificationConsumerTests.cs`
- 修改：`backend/services/Notification/src/Nerv.IIP.Notification.Web/Program.cs`

- [ ] **步骤 1：编写预期失败的消费者测试**

构造 `OperationTaskFailedIntegrationEvent` 并调用处理器。断言：

1. 创建一个 `sourceService = ops` 的意图。
2. `sourceEventId` 等于事件 ID。
3. `dedupeKey` 等于事件幂等键。
4. 收件人包含 `role:ops-admin`。
5. 再次处理相同事件不会创建重复消息。

预期：失败，因为消费者尚不存在。

- [ ] **步骤 2：实现消费者**

创建处理器：

```csharp
[IntegrationEventConsumer("Nerv.IIP.Contracts.Ops.OperationTaskFailedIntegrationEvent", "notification.operation-task-failed")]
public sealed class OperationTaskFailedIntegrationEventHandlerForNotification(IMediator mediator)
    : IIntegrationEventHandler<OperationTaskFailedIntegrationEvent>
{
    public async Task HandleAsync(OperationTaskFailedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var request = new SubmitNotificationIntentRequest(
            integrationEvent.SourceService,
            integrationEvent.EventType,
            integrationEvent.EventId,
            NotificationContractConstants.IntentTypeTask,
            NotificationContractConstants.SeverityCritical,
            integrationEvent.IdempotencyKey,
            new NotificationResourceRef("operation-task", integrationEvent.Payload.OperationTaskId, null),
            "Operation failed",
            $"Operation {integrationEvent.Payload.OperationCode} failed for {integrationEvent.Payload.InstanceKey}.",
            ["role:ops-admin"]);

        await mediator.Send(new SubmitNotificationIntentCommand(request), cancellationToken);
    }
}
```

按照 AppHub/Ops 模式在 `Program.cs` 中注册集成事件。

- [ ] **步骤 3：运行消费者测试**

运行：

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --filter FullyQualifiedName~OperationTaskFailedNotificationConsumerTests
```

预期：通过。

## 任务 6：AppHost 与 Gateway 外观层

**文件：**

- 修改：`infra/aspire/Nerv.IIP.AppHost/Program.cs`
- 修改：`infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- 新建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Console/Notifications/*.cs`
- 修改：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/*.cs`

- [ ] **步骤 1：编写预期失败的 Gateway 外观层测试**

参照 IAM 管理外观层风格添加测试：

1. `GET /api/console/v1/notifications/messages` 要求通知读取权限。
2. `POST /api/console/v1/notifications/messages/{messageId}/read` 要求通知写入权限。
3. Gateway 将持有者令牌（bearer token）和上下文请求头转发给 Notification 服务。

预期：失败，因为 Gateway 路由尚不存在。

- [ ] **步骤 2：实现 Gateway 路由**

创建外观层 API 端点：

```text
ListConsoleNotificationMessagesEndpoint
ListConsoleNotificationTasksEndpoint
SubmitConsoleNotificationIntentEndpoint
MarkConsoleNotificationMessageReadEndpoint
MarkConsoleNotificationMessagesReadEndpoint
```

使用现有 Console 外观层辅助类和 IAM 授权检查模式。权限名称：

```text
notification.messages.read
notification.messages.write
notification.intents.submit
notification.tasks.read
```

- [ ] **步骤 3：接入 AppHost**

将 Notification Web 项目添加到 AppHost，端口使用 `5106`，除非现有项目约定分配了另一个空闲的平台端口。Gateway `5100`、Console `5105` 和 FileStorage `5104` 保持不变。

- [ ] **步骤 4：运行 Gateway 测试并构建 AppHost**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：通过。

## 任务 7：Console 通知界面

**文件：**

- 新建：`frontend/apps/console/src/composables/useNotifications.ts`
- 新建：`frontend/apps/console/src/pages/notifications/index.vue`
- 修改：根据当前文件路由结构修改 Console 路由/导航文件
- 新建或修改：通知功能的前端专项测试

- [ ] **步骤 1：重新生成 API 客户端**

Gateway OpenAPI 包含通知路由后，运行 Console 使用的现有 OpenAPI/代码生成工作流。

预期：生成的 `@nerv-iip/api-client` 公开 Console 通知操作。

- [ ] **步骤 2：编写预期失败的前端测试**

测试覆盖：

1. 消息页签呈现未读消息和全部消息。
2. 任务页签呈现未完成任务。
3. 标记已读会调用生成的变更操作并更新可见状态。
4. 空状态正常呈现，且不解释实现细节。

预期：失败，因为界面尚不存在。

- [ ] **步骤 3：实现组合式函数和页面**

仅使用生成的 API 客户端。界面应保持紧凑并适合操作：

1. 筛选页签：`Unread`、`All`、`Tasks`。
2. 严重程度徽标。
3. 标题、摘要、创建时间。
4. 资源引用文本。
5. 用于标记已读的图标按钮或小型操作。

使用现有 `@nerv-iip/ui` 基础组件和当前 Console 应用壳模式。

- [ ] **步骤 4：运行前端检查**

运行：

```powershell
pnpm -C frontend typecheck
pnpm -C frontend test -- notifications
```

预期：通过。

## 任务 8：验证与文档

**文件：**

- 修改：`README.md`
- 修改：`docs/architecture/notification-baseline.md`
- 修改：`docs/architecture/platform-sdk-baseline.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 可选新建：`scripts/verify-notification-mvp.ps1`

- [ ] **步骤 1：实施后更新文档**

只更新代码落地后属实的事实：

1. Notification 服务已存在。
2. 已支持的 API 端点。
3. PostgreSQL 数据库 schema 和迁移状态。
4. Gateway 外观层和 Console 路由。
5. 已知限制：没有外部提供程序、没有偏好设置、没有 FileStorage 附件。

- [ ] **步骤 2：需要时添加验证脚本**

如果实施跨越多个项目，则按照脚本治理添加 `scripts/verify-notification-mvp.ps1`。该脚本应运行契约、领域、Web、Gateway、AppHost 和前端专项检查。

- [ ] **步骤 3：运行最终验证**

运行：

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
pnpm -C frontend typecheck
pnpm -C frontend test -- notifications
git diff --check
```

预期：所有可执行检查均通过。如果 Docker/PostgreSQL 不可用，记录受阻的确切 PostgreSQL 配置档检查。

## 自查清单

1. Notification 不引用 FileStorage 实现或对象键。
2. Notification 契约不公开机密、令牌、对象存储键或长期有效的 URL。
3. Gateway 只负责 Console 聚合；Notification 拥有通知事实。
4. Notification 查询和变更路由经 Gateway 为 Console 访问强制执行 IAM 检查。
5. 领域命令不手动调用 `SaveChanges`。
6. PostgreSQL 映射包含表注释、列注释、索引，以及 `notification` 数据库 schema 中的迁移历史记录。
7. 集成事件处理按事件和去重键保证幂等。
8. Console 使用生成的 Gateway API 客户端和现有 UI 基础组件。
9. 文档只提及已实现的行为，不把未来提供程序支持描述为已交付。
