using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Notification.Infrastructure;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Notification.Web.Application.Queries.Notifications;
using NetCorePal.Extensions.DistributedTransactions;

namespace Nerv.IIP.Notification.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class DemandChangePlannerNotificationConsumerTests
{
    [Theory]
    [InlineData(false, "changed")]
    [InlineData(true, "cancelled")]
    public async Task Published_demand_change_reaches_each_planner_personal_inbox_once(bool cancelled, string expectedWord)
    {
        using var factory = new NotificationConsumerFactory(["planner-a", "planner-b"]);
        var integrationEvent = CreateEvent(cancelled);

        await HandleAsync(factory, integrationEvent);
        await HandleAsync(factory, integrationEvent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.NotificationIntents.ToListAsync());
        Assert.Single(await db.ProcessedIntegrationEvents.ToListAsync());
        Assert.Equal(2, await db.NotificationMessages.CountAsync());
        Assert.Equal(2, await db.NotificationTasks.CountAsync());

        var messages = new ListNotificationMessagesQueryHandler(db);
        var tasks = new ListNotificationTasksQueryHandler(db);
        foreach (var user in new[] { "planner-a", "planner-b" })
        {
            var recipient = $"user:{user}";
            var message = Assert.Single((await messages.Handle(
                new ListNotificationMessagesQuery("org-001", "env-001", recipient, null), CancellationToken.None)).Items);
            Assert.Contains(expectedWord, message.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Single((await tasks.Handle(
                new ListNotificationTasksQuery("org-001", "env-001", recipient, null), CancellationToken.None)).Items);
            Assert.Empty((await messages.Handle(
                new ListNotificationMessagesQuery("org-other", "env-001", recipient, null), CancellationToken.None)).Items);
            Assert.Empty((await messages.Handle(
                new ListNotificationMessagesQuery("org-001", "env-other", recipient, null), CancellationToken.None)).Items);
        }

        Assert.Empty((await messages.Handle(
            new ListNotificationMessagesQuery("org-001", "env-001", "user:non-member", null), CancellationToken.None)).Items);
    }

    [Fact]
    public async Task Changed_and_cancelled_events_for_one_work_order_remain_distinct_in_each_planner_inbox()
    {
        using var factory = new NotificationConsumerFactory(["planner-a", "planner-b"]);
        await HandleAsync(factory, CreateEvent(false));
        await HandleAsync(factory, CreateEvent(true));

        using var scope = factory.Services.CreateScope();
        var messages = new ListNotificationMessagesQueryHandler(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        foreach (var user in new[] { "planner-a", "planner-b" })
        {
            var personalMessages = (await messages.Handle(
                new ListNotificationMessagesQuery("org-001", "env-001", $"user:{user}", null), CancellationToken.None)).Items;
            Assert.Equal(2, personalMessages.Count);
            Assert.Contains(personalMessages, message => message.Summary.Contains("changed", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(personalMessages, message => message.Summary.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task No_planner_members_means_no_personal_notification()
    {
        using var factory = new NotificationConsumerFactory([]);
        await HandleAsync(factory, CreateEvent(false));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.NotificationIntents.ToListAsync());
        Assert.Empty(await db.NotificationMessages.ToListAsync());
    }

    private static async Task HandleAsync(NotificationConsumerFactory factory, SalesOrderDemandChangedForWorkOrderIntegrationEvent integrationEvent)
    {
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SalesOrderDemandChangedForWorkOrderIntegrationEventHandlerForNotification>();
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static SalesOrderDemandChangedForWorkOrderIntegrationEvent CreateEvent(bool cancelled) => new(
        EventId: cancelled ? "event-cancelled" : "event-changed",
        EventType: DemandPlanningIntegrationEventTypes.SalesOrderDemandChangedForWorkOrder,
        EventVersion: DemandPlanningIntegrationEventVersions.V1,
        OccurredAtUtc: DateTimeOffset.Parse("2026-09-29T08:00:00Z"),
        SourceService: DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
        CorrelationId: "demand-change-001",
        CausationId: "sales-order-001",
        OrganizationId: "org-001",
        EnvironmentId: "env-001",
        Actor: "system:business-demand-planning",
        IdempotencyKey: cancelled ? "demand-cancelled-001" : "demand-changed-001",
        Payload: new SalesOrderDemandChangedForWorkOrderPayload(
            "suggestion-001", "WO-001", "sales-order:SO-001", "SO-001", 2, cancelled));

    private sealed class NotificationConsumerFactory(IReadOnlyList<string> plannerIds) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:Provider"] = "InMemory",
                    ["Persistence:InMemoryDatabaseName"] = Guid.NewGuid().ToString("N"),
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProductionPlannerMemberDirectory>();
                services.AddSingleton<IProductionPlannerMemberDirectory>(new ScopedPlannerDirectory(plannerIds));
            });
        }
    }

    private sealed class ScopedPlannerDirectory(IReadOnlyList<string> plannerIds) : IProductionPlannerMemberDirectory
    {
        public Task<IReadOnlyList<string>> ListMemberIdsAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(
                organizationId == "org-001" && environmentId == "env-001" ? plannerIds : []);
    }
}
