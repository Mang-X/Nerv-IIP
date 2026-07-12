using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class QualityInspectionTaskNotificationConsumerTests
{
    [Fact]
    public async Task Inspection_task_overdue_creates_quality_task_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Quality:InspectionTaskOverdue:RecipientRefs:0"] = "role:quality-inspector",
        });

        using (var scope = factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<InspectionTaskOverdueIntegrationEventHandlerForNotification>(scope.ServiceProvider);
            await ((IIntegrationEventHandler<InspectionTaskOverdueIntegrationEvent>)handler).HandleAsync(CreateEvent(), CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var intent = await dbContext.NotificationIntents
                .Include(x => x.Messages)
                .Include(x => x.Tasks)
                .SingleAsync();
            Assert.Equal(QualityIntegrationEventTypes.InspectionTaskOverdue, intent.SourceEventType);
            Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
            Assert.Equal(NotificationContractConstants.SeverityWarning, intent.Severity);
            Assert.Equal("role:quality-inspector", Assert.Single(intent.Messages).RecipientRef);
            Assert.Single(intent.Tasks);
            Assert.Equal("inspection-task", intent.ResourceType);
            Assert.Equal("IT-001", intent.ResourceId);
        }
    }

    [Fact]
    public async Task Inspection_task_overdue_with_invalid_payload_is_dead_lettered_without_throwing()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<InspectionTaskOverdueIntegrationEventHandlerForNotification>(scope.ServiceProvider);
            await ((IIntegrationEventHandler<InspectionTaskOverdueIntegrationEvent>)handler).HandleAsync(CreateEvent(skuCode: null!), CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await dbContext.NotificationIntents.ToListAsync());
            var deadLetterStore = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();
            var deadLetter = Assert.Single(await deadLetterStore.ListAsync(
                InspectionTaskOverdueIntegrationEventHandlerForNotification.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None));
            Assert.Equal("invalid-payload", deadLetter.FailureCode);
        }
    }

    [Fact]
    public async Task Overdue_measuring_device_creates_quality_calibration_task_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<MeasuringDeviceCalibrationNotificationConsumer>(scope.ServiceProvider);
            await ((IIntegrationEventHandler<MeasuringDeviceCalibrationDueIntegrationEvent>)handler).HandleAsync(CreateCalibrationEvent(), CancellationToken.None);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var intent = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().NotificationIntents.Include(x => x.Tasks).SingleAsync();
            Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
            Assert.Equal("measuring-device", intent.ResourceType);
            Assert.Equal("MD-001", intent.ResourceId);
            Assert.Single(intent.Tasks);
        }
    }

    private static InspectionTaskOverdueIntegrationEvent CreateEvent(string? skuCode = "SKU-RM-1000")
    {
        return new InspectionTaskOverdueIntegrationEvent(
            "evt-quality-overdue-001",
            QualityIntegrationEventTypes.InspectionTaskOverdue,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-05T09:00:00Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-quality-overdue-001",
            "IT-001",
            "org-001",
            "env-dev",
            "system:quality",
            "quality:inspection-task-overdue:org-001:env-dev:IT-001",
            new InspectionTaskOverduePayload(
                "IT-001",
                "receiving",
                "wms",
                "IN-001",
                "LINE-001",
                skuCode!,
                DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-05T09:00:00Z")));
    }

    private static MeasuringDeviceCalibrationDueIntegrationEvent CreateCalibrationEvent() => new(
        "evt-device-calibration-001", QualityIntegrationEventTypes.MeasuringDeviceCalibrationDue, QualityIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-07-05T09:00:00Z"), QualityIntegrationEventSources.BusinessQuality, "corr-device-001", "MD-001",
        "org-001", "env-dev", "system:quality", "quality:calibration:MD-001:overdue",
        new MeasuringDeviceCalibrationDuePayload("MD-001", "MD-0001", "Micrometer", "overdue", DateTimeOffset.Parse("2026-07-01T00:00:00Z"), DateTimeOffset.Parse("2026-07-05T09:00:00Z")));

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
