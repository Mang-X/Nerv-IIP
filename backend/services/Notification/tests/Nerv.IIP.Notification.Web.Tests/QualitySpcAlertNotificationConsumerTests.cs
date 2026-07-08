using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class QualitySpcAlertNotificationConsumerTests
{
    [Fact]
    public async Task Handle_quality_spc_alert_creates_quality_notification_resource_not_equipment_alarm()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Quality:SpcAlert:RecipientRefs:0"] = "role:quality-engineer",
        });
        var integrationEvent = CreateSpcAlertEvent(
            "event-spc-alert",
            "quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01:trend-increasing");

        await HandleAsync(factory, integrationEvent);
        await HandleAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var intent = await dbContext.NotificationIntents
            .Include(x => x.Messages)
            .Include(x => x.Tasks)
            .SingleAsync();
        var processed = await dbContext.ProcessedIntegrationEvents.SingleAsync();

        Assert.Equal(QualityIntegrationEventSources.BusinessQuality, intent.SourceService);
        Assert.Equal(QualityIntegrationEventTypes.SpcAlertRaised, intent.SourceEventType);
        Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
        Assert.Equal(NotificationContractConstants.SeverityWarning, intent.Severity);
        Assert.Equal("quality-spc-alert", intent.ResourceType);
        Assert.Equal("quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01", intent.ResourceId);
        Assert.NotEqual("industrial-telemetry-alarm", intent.ResourceType);
        Assert.Equal("role:quality-engineer", Assert.Single(intent.Messages).RecipientRef);
        Assert.Single(intent.Tasks);
        Assert.Contains("SKU-RM-1000", intent.Summary, StringComparison.Ordinal);
        Assert.Contains("trend-increasing", intent.Summary, StringComparison.Ordinal);
        Assert.Equal(SpcAlertRaisedIntegrationEventHandlerForNotification.ConsumerName, processed.ConsumerName);
        Assert.Equal(1, await dbContext.NotificationIntents.CountAsync());
    }

    private static async Task HandleAsync(
        NotificationConsumerWebApplicationFactory factory,
        SpcAlertRaisedIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        IIntegrationEventHandler<SpcAlertRaisedIntegrationEvent> handler =
            ActivatorUtilities.CreateInstance<SpcAlertRaisedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static SpcAlertRaisedIntegrationEvent CreateSpcAlertEvent(string eventId, string idempotencyKey)
    {
        return new SpcAlertRaisedIntegrationEvent(
            EventId: eventId,
            EventType: QualityIntegrationEventTypes.SpcAlertRaised,
            EventVersion: QualityIntegrationEventVersions.V1,
            OccurredAtUtc: DateTimeOffset.Parse("2026-07-07T08:00:00Z"),
            SourceService: QualityIntegrationEventSources.BusinessQuality,
            CorrelationId: $"corr-{eventId}",
            CausationId: "spc-evaluate-001",
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            Actor: "system:business-quality",
            IdempotencyKey: idempotencyKey,
            Payload: new SpcAlertRaisedPayload(
                AlertKey: "quality-spc-alert:org-001:env-dev:SKU-RM-1000:length:WC-MIX-01",
                ResourceType: "quality-spc-alert",
                SkuCode: "SKU-RM-1000",
                CharacteristicCode: "length",
                WorkCenterId: "WC-MIX-01",
                RuleCodes: [QualitySpcRuleCodes.TrendIncreasing],
                Severity: NotificationContractConstants.SeverityWarning,
                LatestMeasuredAtUtc: DateTimeOffset.Parse("2026-07-07T07:59:00Z"),
                Summary: "SPC trend detected for SKU-RM-1000 length at WC-MIX-01."));
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
