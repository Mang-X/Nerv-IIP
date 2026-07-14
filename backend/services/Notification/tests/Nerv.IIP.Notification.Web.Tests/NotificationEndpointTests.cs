using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Web.Application.DeadLetters;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class NotificationEndpointTests
{
    [Fact]
    public async Task Notification_api_endpoints_require_internal_service_authorization()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateClient();

        var responses = new[]
        {
            await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-unauthorized", "user:admin")),
            await client.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin"),
            await client.PostAsync($"/api/notifications/v1/messages/{Guid.NewGuid()}/read", null),
            await client.PostAsJsonAsync("/api/notifications/v1/messages/read-batch", new { messageIds = new[] { Guid.NewGuid().ToString() } }),
            await client.GetAsync("/api/notifications/v1/tasks?recipientRef=user:admin"),
            await client.GetAsync("/api/notifications/v1/dlq"),
            await client.GetAsync("/api/notifications/v1/dlq/metrics"),
            await client.GetAsync($"/api/notifications/v1/dlq/{Guid.NewGuid()}"),
            await client.PostAsync($"/api/notifications/v1/dlq/{Guid.NewGuid()}/replay", null),
            await client.PostAsJsonAsync("/api/notifications/v1/dlq/replay-batch", new { eventType = "ops.OperationTaskFailed" }),
            await client.PostAsJsonAsync($"/api/notifications/v1/dlq/{Guid.NewGuid()}/ignore", new { reason = "manual triage" }),
            await client.PostAsJsonAsync("/api/notifications/v1/delivery/recipient-channel-bindings", new { recipientRef = "user:admin", channel = "email", recipientAddress = "admin@example.test", enabled = true }),
            await client.PostAsJsonAsync("/api/notifications/v1/delivery/preferences", new { recipientRef = "user:admin", notificationType = "ops.OperationTaskFailed", channel = "email", enabled = true }),
            await client.PostAsJsonAsync("/api/notifications/v1/delivery/subscriptions", new { recipientRef = "user:admin", notificationType = "ops.OperationTaskFailed", channel = "email", enabled = true })
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task Submit_intent_creates_messages_for_recipients()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var response = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-create", "user:admin", "user:operator"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationIntentResponse>(response);
        Assert.False(data.Duplicate);
        Assert.NotEqual(string.Empty, data.IntentId);
        Assert.Equal(["user:admin", "user:operator"], data.Messages.Select(x => x.RecipientRef).Order());
        Assert.All(data.Messages, message => Assert.Equal("unread", message.Status));
    }

    [Fact]
    public async Task Delivery_configuration_endpoints_upsert_binding_preference_and_subscription()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var binding = await client.PostAsJsonAsync(
            "/api/notifications/v1/delivery/recipient-channel-bindings",
            new UpsertNotificationRecipientChannelBindingRequest(
                "user:admin",
                "wecom",
                "wecom-user-001",
                true));
        var preference = await client.PostAsJsonAsync(
            "/api/notifications/v1/delivery/preferences",
            new UpsertNotificationPreferenceRequest(
                "user:admin",
                "industrialTelemetry.AlarmRaised",
                "wecom",
                false));
        var subscription = await client.PostAsJsonAsync(
            "/api/notifications/v1/delivery/subscriptions",
            new UpsertNotificationSubscriptionRequest(
                "user:admin",
                "industrialTelemetry.AlarmRaised",
                "wecom",
                true));

        Assert.Equal(HttpStatusCode.OK, binding.StatusCode);
        Assert.Equal(HttpStatusCode.OK, preference.StatusCode);
        Assert.Equal(HttpStatusCode.OK, subscription.StatusCode);
        Assert.Equal("wecom-user-001", (await ReadDataAsync<NotificationRecipientChannelBindingResponse>(binding)).RecipientAddress);
        Assert.False((await ReadDataAsync<NotificationPreferenceResponse>(preference)).Enabled);
        Assert.True((await ReadDataAsync<NotificationSubscriptionResponse>(subscription)).Enabled);
    }

    [Fact]
    public async Task Submit_intent_with_duplicate_dedupe_key_returns_existing_intent()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var first = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-duplicate", "user:admin"));
        var second = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-duplicate", "user:admin"));

        var firstData = await ReadDataAsync<NotificationIntentResponse>(first);
        var secondData = await ReadDataAsync<NotificationIntentResponse>(second);
        Assert.False(firstData.Duplicate);
        Assert.True(secondData.Duplicate);
        Assert.Equal(firstData.IntentId, secondData.IntentId);
        Assert.Equal(firstData.Messages.Single().MessageId, secondData.Messages.Single().MessageId);
    }

    [Fact]
    public async Task Submit_intent_with_same_dedupe_key_but_different_source_is_not_duplicate()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var first = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-source", "user:admin"));
        var differentEventType = await client.PostAsJsonAsync(
            "/api/notifications/v1/intents",
            CreateIntent("dedupe-source", ["user:admin"], sourceEventType: "ops.OtherFailure"));
        var differentService = await client.PostAsJsonAsync(
            "/api/notifications/v1/intents",
            CreateIntent("dedupe-source", ["user:admin"], sourceService: "iam"));

        var firstData = await ReadDataAsync<NotificationIntentResponse>(first);
        var eventTypeData = await ReadDataAsync<NotificationIntentResponse>(differentEventType);
        var serviceData = await ReadDataAsync<NotificationIntentResponse>(differentService);
        Assert.False(firstData.Duplicate);
        Assert.False(eventTypeData.Duplicate);
        Assert.False(serviceData.Duplicate);
        Assert.NotEqual(firstData.IntentId, eventTypeData.IntentId);
        Assert.NotEqual(firstData.IntentId, serviceData.IntentId);
    }

    [Fact]
    public async Task List_messages_filters_by_recipient_and_status()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-list-admin", "user:admin"));
        await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-list-operator", "user:operator"));

        var response = await client.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationMessageListResponse>(response);
        var message = Assert.Single(data.Items);
        Assert.Equal("user:admin", message.RecipientRef);
        Assert.Equal("unread", message.Status);
    }

    [Fact]
    public async Task List_messages_is_limited_to_request_organization_and_environment()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var orgOneClient = factory.CreateNotificationClient("org-001", "env-001");
        using var orgTwoClient = factory.CreateNotificationClient("org-002", "env-001");

        var orgOneCreated = await orgOneClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-list-org-1", "user:admin"));
        await orgTwoClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-list-org-2", "user:admin"));
        var orgOneMessageId = (await ReadDataAsync<NotificationIntentResponse>(orgOneCreated)).Messages.Single().MessageId;

        var response = await orgOneClient.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationMessageListResponse>(response);
        var message = Assert.Single(data.Items);
        Assert.Equal(orgOneMessageId, message.MessageId);
    }

    [Fact]
    public async Task Mark_message_read_sets_status_and_read_time()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var created = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read", "user:admin"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;

        var response = await client.PostAsync($"/api/notifications/v1/messages/{messageId}/read?recipientRef=user%3Aadmin", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<MarkNotificationMessageReadResponse>(response);
        Assert.Equal(messageId, data.MessageId);
        Assert.Equal("read", data.Status);
        Assert.True(data.ReadAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Mark_message_read_rejects_message_owned_by_another_recipient()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var created = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read-owner", "user:operator"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;

        var response = await client.PostAsync($"/api/notifications/v1/messages/{messageId}/read?recipientRef=user%3Aadmin", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unreadResponse = await client.GetAsync("/api/notifications/v1/messages?recipientRef=user:operator&status=unread");
        var unread = await ReadDataAsync<NotificationMessageListResponse>(unreadResponse);
        Assert.Contains(unread.Items, message => message.MessageId == messageId);
    }

    [Fact]
    public async Task Mark_message_read_from_another_organization_returns_error_and_does_not_mutate()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var orgOneClient = factory.CreateNotificationClient("org-001", "env-001");
        using var orgTwoClient = factory.CreateNotificationClient("org-002", "env-001");
        var created = await orgOneClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-cross-org-read", "user:admin"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;

        var response = await orgTwoClient.PostAsync($"/api/notifications/v1/messages/{messageId}/read", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unreadResponse = await orgOneClient.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");
        var unread = await ReadDataAsync<NotificationMessageListResponse>(unreadResponse);
        Assert.Contains(unread.Items, message => message.MessageId == messageId);
    }

    [Fact]
    public async Task Mark_message_read_with_invalid_message_id_returns_bad_request()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var response = await client.PostAsync("/api/notifications/v1/messages/not-a-guid/read", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Read_batch_marks_multiple_messages_read()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var first = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read-batch-1", "user:admin"));
        var second = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read-batch-2", "user:admin"));
        var messageIds = new[]
        {
            (await ReadDataAsync<NotificationIntentResponse>(first)).Messages.Single().MessageId,
            (await ReadDataAsync<NotificationIntentResponse>(second)).Messages.Single().MessageId,
        };

        var response = await client.PostAsJsonAsync("/api/notifications/v1/messages/read-batch", new { messageIds });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<IReadOnlyCollection<MarkNotificationMessageReadResponse>>(response);
        Assert.Equal(messageIds.Order(), data.Select(x => x.MessageId).Order());
        Assert.All(data, item => Assert.Equal("read", item.Status));
        var unreadResponse = await client.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");
        var unread = await ReadDataAsync<NotificationMessageListResponse>(unreadResponse);
        Assert.Empty(unread.Items);
    }

    [Fact]
    public async Task Read_batch_with_invalid_id_does_not_mark_any_messages_read()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var created = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-read-batch-invalid", "user:admin"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;

        var response = await client.PostAsJsonAsync(
            "/api/notifications/v1/messages/read-batch",
            new { messageIds = new[] { messageId, Guid.NewGuid().ToString() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unreadResponse = await client.GetAsync("/api/notifications/v1/messages?recipientRef=user:admin&status=unread");
        var unread = await ReadDataAsync<NotificationMessageListResponse>(unreadResponse);
        Assert.Contains(unread.Items, message => message.MessageId == messageId);
    }

    [Fact]
    public async Task Read_batch_with_empty_ids_returns_bad_request()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var response = await client.PostAsJsonAsync("/api/notifications/v1/messages/read-batch", new { messageIds = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_tasks_filters_by_recipient_and_status()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();

        var created = await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-task", "user:admin"));
        var messageId = (await ReadDataAsync<NotificationIntentResponse>(created)).Messages.Single().MessageId;
        await client.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-task-other", "user:operator"));

        var response = await client.GetAsync("/api/notifications/v1/tasks?recipientRef=user:admin&status=open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationTaskListResponse>(response);
        var task = Assert.Single(data.Items);
        Assert.Equal(messageId, task.MessageId);
        Assert.Equal("user:admin", task.RecipientRef);
        Assert.Equal("open", task.Status);
    }

    [Fact]
    public async Task List_tasks_is_limited_to_request_organization_and_environment()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var orgOneClient = factory.CreateNotificationClient("org-001", "env-001");
        using var orgTwoClient = factory.CreateNotificationClient("org-002", "env-001");

        var orgOneCreated = await orgOneClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-task-org-1", "user:admin"));
        await orgTwoClient.PostAsJsonAsync("/api/notifications/v1/intents", CreateIntent("dedupe-task-org-2", "user:admin"));
        var orgOneMessageId = (await ReadDataAsync<NotificationIntentResponse>(orgOneCreated)).Messages.Single().MessageId;

        var response = await orgOneClient.GetAsync("/api/notifications/v1/tasks?recipientRef=user:admin&status=open");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationTaskListResponse>(response);
        var task = Assert.Single(data.Items);
        Assert.Equal(orgOneMessageId, task.MessageId);
    }

    [Fact]
    public async Task Dead_letter_endpoints_list_detail_and_ignore_messages()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var pending = await AddDeadLetterAsync(factory, "event-dlq-list", "operation-task-failed:dlq-list");
        var otherType = await AddDeadLetterAsync(factory, "event-dlq-other", "operation-task-failed:dlq-other", eventType: "ops.OtherEvent");

        var listResponse = await client.GetAsync("/api/notifications/v1/dlq?eventType=ops.OperationTaskFailed&status=Pending");
        var detailResponse = await client.GetAsync($"/api/notifications/v1/dlq/{pending.Id}");
        var ignoreResponse = await client.PostAsJsonAsync(
            $"/api/notifications/v1/dlq/{otherType.Id}/ignore",
            new IgnoreNotificationDeadLetterRequest("tracked in replacement incident"));

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ignoreResponse.StatusCode);
        var list = await ReadDataAsync<NotificationDeadLetterListResponse>(listResponse);
        var item = Assert.Single(list.Items);
        Assert.Equal(pending.Id, item.Id);
        Assert.Equal("ops.OperationTaskFailed", item.EventType);
        Assert.Equal("Pending", item.Status);

        var detail = await ReadDataAsync<NotificationDeadLetterDetailResponse>(detailResponse);
        Assert.Equal(pending.Id, detail.Id);
        Assert.Contains("\"eventId\":\"event-dlq-list\"", detail.EventJson, StringComparison.OrdinalIgnoreCase);

        var ignored = await ReadDataAsync<NotificationDeadLetterDetailResponse>(ignoreResponse);
        Assert.Equal("Ignored", ignored.Status);
        Assert.Equal("ignored", ignored.FailureCode);
        Assert.Equal("tracked in replacement incident", ignored.FailureMessage);
    }

    [Fact]
    public async Task Dead_letter_metrics_endpoint_returns_actionable_backlog_counts()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        await AddDeadLetterAsync(factory, "event-dlq-metrics-pending", "operation-task-failed:dlq-metrics-pending");
        var failed = await AddDeadLetterAsync(factory, "event-dlq-metrics-failed", "operation-task-failed:dlq-metrics-failed");
        var ignored = await AddDeadLetterAsync(factory, "event-dlq-metrics-ignored", "operation-task-failed:dlq-metrics-ignored", eventType: "ops.OtherEvent");

        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
            await store.MarkFailedAsync(
                failed.Id,
                "replay-handler-failed",
                "handler still fails",
                DateTimeOffset.Parse("2026-07-04T00:00:00Z"),
                CancellationToken.None);
            await store.MarkIgnoredAsync(
                ignored.Id,
                "tracked elsewhere",
                DateTimeOffset.Parse("2026-07-04T00:01:00Z"),
                CancellationToken.None);
        }

        var response = await client.GetAsync("/api/notifications/v1/dlq/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metrics = await ReadDataAsync<NotificationDeadLetterMetricsResponse>(response);
        Assert.Equal(2, metrics.ActionableCount);
        Assert.Equal(1, metrics.PendingCount);
        Assert.Equal(1, metrics.FailedCount);
        Assert.Equal(1, metrics.IgnoredCount);
        Assert.Equal(0, metrics.ReplayedCount);
        Assert.Contains(metrics.EventTypes, item =>
            item.EventType == "ops.OperationTaskFailed"
            && item.PendingCount == 1
            && item.FailedCount == 1
            && item.ActionableCount == 2);
        Assert.Contains(metrics.EventTypes, item =>
            item.EventType == "ops.OtherEvent"
            && item.IgnoredCount == 1
            && item.ActionableCount == 0);
    }

    [Fact]
    public async Task Dead_letter_alert_monitor_submits_critical_notification_when_backlog_reaches_threshold()
    {
        using var factory = new NotificationWebApplicationFactory();
        await AddDeadLetterAsync(factory, "event-dlq-alert-one", "operation-task-failed:dlq-alert-one");
        await AddDeadLetterAsync(factory, "event-dlq-alert-two", "operation-task-failed:dlq-alert-two");

        using var scope = factory.Services.CreateScope();
        var monitor = ActivatorUtilities.CreateInstance<NotificationDeadLetterAlertMonitor>(
            scope.ServiceProvider,
            Options.Create(new NotificationDeadLetterAlertOptions
            {
                Enabled = true,
                OrganizationId = "org-001",
                EnvironmentId = "env-001",
                Threshold = 2,
                RecipientRefs = ["role:ops-admin"],
                DedupeWindow = TimeSpan.FromHours(1)
            }));

        var first = await monitor.CheckOnceAsync(DateTimeOffset.Parse("2026-07-04T03:30:00Z"), CancellationToken.None);
        var duplicate = await monitor.CheckOnceAsync(DateTimeOffset.Parse("2026-07-04T03:45:00Z"), CancellationToken.None);

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();

        Assert.True(first.AlertSubmitted);
        Assert.True(duplicate.AlertSubmitted);
        Assert.True(duplicate.Duplicate);
        Assert.Equal("notification", intent.SourceService);
        Assert.Equal("notification.DeadLetterBacklogThresholdExceeded", intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityCritical, intent.Severity);
        Assert.Equal("notification-dlq-backlog:org-001:env-001:2:202607040300", intent.DedupeKey);
        Assert.Equal("notification-dead-letter-backlog", intent.ResourceType);
        Assert.Equal("notification-dlq", intent.ResourceId);
        Assert.Equal("role:ops-admin", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
    }

    [Fact]
    public async Task Replay_dead_letter_endpoint_invokes_real_handler_and_marks_message_replayed()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var deadLetter = await AddDeadLetterAsync(factory, "event-dlq-replay", "operation-task-failed:dlq-replay");

        var replayResponse = await client.PostAsync($"/api/notifications/v1/dlq/{deadLetter.Id}/replay", null);

        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = await ReadDataAsync<NotificationDeadLetterReplayResponse>(replayResponse);
        Assert.True(replay.Succeeded);
        Assert.Equal("Replayed", replay.Status);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var updated = await store.GetAsync(deadLetter.Id, CancellationToken.None);
        var intent = await dbContext.NotificationIntents.SingleAsync();
        Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, updated!.Status);
        Assert.NotNull(updated.ReplayedAtUtc);
        Assert.Equal("event-dlq-replay", intent.SourceEventId);
    }

    [Fact]
    public async Task Replay_batch_endpoint_filters_by_event_type_and_replays_pending_messages()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var client = factory.CreateNotificationClient();
        var replayable = await AddDeadLetterAsync(factory, "event-dlq-batch", "operation-task-failed:dlq-batch");
        var otherType = await AddDeadLetterAsync(factory, "event-dlq-batch-other", "operation-task-failed:dlq-batch-other", eventType: "ops.OtherEvent");

        var response = await client.PostAsJsonAsync(
            "/api/notifications/v1/dlq/replay-batch",
            new ReplayNotificationDeadLetterBatchRequest(null, "ops.OperationTaskFailed", null, 10));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await ReadDataAsync<NotificationDeadLetterBatchReplayResponse>(response);
        var result = Assert.Single(data.Items);
        Assert.Equal(replayable.Id, result.Id);
        Assert.True(result.Succeeded);

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        Assert.Equal(IntegrationEventDeadLetterStatus.Replayed, (await store.GetAsync(replayable.Id, CancellationToken.None))!.Status);
        Assert.Equal(IntegrationEventDeadLetterStatus.Pending, (await store.GetAsync(otherType.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Missing_organization_or_environment_header_returns_bad_request()
    {
        using var factory = new NotificationWebApplicationFactory();
        using var missingOrganization = factory.CreateNotificationClient(organizationId: null, environmentId: "env-001");
        using var missingEnvironment = factory.CreateNotificationClient(organizationId: "org-001", environmentId: null);

        var missingOrganizationResponse = await missingOrganization.PostAsJsonAsync(
            "/api/notifications/v1/intents",
            CreateIntent("dedupe-missing-org", "user:admin"));
        var missingEnvironmentResponse = await missingEnvironment.GetAsync("/api/notifications/v1/messages");

        Assert.Equal(HttpStatusCode.BadRequest, missingOrganizationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingEnvironmentResponse.StatusCode);
    }

    private static SubmitNotificationIntentRequest CreateIntent(string dedupeKey, params string[] recipients)
    {
        return CreateIntent(dedupeKey, (IReadOnlyCollection<string>)recipients);
    }

    private static SubmitNotificationIntentRequest CreateIntent(
        string dedupeKey,
        IReadOnlyCollection<string> recipients,
        string sourceService = "ops",
        string sourceEventType = "ops.OperationTaskFailed")
    {
        return new SubmitNotificationIntentRequest(
            SourceService: sourceService,
            SourceEventType: sourceEventType,
            SourceEventId: dedupeKey,
            IntentType: NotificationContractConstants.IntentTypeTask,
            Severity: NotificationContractConstants.SeverityCritical,
            DedupeKey: dedupeKey,
            Resource: new NotificationResourceRef("operation-task", dedupeKey, null),
            Title: "Restart failed",
            Summary: "Instance restart failed with timeout.",
            SuggestedRecipientRefs: recipients);
    }

    private static async Task<IntegrationEventDeadLetterMessage> AddDeadLetterAsync(
        NotificationWebApplicationFactory factory,
        string eventId,
        string idempotencyKey,
        string eventType = "ops.OperationTaskFailed")
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        return await store.AddAsync(
            IntegrationEventDeadLetterMessage.Create(
                OperationTaskFailedIntegrationEventHandlerForNotification.ConsumerName,
                CreateOperationTaskFailedEvent(eventId, idempotencyKey, eventType),
                "handler-retry-exhausted",
                "Simulated exhausted handler exception."),
            CancellationToken.None);
    }

    private static OperationTaskFailedIntegrationEvent CreateOperationTaskFailedEvent(
        string eventId,
        string idempotencyKey,
        string eventType)
    {
        return new OperationTaskFailedIntegrationEvent(
            EventId: eventId,
            EventType: eventType,
            EventVersion: 1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:00Z"),
            SourceService: "ops",
            CorrelationId: $"corr-{eventId}",
            CausationId: $"cause-{eventId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "connector-host-001",
            IdempotencyKey: idempotencyKey,
            Payload: new OperationTaskFailedPayload(
                OperationTaskId: $"task-{eventId}",
                AttemptId: $"attempt-{eventId}",
                InstanceKey: "demo-api-001",
                OperationCode: "lifecycle.restart",
                FinishedAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:05Z"),
                FailureCode: "timeout"));
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var data = document.RootElement.GetProperty("data");
        return data.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    public sealed class NotificationWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                });
            });
        }

        public HttpClient CreateNotificationClient(string? organizationId = "org-001", string? environmentId = "env-001")
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                InternalServiceAuthentication.DefaultDevelopmentBearerToken);
            if (organizationId is not null)
            {
                client.DefaultRequestHeaders.Add("X-Organization-Id", organizationId);
            }

            if (environmentId is not null)
            {
                client.DefaultRequestHeaders.Add("X-Environment-Id", environmentId);
            }

            return client;
        }
    }
}
