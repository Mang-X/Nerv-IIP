using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class OperationTaskFailedNotificationConsumerTests
{
    [Fact]
    public async Task Handle_failed_operation_creates_notification_for_ops_admin()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleAsync(factory, CreateEvent("event-001", "operation-task-failed:task-001"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var message = Assert.Single(intent.Messages);
        var task = Assert.Single(intent.Tasks);
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal("org-001", intent.OrganizationId);
        Assert.Equal("env-001", intent.EnvironmentId);
        Assert.Equal("ops", intent.SourceService);
        Assert.Equal("ops.OperationTaskFailed", intent.SourceEventType);
        Assert.Equal("event-001", intent.SourceEventId);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityCritical, intent.Severity);
        Assert.Equal("operation-task-failed:task-001", intent.DedupeKey);
        Assert.Equal("operation-task", intent.ResourceType);
        Assert.Equal("task-001", intent.ResourceId);
        Assert.Equal("role:ops-admin", message.RecipientRef);
        Assert.Equal(message.Id, task.MessageId);
        Assert.Equal("notification.operation-task-failed", processed.ConsumerName);
        Assert.Equal("event-001", processed.EventId);
        Assert.Equal("operation-task-failed:task-001", processed.DedupeKey);
    }

    [Fact]
    public async Task Handle_same_event_twice_does_not_create_duplicate_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateEvent("event-duplicate", "operation-task-failed:duplicate");

        await HandleAsync(factory, integrationEvent);
        await HandleAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await dbContext.NotificationIntents.CountAsync());
        Assert.Equal(1, await dbContext.NotificationMessages.CountAsync());
        Assert.Equal(1, await dbContext.NotificationTasks.CountAsync());
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Handle_different_event_with_different_dedupe_creates_another_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleAsync(factory, CreateEvent("event-first", "operation-task-failed:first", operationTaskId: "task-first"));
        await HandleAsync(factory, CreateEvent("event-second", "operation-task-failed:second", operationTaskId: "task-second"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await dbContext.NotificationIntents.CountAsync());
        Assert.Equal(2, await dbContext.NotificationMessages.CountAsync());
        Assert.Equal(2, await dbContext.NotificationTasks.CountAsync());
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Handle_different_event_with_same_dedupe_records_processed_event_but_reuses_intent()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleAsync(factory, CreateEvent("event-retry-first", "operation-task-failed:retry", operationTaskId: "task-retry"));
        await HandleAsync(factory, CreateEvent("event-retry-second", "operation-task-failed:retry", operationTaskId: "task-retry"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await dbContext.NotificationIntents.CountAsync());
        Assert.Equal(1, await dbContext.NotificationMessages.CountAsync());
        Assert.Equal(1, await dbContext.NotificationTasks.CountAsync());
        Assert.Equal(2, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Handle_event_missing_required_data_is_rejected_without_marking_processed()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateEvent("event-invalid", "operation-task-failed:invalid", operationTaskId: " ");

        await Assert.ThrowsAsync<KnownException>(() => HandleAsync(factory, integrationEvent));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await dbContext.NotificationIntents.ToListAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync());
    }

    [Fact]
    public async Task Handle_unsupported_event_version_is_dead_lettered_without_creating_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleAsync(factory, CreateEvent("event-v2", "operation-task-failed:v2", eventVersion: 2));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deadLetterStore = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
        var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
            OperationTaskFailedIntegrationEventHandlerForNotification.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));

        Assert.Empty(await dbContext.NotificationIntents.ToListAsync());
        Assert.Empty(await dbContext.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    private static async Task HandleAsync(
        NotificationConsumerWebApplicationFactory factory,
        OperationTaskFailedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<OperationTaskFailedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<OperationTaskFailedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static OperationTaskFailedIntegrationEvent CreateEvent(
        string eventId,
        string idempotencyKey,
        string operationTaskId = "task-001",
        int eventVersion = 1)
    {
        return new OperationTaskFailedIntegrationEvent(
            EventId: eventId,
            EventType: "ops.OperationTaskFailed",
            EventVersion: eventVersion,
            OccurredAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:00Z"),
            SourceService: "ops",
            CorrelationId: $"corr-{eventId}",
            CausationId: $"cause-{eventId}",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "connector-host-001",
            IdempotencyKey: idempotencyKey,
            Payload: new OperationTaskFailedPayload(
                OperationTaskId: operationTaskId,
                AttemptId: $"attempt-{eventId}",
                InstanceKey: "demo-api-001",
                OperationCode: "lifecycle.restart",
                FinishedAtUtc: DateTimeOffset.Parse("2026-05-21T08:00:05Z"),
                FailureCode: "timeout"));
    }

    private sealed class NotificationConsumerWebApplicationFactory : WebApplicationFactory<Program>
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
    }
}
