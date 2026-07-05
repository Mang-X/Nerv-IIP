using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Contracts.Quality;
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

    private static InspectionTaskOverdueIntegrationEvent CreateEvent()
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
                "SKU-RM-1000",
                DateTimeOffset.Parse("2026-07-05T08:00:00Z"),
                DateTimeOffset.Parse("2026-07-05T09:00:00Z")));
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
