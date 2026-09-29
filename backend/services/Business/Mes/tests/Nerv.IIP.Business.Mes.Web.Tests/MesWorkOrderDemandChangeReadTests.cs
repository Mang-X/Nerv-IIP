using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderDemandChangeAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesWorkOrderDemandChangeReadTests
{
    [Fact]
    public async Task List_projects_changed_and_cancelled_demand_from_the_scoped_work_order()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var due = DateTimeOffset.Parse("2026-10-02T08:00:00Z");
        foreach (var id in new[] { "WO-CHANGED", "WO-CANCELLED", "WO-MIXED", "WO-PLAIN" })
        {
            db.WorkOrders.Add(WorkOrder.Create("org-a", "env-dev", id, "SKU-A", "PV-A", 10m, 10, due));
        }
        db.WorkOrders.Add(WorkOrder.Create("org-b", "env-dev", "WO-OTHER", "SKU-A", "PV-A", 10m, 10, due));
        db.WorkOrders.Add(WorkOrder.Create("org-a", "env-other", "WO-PLAIN", "SKU-A", "PV-A", 10m, 10, due));
        db.WorkOrderDemandChanges.AddRange(
            Marker("org-a", "WO-CHANGED", "demand-1", false),
            Marker("org-a", "WO-CANCELLED", "demand-2", true),
            Marker("org-a", "WO-MIXED", "demand-3", false),
            Marker("org-a", "WO-MIXED", "demand-4", true),
            Marker("org-b", "WO-PLAIN", "demand-5", true),
            Marker("org-a", "WO-PLAIN", "demand-6", true, "env-other"));
        await db.SaveChangesAsync();

        var result = await new ListMesWorkOrdersQueryHandler(db).Handle(
            new ListMesWorkOrdersQuery("org-a", "env-dev", null), CancellationToken.None);
        var rows = result.Items.ToDictionary(x => x.WorkOrderId);

        Assert.True(rows["WO-CHANGED"].HasChangedDemand);
        Assert.False(rows["WO-CHANGED"].HasCancelledDemand);
        Assert.False(rows["WO-CANCELLED"].HasChangedDemand);
        Assert.True(rows["WO-CANCELLED"].HasCancelledDemand);
        Assert.True(rows["WO-MIXED"].HasChangedDemand);
        Assert.True(rows["WO-MIXED"].HasCancelledDemand);
        Assert.False(rows["WO-PLAIN"].HasChangedDemand);
        Assert.False(rows["WO-PLAIN"].HasCancelledDemand);
        Assert.Equal("created", rows["WO-MIXED"].Status);
        Assert.DoesNotContain("WO-OTHER", rows.Keys);
    }

    private static WorkOrderDemandChange Marker(
        string organizationId, string workOrderId, string reference, bool cancelled, string environmentId = "env-dev") =>
        new(organizationId, environmentId, workOrderId, "suggestion-1", reference, "sales-order-1", 2, cancelled);
}
