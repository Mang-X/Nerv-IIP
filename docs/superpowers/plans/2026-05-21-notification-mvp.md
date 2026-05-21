# Notification MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the first Notification vertical slice: in-app notification intents, messages, tasks, Ops failure event consumption, Gateway facade, and Console read-state UI.

**Architecture:** Add a new CleanDDD `Notification` service with `Domain`, `Infrastructure`, and `Web` projects. Keep public DTOs in `Nerv.IIP.Contracts.Notification`, keep client helpers in `Nerv.IIP.Sdk.Notification`, and route Console access through PlatformGateway with IAM-backed permission checks. FileStorage is only a weak `resourceRef` or `fileId` reference in this phase.

**Tech Stack:** .NET 10, FastEndpoints, netcorepal/CleanDDD, EF Core PostgreSQL migrations, CAP integration events, xUnit, Vue 3, Pinia Colada generated API client, shadcn-vue.

---

## File Structure Map

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

## Task 1: Contracts and SDK Minimum

**Files:**

- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Notification/Nerv.IIP.Contracts.Notification.csproj`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Notification/NotificationContracts.cs`
- Create: `backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Contracts.Notification.Tests/NotificationContractJsonTests.cs`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj`
- Create: `backend/common/Sdk/Nerv.IIP.Sdk.Notification/NotificationClient.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create projects**

Run:

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

Expected: projects are created and added to the backend solution.

- [ ] **Step 2: Write failing JSON contract tests**

Create `NotificationContractJsonTests.cs` with tests that serialize:

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

Assert JSON contains `sourceService`, `sourceEventType`, `dedupeKey`, `suggestedRecipientRefs`, and nested `resourceId`.

Expected: FAIL because the contracts do not exist yet.

- [ ] **Step 3: Add contracts**

Create these records in `NotificationContracts.cs`:

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

- [ ] **Step 4: Write failing SDK route test**

Create a test for `NotificationClient.SubmitIntentAsync` using a fake `HttpMessageHandler`. Assert it sends `POST /api/notifications/v1/intents`, serializes the contract request, and applies organization/environment/correlation/idempotency headers through the existing SDK core request context.

Expected: FAIL because `NotificationClient` does not exist.

- [ ] **Step 5: Implement SDK client**

Implement only `SubmitIntentAsync` first:

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

If the current `Sdk.Core` does not yet expose `PlatformApiClient`, add a minimal local helper or defer SDK implementation to the business integration readiness branch. Do not reference Notification service projects from the SDK.

- [ ] **Step 6: Run tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Notification.Tests/Nerv.IIP.Contracts.Notification.Tests.csproj
dotnet build backend/common/Sdk/Nerv.IIP.Sdk.Notification/Nerv.IIP.Sdk.Notification.csproj
```

Expected: PASS.

## Task 2: Domain Model

**Files:**

- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj`
- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Domain/AggregatesModel/NotificationIntentAggregate/NotificationIntent.cs`
- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Domain/DomainEvents/NotificationDomainEvents.cs`
- Create: `backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj`
- Create: `backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/NotificationIntentTests.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create domain and test projects**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Notification.Domain -o backend/services/Notification/src/Nerv.IIP.Notification.Domain --framework net10.0
dotnet new xunit -n Nerv.IIP.Notification.Domain.Tests -o backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests --framework net10.0
dotnet add backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj reference backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj
```

- [ ] **Step 2: Write failing domain tests**

Add tests for:

1. Creating a task intent creates one message per recipient and one task per message.
2. Creating a message intent creates messages but no tasks.
3. Marking an unread message read sets status to `read` and records `ReadAtUtc`.
4. Marking a read message read again is idempotent.
5. Blank title, summary, organization, environment, source, or recipient refs throw `KnownException`.

Expected: FAIL because the aggregate does not exist.

- [ ] **Step 3: Implement aggregate and events**

Use CleanDDD aggregate style:

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

Domain events:

```csharp
public sealed record NotificationIntentSubmittedDomainEvent(NotificationIntent Intent) : IDomainEvent;
public sealed record NotificationMessageReadDomainEvent(NotificationIntent Intent, NotificationMessage Message) : IDomainEvent;
```

- [ ] **Step 4: Run domain tests**

