using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class SalesOrderDemandChangedForWorkOrderConsumerTests
{
    [Fact]
    public async Task Marks_only_the_matching_scope_work_order_suggestion_and_demand_without_changing_execution_status()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        var started = WorkOrderFor("org-1", "env-1", "WO-1", "SUG-1", "SO-1", "SO-2");
        started.MarkReleased();
        started.Start(DateTimeOffset.Parse("2026-09-28T00:00:00Z"));
        await SeedAsync(provider,
            started,
            WorkOrderFor("org-1", "env-1", "WO-2", "SUG-2", "SO-1"),
            WorkOrderFor("org-2", "env-1", "WO-1", "SUG-1", "SO-1"),
            WorkOrderFor("org-1", "env-2", "WO-1", "SUG-1", "SO-1"));

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
            var handler = new SalesOrderDemandChangedForWorkOrderIntegrationEventHandler(db, new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(Event("org-1", "env-1", "WO-1", "SUG-OTHER", "SO-2", 4, false), CancellationToken.None);
            await handler.HandleAsync(Event("org-1", "env-1", "WO-1", "SUG-1", "SO-OTHER", 4, false), CancellationToken.None);
            await handler.HandleAsync(Event("org-2", "env-1", "WO-1", "SUG-1", "SO-2", 4, false), CancellationToken.None);
            await handler.HandleAsync(Event("org-1", "env-1", "WO-1", "SUG-1", "SO-2", 4, false), CancellationToken.None);
        }

        using var readScope = provider.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var marker = Assert.Single(await readDb.WorkOrderDemandChanges.ToListAsync());
        Assert.Equal(("org-1", "env-1", "WO-1", "SUG-1", "SO-2", 4, false),
            (marker.OrganizationId, marker.EnvironmentId, marker.WorkOrderId, marker.SuggestionId,
                marker.DemandSourceReference, marker.OrderVersion, marker.Cancelled));
        Assert.Equal(WorkOrder.StartedStatus, (await readDb.WorkOrders.SingleAsync(x =>
            x.OrganizationId == "org-1" && x.EnvironmentId == "env-1" && x.WorkOrderIdValue == "WO-1")).Status);
        Assert.Equal(3, (await readDb.WorkOrders.SingleAsync(x =>
            x.OrganizationId == "org-1" && x.EnvironmentId == "env-1" && x.WorkOrderIdValue == "WO-1")).Version);
        Assert.All((await readDb.WorkOrders.ToListAsync()).Where(x => x.OrganizationId != "org-1" || x.EnvironmentId != "env-1" || x.WorkOrderIdValue != "WO-1"),
            order => Assert.Equal(WorkOrder.CreatedStatus, order.Status));
    }

    [Fact]
    public async Task Event_arriving_before_work_order_creation_can_be_retried_after_the_work_order_is_saved()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        var integrationEvent = Event("org-1", "env-1", "WO-1", "SUG-1", "SO-1", 4, true);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
            var handler = new SalesOrderDemandChangedForWorkOrderIntegrationEventHandler(db, new InMemoryIntegrationEventDeadLetterStore());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.HandleAsync(integrationEvent, CancellationToken.None));
        }

        await SeedAsync(provider, WorkOrderFor("org-1", "env-1", "WO-1", "SUG-1", "SO-1"));
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
            var handler = new SalesOrderDemandChangedForWorkOrderIntegrationEventHandler(db, new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
        }

        using var readScope = provider.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        Assert.True(Assert.Single(await readDb.WorkOrderDemandChanges.ToListAsync()).Cancelled);
    }

    [Fact]
    public async Task Duplicate_and_older_versions_do_not_regress_a_cancelled_marker()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        await SeedAsync(provider, WorkOrderFor("org-1", "env-1", "WO-1", "SUG-1", "SO-1"));

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
            var handler = new SalesOrderDemandChangedForWorkOrderIntegrationEventHandler(db, new InMemoryIntegrationEventDeadLetterStore());
            var changed = Event("org-1", "env-1", "WO-1", "SUG-1", "SO-1", 4, false);
            await handler.HandleAsync(changed, CancellationToken.None);
            await handler.HandleAsync(changed, CancellationToken.None);
            await handler.HandleAsync(Event("org-1", "env-1", "WO-1", "SUG-1", "SO-1", 5, true), CancellationToken.None);
            await handler.HandleAsync(Event("org-1", "env-1", "WO-1", "SUG-1", "SO-1", 3, false), CancellationToken.None);
        }

        using var readScope = provider.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var marker = Assert.Single(await readDb.WorkOrderDemandChanges.ToListAsync());
        Assert.Equal(5, marker.OrderVersion);
        Assert.True(marker.Cancelled);
        Assert.Equal(3, await readDb.ProcessedIntegrationEvents.CountAsync());
    }

    private static async Task SeedAsync(ServiceProvider provider, params WorkOrder[] orders)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        db.WorkOrders.AddRange(orders);
        await db.SaveChangesAsync();
    }

    private static WorkOrder WorkOrderFor(string org, string env, string id, string suggestion, params string[] demands) =>
        WorkOrder.Create(org, env, id, "SKU-1", "PV-1", 1m, 1,
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"), "PCS",
            new SourcePlanReference(DemandPlanningSourceReferences.DemandPlanning,
                DemandPlanningSourceReferences.PlanningSuggestion, suggestion, demands[0], demands));

    private static SalesOrderDemandChangedForWorkOrderIntegrationEvent Event(
        string org, string env, string workOrder, string suggestion, string demand, int version, bool cancelled) =>
        new($"evt-{org}-{env}-{workOrder}-{suggestion}-{demand}-{version}",
            DemandPlanningIntegrationEventTypes.SalesOrderDemandChangedForWorkOrder,
            DemandPlanningIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-09-29T00:00:00Z"),
            DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            "corr-1", "cause-1", org, env, "system:demand-planning",
            $"demand-changed:{org}:{env}:{suggestion}:{demand}:{version}",
            new SalesOrderDemandChangedForWorkOrderPayload(suggestion, workOrder, demand, "sales-order-id-1", version, cancelled));
}
