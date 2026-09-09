using Nerv.IIP.Business.Wms.Domain.AggregatesModel.BackorderOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain;

namespace Nerv.IIP.Business.Wms.Domain.Tests;

public sealed class BackorderOrderAggregateTests
{
    [Fact]
    public void Short_pick_creates_trackable_backorder_and_replenishment_recommendation()
    {
        var backorder = BackorderOrder.Create(
            "org-001", "env-dev", "BO-OUT-001-LINE-001", "OUT-001", "LINE-001",
            "SKU-001", "pcs", "SITE-01", "PICK-01", 3m);

        var task = backorder.CreateReplenishmentRecommendation();

        Assert.Equal(BackorderOrderStatus.Open, backorder.Status);
        Assert.Equal(3m, backorder.BackorderQuantity);
        Assert.Equal(WarehouseTaskType.Replenishment, task.TaskType);
        Assert.Equal(backorder.BackorderOrderNo, task.SourceOrderNo);
        // 任务号由聚合按 RPL 种类自算，调用方拿不到「传错种类」的机会（#3228）。
        Assert.Equal(BackorderOrder.ComposeReplenishmentTaskNo("OUT-001", "LINE-001"), task.TaskNo);
        Assert.StartsWith("RPL-", task.TaskNo, StringComparison.Ordinal);
        Assert.Equal("PICK-01", task.ToLocationCode);
        Assert.Equal(3m, task.PlannedQuantity);
    }

    [Fact]
    public void Backorder_can_be_closed_once_and_retains_closure_audit()
    {
        var backorder = BackorderOrder.Create(
            "org-001", "env-dev", "BO-OUT-001-LINE-001", "OUT-001", "LINE-001",
            "SKU-001", "pcs", "SITE-01", "PICK-01", 3m);

        backorder.Close("stock-restored");
        backorder.Close("stock-restored");

        Assert.Equal(BackorderOrderStatus.Closed, backorder.Status);
        Assert.Equal("stock-restored", backorder.ClosureReason);
        Assert.NotNull(backorder.ClosedAtUtc);
    }

    [Fact]
    public void Stable_operational_code_is_deterministic_and_bounded()
    {
        var first = BackorderOrder.ComposeBackorderOrderNo(new string('O', 100), new string('L', 100));
        var replay = BackorderOrder.ComposeBackorderOrderNo(new string('O', 100), new string('L', 100));

        Assert.Equal(first, replay);
        Assert.StartsWith("BO-", first, StringComparison.Ordinal);
        Assert.True(first.Length <= 100);
    }
}
