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

    [Fact]
    public void Same_material_in_different_lots_or_units_has_separate_shortage_totals()
    {
        var risks = new[]
        {
            new SchedulePlanMaterialRiskContract("WO-1", "OP-10", ["material.shortage"],
                [
                    new SchedulingMaterialShortageContract("RM-1", "L1", 10m, 6m, 4m, "KG"),
                    new SchedulingMaterialShortageContract("RM-1", "L2", 10m, 7m, 3m, "KG"),
                    new SchedulingMaterialShortageContract("RM-1", "L1", 10m, 8m, 2m, "PCS")
                ], "物料缺口"),
            new SchedulePlanMaterialRiskContract("WO-2", "OP-20", ["material.shortage"],
                [new SchedulingMaterialShortageContract("RM-1", "L1", 10m, 5m, 5m, "KG")], "物料缺口")
        };

        var summary = SchedulePlanMaterialShortageSummary.Project(risks);

        Assert.Equal(3, summary.Count);
        Assert.Equal(9m, summary.Single(x => x.MaterialLotId == "L1" && x.UomCode == "KG").ShortageQuantity);
        Assert.Equal(3m, summary.Single(x => x.MaterialLotId == "L2" && x.UomCode == "KG").ShortageQuantity);
        Assert.Equal(2m, summary.Single(x => x.MaterialLotId == "L1" && x.UomCode == "PCS").ShortageQuantity);
    }

    private static SchedulePlanMaterialRiskContract Risk(string orderId, string operationId, decimal shortage) =>
        new(orderId, operationId, ["material.shortage"],
            [new SchedulingMaterialShortageContract("RM-1", null, 10m, 10m - shortage, shortage)],
            "物料缺口");
}
