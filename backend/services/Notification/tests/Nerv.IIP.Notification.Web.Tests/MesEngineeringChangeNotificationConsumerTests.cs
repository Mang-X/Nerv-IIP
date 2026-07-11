using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Notification;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Domain.AggregatesModel.NotificationIntentAggregate;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Notification.Web.Tests;

public sealed class MesEngineeringChangeNotificationConsumerTests
{
    [Fact]
    public async Task Work_order_engineering_change_impact_creates_planner_task_notification()
    {
        using var factory = new NotificationConsumerWebApplicationFactory(new Dictionary<string, string?>
        {
            ["Mes:EngineeringChangeImpact:RecipientRefs:0"] = "role:process-engineer",
            ["Mes:EngineeringChangeImpact:RecipientRefs:1"] = "role:production-planner",
        });

        using (var scope = factory.Services.CreateScope())
        {
            var handler = ActivatorUtilities.CreateInstance<WorkOrderEngineeringChangeImpactDetectedIntegrationEventHandlerForNotification>(scope.ServiceProvider);
            await ((IIntegrationEventHandler<WorkOrderEngineeringChangeImpactDetectedIntegrationEvent>)handler).HandleAsync(CreateEvent(), CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var intent = await dbContext.NotificationIntents
                .Include(x => x.Messages)
                .Include(x => x.Tasks)
                .SingleAsync();
            Assert.Equal(MesIntegrationEventTypes.WorkOrderEngineeringChangeImpactDetected, intent.SourceEventType);
            Assert.Equal(NotificationIntentTypes.Task, intent.IntentType);
            Assert.Equal(NotificationContractConstants.SeverityWarning, intent.Severity);
            Assert.Equal("mes-work-order", intent.ResourceType);
            Assert.Equal("WO-STARTED", intent.ResourceId);
            Assert.Equal(["role:process-engineer", "role:production-planner"], intent.Messages.Select(x => x.RecipientRef).Order(StringComparer.Ordinal));
            Assert.Equal(2, intent.Tasks.Count);
            Assert.Contains("ECO-721", intent.Summary, StringComparison.Ordinal);
        }
    }

    private static WorkOrderEngineeringChangeImpactDetectedIntegrationEvent CreateEvent()
    {
        return new WorkOrderEngineeringChangeImpactDetectedIntegrationEvent(
            "evt-mes-eco-impact-001",
            MesIntegrationEventTypes.WorkOrderEngineeringChangeImpactDetected,
            MesIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-06T08:05:00Z"),
            MesIntegrationEventSources.BusinessMes,
            "corr-eco-721",
            "WO-STARTED",
            "org-001",
            "env-dev",
            "system:mes",
            "mes:engineering-change-impact:org-001:env-dev:ECO-721:WO-STARTED",
            new WorkOrderEngineeringChangeImpactDetectedPayload(
                "WO-STARTED",
                "SKU-FG-1000",
                "ECO-721",
                "PV-OLD",
                "PV-NEW",
                MesEngineeringChangeImpactContractStatuses.PendingDecision,
                new DateOnly(2026, 7, 6)));
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