Run:

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Domain.Tests/Nerv.IIP.Notification.Domain.Tests.csproj
```

Expected: PASS.

## Task 3: Persistence and Schema Convention

**Files:**

- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj`
- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Repositories/NotificationIntentRepository.cs`
- Create: `backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/NotificationSchemaConventionTests.cs`
- Create: `backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/NotificationPostgresProfileTests.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create infrastructure project**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Notification.Infrastructure -o backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure --framework net10.0
dotnet add backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj reference backend/services/Notification/src/Nerv.IIP.Notification.Domain/Nerv.IIP.Notification.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj
```

- [ ] **Step 2: Write failing schema convention tests**

Use the existing `Nerv.IIP.Testing` schema convention helper. Assert:

1. Default schema is `notification`.
2. `__EFMigrationsHistory` is in `notification`.
3. Business tables have table comments.
4. Business columns have comments.
5. String IDs and text/json compatibility follow repository rules.

Expected: FAIL because DbContext and mappings do not exist.

- [ ] **Step 3: Implement DbContext and mappings**

Create `ApplicationDbContext`:

```csharp
public partial class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IMediator mediator)
    : AppDbContextBase(options, mediator)
{
    public DbSet<NotificationIntent> NotificationIntents => Set<NotificationIntent>();
}
```

Configure:

```text
notification_intents
notification_messages
notification_tasks
delivery_attempts
processed_integration_events
```

Add comments for every business table and business column. Configure unique dedupe index and recipient/status query indexes from the spec.

- [ ] **Step 4: Generate initial migration**

Run with PostgreSQL profile:

```powershell
$env:Persistence__Provider="PostgreSQL"
$env:ConnectionStrings__NotificationDb="Host=localhost;Port=15432;Database=nerv_iip_notification_dev;Username=postgres;Password=postgres"
dotnet tool run dotnet-ef migrations add InitialNotificationSchema --project backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Nerv.IIP.Notification.Infrastructure.csproj --startup-project backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj --output-dir Migrations
```

Expected: migration creates the `notification` schema tables and history configuration.

- [ ] **Step 5: Run persistence tests**

Run:

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --filter "FullyQualifiedName~NotificationSchemaConventionTests|FullyQualifiedName~NotificationPostgresProfileTests"
```

Expected: PASS when Docker/PostgreSQL is available; otherwise record Docker/PostgreSQL as an environment blocker.

## Task 4: Notification Web API

**Files:**

- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Web/Nerv.IIP.Notification.Web.csproj`
- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Web/Program.cs`
- Create: command/query and endpoint files listed in the file structure map
- Create: `backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj`
- Create: `backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/NotificationEndpointTests.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create Web and test projects**

Run:

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

- [ ] **Step 2: Write failing endpoint tests**

Use `WebApplicationFactory<Program>` with InMemory provider. Cover:

1. `POST /api/notifications/v1/intents` creates messages for recipients.
2. Posting the same dedupe key returns `Duplicate = true`.
3. `GET /api/notifications/v1/messages?recipientRef=user:admin&status=unread` returns unread messages.
4. `POST /api/notifications/v1/messages/{messageId}/read` marks a message read.
5. `GET /api/notifications/v1/tasks?recipientRef=user:admin&status=open` returns actionable tasks.

Expected: FAIL because endpoints do not exist.

- [ ] **Step 3: Implement commands, queries, and endpoints**

Implement:

```text
SubmitNotificationIntentCommand
ListNotificationMessagesQuery
ListNotificationTasksQuery
MarkNotificationMessageReadCommand
MarkNotificationMessagesReadCommand
```

Endpoint rules:

1. Use FastEndpoints attributes, not `Configure()`.
2. Use mediator from constructor injection.
3. Return `ResponseData<T>` through `.AsResponseData()`.
4. Validate DTO fields with FluentValidation.
5. Do not call `SaveChanges` manually in command handlers.

- [ ] **Step 4: Run web tests**

Run:

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --filter FullyQualifiedName~NotificationEndpointTests
```

Expected: PASS.

## Task 5: Ops Failed Event Consumer

**Files:**

- Modify: `backend/services/Notification/src/Nerv.IIP.Notification.Web/Application/IntegrationEventHandlers/OperationTaskFailedIntegrationEventHandlerForNotification.cs`
- Create: `backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/OperationTaskFailedNotificationConsumerTests.cs`
- Modify: `backend/services/Notification/src/Nerv.IIP.Notification.Web/Program.cs`

- [ ] **Step 1: Write failing consumer test**

Construct an `OperationTaskFailedIntegrationEvent` and call the handler. Assert:

1. One intent is created with `sourceService = ops`.
2. `sourceEventId` equals the event ID.
3. `dedupeKey` equals event idempotency key.
4. Recipient includes `role:ops-admin`.
5. Rehandling the same event does not create duplicate messages.

Expected: FAIL because consumer does not exist.

- [ ] **Step 2: Implement consumer**

Create handler:

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

