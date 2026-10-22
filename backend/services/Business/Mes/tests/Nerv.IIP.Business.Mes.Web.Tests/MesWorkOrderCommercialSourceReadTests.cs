using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesWorkOrderCommercialSourceReadTests
{
    [Fact]
    public async Task List_and_detail_preserve_all_source_demands_and_authoritative_completed_quantity()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();
        var at = DateTimeOffset.Parse("2026-10-02T08:00:00Z");
        var order = WorkOrder.Create("org-a", "env-dev", "WO-A", "SKU-A", null, 10m, 10, at,
            sourcePlanReference: new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-A", "SO-A", ["SO-A", "SO-B"]));
        order.RecordProductionProgress(3.25m, 1m, at);
        db.WorkOrders.Add(order);
        db.WorkOrders.Add(WorkOrder.Create("org-b", "env-dev", "WO-A", "SKU-OTHER", null, 20m, 10, at));
        db.WorkOrders.Add(WorkOrder.Create("org-a", "env-other", "WO-A", "SKU-OTHER", null, 20m, 10, at));
        db.WorkOrders.Add(WorkOrder.Create("org-a", "env-dev", "WO-PLAIN", "SKU-A", null, 10m, 10, at));
        await db.SaveChangesAsync();
        var list = await new ListMesWorkOrdersQueryHandler(db).Handle(new("org-a", "env-dev", null), CancellationToken.None);
        var detail = await new GetMesWorkOrderDetailQueryHandler(db).Handle(new("org-a", "env-dev", "WO-A"), CancellationToken.None);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var row = JsonSerializer.SerializeToElement(list.Items.Single(x => x.WorkOrderId == "WO-A"), options);
        var body = JsonSerializer.SerializeToElement(detail, options);
        Assert.Equal(3.25m, body.GetProperty("completedQuantity").GetDecimal());
        Assert.Equal(3.25m, row.GetProperty("completedQuantity").GetDecimal());
        Assert.Equal("SKU-A", detail.SkuId);
        Assert.Equal(2, list.Total);
        foreach (var json in new[] { row, body })
            Assert.Equal(new[] { "SO-A", "SO-B" }, json.GetProperty("sourcePlanReference").GetProperty("sourceDemandReferences").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(list.Items.Single(x => x.WorkOrderId == "WO-PLAIN"), options).GetProperty("sourcePlanReference").ValueKind);
    }
}
