using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class IndustrialTelemetryAlarmNotificationConsumerTests
{
    [Fact]
    public async Task Handle_alarm_raised_creates_configured_recipient_notification_with_alarm_context()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["IndustrialTelemetry:AlarmNotification:RecipientRefs:0"] = "role:maintenance-dispatcher",
        });

        await HandleRaisedAsync(factory, CreateAlarmRaisedEvent(
            "event-alarm-raised",
            "industrial-alarm-raised:external-alarm-001"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal(IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry, intent.SourceService);
        Assert.Equal(IndustrialTelemetryIntegrationEventTypes.AlarmRaised, intent.SourceEventType);
        Assert.Equal("event-alarm-raised", intent.SourceEventId);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityCritical, intent.Severity);
        Assert.Equal("industrial-telemetry-alarm", intent.ResourceType);
        Assert.Equal("external-alarm-001", intent.ResourceId);
        Assert.Equal("role:maintenance-dispatcher", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
        Assert.Contains("device asset-001", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("tag temp.bearing", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("observed 97.5 C", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("threshold 80 C", intent.Summary, StringComparison.Ordinal);
        Assert.Equal(AlarmRaisedIntegrationEventHandlerForNotification.ConsumerName, processed.ConsumerName);
        Assert.Equal("industrial-alarm-raised:external-alarm-001", processed.IdempotencyKey);
    }

    [Fact]
    public async Task Handle_alarm_cleared_creates_recovery_notification_for_same_alarm_resource()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        await HandleClearedAsync(factory, CreateAlarmClearedEvent(
            "event-alarm-cleared",
            "industrial-alarm-cleared:external-alarm-001"));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal(IndustrialTelemetryIntegrationEventTypes.AlarmCleared, intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Message, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityInfo, intent.Severity);
        Assert.Equal("industrial-telemetry-alarm", intent.ResourceType);
        Assert.Equal("external-alarm-001", intent.ResourceId);
        Assert.Equal("role:maintenance", Assert.Single(intent.Messages).RecipientRef);
        Assert.Empty(intent.Tasks);
        Assert.Contains("Alarm HI_TEMP on device asset-001 cleared", intent.Summary, StringComparison.Ordinal);
        Assert.Equal(AlarmClearedIntegrationEventHandlerForNotification.ConsumerName, processed.ConsumerName);
    }

    [Fact]
    public async Task Handle_same_alarm_raised_event_twice_does_not_create_duplicate_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        var integrationEvent = CreateAlarmRaisedEvent(
            "event-alarm-raised-duplicate",
            "industrial-alarm-raised:external-alarm-duplicate");

        await HandleRaisedAsync(factory, integrationEvent);
        await HandleRaisedAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await dbContext.NotificationIntents.CountAsync());
        Assert.Equal(1, await dbContext.NotificationMessages.CountAsync());
        Assert.Equal(1, await dbContext.NotificationTasks.CountAsync());
        Assert.Equal(1, await dbContext.ProcessedIntegrationEvents.CountAsync());
    }

    private static async Task HandleRaisedAsync(
        NotificationConsumerWebApplicationFactory factory,
        AlarmRaisedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<AlarmRaisedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<AlarmRaisedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static async Task HandleClearedAsync(
        NotificationConsumerWebApplicationFactory factory,
        AlarmClearedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<AlarmClearedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<AlarmClearedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static AlarmRaisedIntegrationEvent CreateAlarmRaisedEvent(string eventId, string idempotencyKey)
    {
        return new AlarmRaisedIntegrationEvent(
            EventId: eventId,
            EventType: IndustrialTelemetryIntegrationEventTypes.AlarmRaised,
            EventVersion: IndustrialTelemetryIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-03T08:00:00Z"),
            SourceService: IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            CorrelationId: $"corr-{eventId}",
            CausationId: "sample-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:industrial-telemetry",
            IdempotencyKey: idempotencyKey,
            Payload: new AlarmRaisedPayload(
                AlarmEventId: "alarm-event-001",
                DeviceAssetId: "asset-001",
                AlarmCode: "HI_TEMP",
                Severity: "critical",
                RaisedAtUtc: DateTimeOffset.Parse("2026-07-03T08:00:00Z"),
                ExternalAlarmId: "external-alarm-001",
                Priority: null,
                TagKey: "temp.bearing",
                ObservedValue: 97.5m,
                ThresholdValue: 80m,
                UnitCode: "C"));
    }

    private static AlarmClearedIntegrationEvent CreateAlarmClearedEvent(string eventId, string idempotencyKey)
    {
        return new AlarmClearedIntegrationEvent(
            EventId: eventId,
            EventType: IndustrialTelemetryIntegrationEventTypes.AlarmCleared,
            EventVersion: IndustrialTelemetryIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-03T08:05:00Z"),
            SourceService: IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
            CorrelationId: $"corr-{eventId}",
            CausationId: "alarm-event-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-001",
            Actor: "system:industrial-telemetry",
            IdempotencyKey: idempotencyKey,
            Payload: new AlarmClearedPayload(
                AlarmEventId: "alarm-event-001",
                DeviceAssetId: "asset-001",
                AlarmCode: "HI_TEMP",
                Severity: "critical",
                RaisedAtUtc: DateTimeOffset.Parse("2026-07-03T08:00:00Z"),
                ClearedAtUtc: DateTimeOffset.Parse("2026-07-03T08:05:00Z"),
                ExternalAlarmId: "external-alarm-001"));
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