Register integration events in `Program.cs` following AppHub/Ops patterns.

- [ ] **Step 3: Run consumer tests**

Run:

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --filter FullyQualifiedName~OperationTaskFailedNotificationConsumerTests
```

Expected: PASS.

## Task 6: AppHost and Gateway Facade

**Files:**

- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Console/Notifications/*.cs`
- Modify: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/*.cs`

- [ ] **Step 1: Write failing Gateway facade tests**

Add tests mirroring IAM admin facade style:

1. `GET /api/console/v1/notifications/messages` requires notification read permission.
2. `POST /api/console/v1/notifications/messages/{messageId}/read` requires notification write permission.
3. Gateway forwards bearer token and context headers to Notification service.

Expected: FAIL because Gateway routes do not exist.

- [ ] **Step 2: Implement Gateway routes**

Create facade endpoints:

```text
ListConsoleNotificationMessagesEndpoint
ListConsoleNotificationTasksEndpoint
SubmitConsoleNotificationIntentEndpoint
MarkConsoleNotificationMessageReadEndpoint
MarkConsoleNotificationMessagesReadEndpoint
```

Use the existing Console facade helper and IAM authorization check pattern. Permission names:

```text
notification.messages.read
notification.messages.write
notification.intents.submit
notification.tasks.read
```

- [ ] **Step 3: Wire AppHost**

Add Notification Web project to AppHost with port `5106` unless an existing project convention assigns another free platform port. Keep Gateway `5100`, Console `5105`, and FileStorage `5104` unchanged.

- [ ] **Step 4: Run Gateway and AppHost tests**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: PASS.

## Task 7: Console Notifications UI

**Files:**

- Create: `frontend/apps/console/src/composables/useNotifications.ts`
- Create: `frontend/apps/console/src/pages/notifications/index.vue`
- Modify: Console route/nav files according to current file-route structure
- Create or modify: focused frontend tests for notifications

- [ ] **Step 1: Regenerate API client**

After Gateway OpenAPI includes notification routes, run the existing OpenAPI/codegen workflow used by the console.

Expected: generated `@nerv-iip/api-client` exposes Console Notification operations.

- [ ] **Step 2: Write failing frontend tests**

Tests cover:

1. Messages tab renders unread and all messages.
2. Tasks tab renders open tasks.
3. Mark read calls the generated mutation and updates visible status.
4. Empty state renders without explaining implementation details.

Expected: FAIL because UI does not exist.

- [ ] **Step 3: Implement composable and page**

Use generated API client only. Keep UI compact and operational:

1. Filter tabs: `Unread`, `All`, `Tasks`.
2. Severity badge.
3. Title, summary, created time.
4. Resource reference text.
5. Icon button or small action for mark read.

Use existing `@nerv-iip/ui` primitives and current Console app shell patterns.

- [ ] **Step 4: Run frontend checks**

Run:

```powershell
pnpm -C frontend typecheck
pnpm -C frontend test -- notifications
```

Expected: PASS.

## Task 8: Verification and Documentation

**Files:**

- Modify: `README.md`
- Modify: `docs/architecture/notification-baseline.md`
- Modify: `docs/architecture/platform-sdk-baseline.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Optionally create: `scripts/verify-notification-mvp.ps1`

- [ ] **Step 1: Update docs after implementation**

Update only facts that are true after code lands:

1. Notification service exists.
2. Supported endpoints.
3. PostgreSQL schema and migration status.
4. Gateway facade and Console route.
5. Known limits: no external providers, no preferences, no FileStorage attachments.

- [ ] **Step 2: Add verification script if useful**

If the implementation spans multiple projects, add `scripts/verify-notification-mvp.ps1` following script governance. It should run focused contracts, domain, web, Gateway, AppHost, and frontend checks.

- [ ] **Step 3: Run final verification**

Run:

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

Expected: all available checks pass. If Docker/PostgreSQL is unavailable, record the exact blocked PostgreSQL-profile checks.

## Self-Review Checklist

1. Notification does not reference FileStorage implementation or object keys.
2. Notification contracts do not expose secrets, tokens, object storage keys, or long-lived URLs.
3. Gateway owns Console aggregation only; Notification owns notification facts.
4. Notification query and mutation routes enforce IAM through Gateway for Console access.
5. Domain commands do not call `SaveChanges` manually.
6. PostgreSQL mappings include table comments, column comments, indexes, and `notification` migrations history schema.
7. Integration event handling is idempotent by event and dedupe key.
8. Console uses generated Gateway API client and existing UI primitives.
9. Docs mention only implemented behavior, not future provider support as shipped.
