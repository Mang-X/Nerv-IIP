using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class AppHubConnectorHostNotificationConsumerTests
{
    [Fact]
    public async Task Handle_connector_host_unreachable_creates_critical_task_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["AppHub:ConnectorHostNotification:RecipientRefs:0"] = "role:ops-dispatcher",
        });

        await HandleUnreachableAsync(factory, CreateUnreachableEvent("event-apphub-offline", "apphub:connector-host-unreachable:org-001:env-001:connector-host-001:demo-api-001:2026-07-06T01:06:00Z"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal(AppHubIntegrationEventSources.AppHub, intent.SourceService);
        Assert.Equal(AppHubIntegrationEventTypes.ConnectorHostUnreachable, intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityCritical, intent.Severity);
        Assert.Equal("connector-host", intent.ResourceType);
        Assert.Equal("connector-host-001", intent.ResourceId);
        Assert.Equal("role:ops-dispatcher", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
        Assert.Contains("demo-api-001", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("last heartbeat 2026-07-06T01:00:00.0000000+00:00", intent.Summary, StringComparison.Ordinal);
        Assert.Equal(ConnectorHostUnreachableIntegrationEventHandlerForNotification.ConsumerName, processed.ConsumerName);
    }

    [Fact]
    public async Task Handle_connector_host_restored_creates_recovery_message_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleRestoredAsync(factory, CreateRestoredEvent("event-apphub-restored", "apphub:connector-host-restored:org-001:env-001:connector-host-001:demo-api-001:2026-07-06T01:08:00Z"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal(AppHubIntegrationEventTypes.ConnectorHostRestored, intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Message, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityInfo, intent.Severity);
        Assert.Equal("connector-host", intent.ResourceType);
        Assert.Equal("connector-host-001", intent.ResourceId);
        Assert.Equal("role:ops-admin", Assert.Single(intent.Messages).RecipientRef);
        Assert.Empty(intent.Tasks);
        Assert.Contains("restored", intent.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectorHostRestoredIntegrationEventHandlerForNotification.ConsumerName, processed.ConsumerName);
    }

    private static async Task HandleUnreachableAsync(
        NotificationConsumerWebApplicationFactory factory,
        ConnectorHostUnreachableIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<ConnectorHostUnreachableIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<ConnectorHostUnreachableIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleRestoredAsync(
        NotificationConsumerWebApplicationFactory factory,
        ConnectorHostRestoredIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<ConnectorHostRestoredIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<ConnectorHostRestoredIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static ConnectorHostUnreachableIntegrationEvent CreateUnreachableEvent(string eventId, string idempotencyKey)
    {
        return new ConnectorHostUnreachableIntegrationEvent(
            EventId: eventId,
            EventType: AppHubIntegrationEventTypes.ConnectorHostUnreachable,
            EventVersion: AppHubIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-06T01:06:00Z"),
            SourceService: AppHubIntegrationEventSources.AppHub,
            CorrelationId: $"corr-{eventId}",
            CausationId: "demo-api-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "apphub",
            IdempotencyKey: idempotencyKey,
            Payload: new ConnectorHostUnreachablePayload(
                ConnectorHostId: "connector-host-001",
                InstanceKey: "demo-api-001",
                LastHeartbeatAtUtc: DateTimeOffset.Parse("2026-07-06T01:00:00Z"),
                DetectedAtUtc: DateTimeOffset.Parse("2026-07-06T01:06:00Z"),
                HeartbeatTimeoutSeconds: 300));
    }

    private static ConnectorHostRestoredIntegrationEvent CreateRestoredEvent(string eventId, string idempotencyKey)
    {
        return new ConnectorHostRestoredIntegrationEvent(
            EventId: eventId,
            EventType: AppHubIntegrationEventTypes.ConnectorHostRestored,
            EventVersion: AppHubIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-06T01:08:00Z"),
            SourceService: AppHubIntegrationEventSources.AppHub,
            CorrelationId: $"corr-{eventId}",
            CausationId: "demo-api-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "apphub",
            IdempotencyKey: idempotencyKey,
            Payload: new ConnectorHostRestoredPayload(
                ConnectorHostId: "connector-host-001",
                InstanceKey: "demo-api-001",
                RestoredAtUtc: DateTimeOffset.Parse("2026-07-06T01:08:00Z")));
    }

    private sealed class NotificationConsumerWebApplicationFactory(IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var mergedSettings = new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                };
                if (settings is not null)
                {
                    foreach (var (key, value) in settings)
                    {
                        mergedSettings[key] = value;
                    }
                }

                configuration.AddInMemoryCollection(mergedSettings);
            });
        }
    }
}
