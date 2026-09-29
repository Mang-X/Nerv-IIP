using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulePlanMaterialShortageSummaryTests
{
    [Fact]
    public void Shared_material_shortage_is_counted_once_per_order_and_keeps_affected_operations()
    {
        var risks = new[]
        {
            Risk("WO-1", "OP-10", 4m),
            Risk("WO-1", "OP-20", 4m),
            Risk("WO-2", "OP-10", 6m)
        };

        var summary = Assert.Single(SchedulePlanMaterialShortageSummary.Project(risks));

        Assert.Equal("RM-1", summary.MaterialId);
        Assert.Equal(10m, summary.ShortageQuantity);
        Assert.Equal(3, summary.AffectedOperations.Count);
        Assert.Contains(summary.AffectedOperations, x => x.OrderId == "WO-1" && x.OperationId == "OP-10");
        Assert.Contains(summary.AffectedOperations, x => x.OrderId == "WO-1" && x.OperationId == "OP-20");
        Assert.Contains(summary.AffectedOperations, x => x.OrderId == "WO-2" && x.OperationId == "OP-10");
    }

    [Fact]
    public void No_shortages_produce_no_material_summary()
    {
        Assert.Empty(SchedulePlanMaterialShortageSummary.Project([]));
    }

    private static SchedulePlanMaterialRiskContract Risk(string orderId, string operationId, decimal shortage) =>
        new(orderId, operationId, ["material.shortage"],
            [new SchedulingMaterialShortageContract("RM-1", null, 10m, 10m - shortage, shortage)],
            "物料缺口");
}
