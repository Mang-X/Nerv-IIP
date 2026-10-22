using Nerv.IIP.Business.Scheduling.Domain.Services;

namespace Nerv.IIP.Business.Scheduling.Domain.Tests;

// DomainInvariant: #4093 独立验收。期望由销售交期减真实剩余路径手算得到。
public sealed class MaterialDeliveryTimeBoundCalculatorTests
{
    private static readonly DateTimeOffset Due = new(2026, 10, 2, 16, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Serial_route_uses_remaining_time_and_excludes_completed_work()
    {
        var result = MaterialDeliveryTimeBoundCalculator.Calculate([
            new MaterialDeliveryTimeSource("sales:SO-01:line-1", Due,
            [
                Operation("cut", 0),
                Operation("mill", 2, "cut"),
                Operation("inspect", 1, "mill")
            ])
        ]);

        var bound = result.TightestBound;
        Assert.Equal(Due.AddHours(-3), bound.LatestStartUtc);
        Assert.Equal(TimeSpan.FromHours(3), bound.RemainingDuration);
        Assert.Equal(["mill", "inspect"], bound.CriticalPath.Select(x => x.OperationId));
        Assert.Equal(bound.RemainingDuration, TimeSpan.FromTicks(bound.CriticalPath.Sum(x => x.RemainingDuration.Ticks)));
        Assert.Equal("sales:SO-01:line-1", bound.SourceReference);
        Assert.Equal(Due, bound.DueUtc);
    }

    [Fact]
    public void Fork_and_join_follow_explicit_predecessors_instead_of_input_order_or_names()
    {
        var result = MaterialDeliveryTimeBoundCalculator.Calculate([
            new MaterialDeliveryTimeSource("sales:SO-02:line-1", Due,
            [
                Operation("01-assembly", 1, "99-frame", "50-motor"),
                Operation("50-motor", 4),
                Operation("99-frame", 2, "10-cut"),
                Operation("10-cut", 1)
            ])
        ]);

        Assert.Equal(TimeSpan.FromHours(5), result.TightestBound.RemainingDuration);
        Assert.Equal(Due.AddHours(-5), result.TightestBound.LatestStartUtc);
        Assert.Equal(["50-motor", "01-assembly"], result.TightestBound.CriticalPath.Select(x => x.OperationId));
    }

    [Fact]
    public void Multiple_sales_sources_keep_each_basis_and_choose_the_tightest_start_not_the_earliest_due()
    {
        var result = MaterialDeliveryTimeBoundCalculator.Calculate([
            new MaterialDeliveryTimeSource("sales:SO-early:line-1", Due, [Operation("pack", 1)]),
            new MaterialDeliveryTimeSource("sales:SO-long:line-2", Due.AddHours(2), [Operation("forge", 5)])
        ]);

        Assert.Equal("sales:SO-long:line-2", result.TightestBound.SourceReference);
        Assert.Equal(Due.AddHours(-3), result.TightestBound.LatestStartUtc);
        Assert.Collection(result.SourceBounds,
            first => Assert.Equal(Due.AddHours(-1), first.LatestStartUtc),
            second => Assert.Equal(Due.AddHours(-3), second.LatestStartUtc));
    }

    [Fact]
    public void Empty_or_completed_routes_return_the_source_due_without_artificial_duration()
    {
        var result = MaterialDeliveryTimeBoundCalculator.Calculate([
            new MaterialDeliveryTimeSource("sales:SO-empty", Due, []),
            new MaterialDeliveryTimeSource("sales:SO-done", Due, [Operation("cut", 0), Operation("pack", 0, "cut")])
        ]);

        Assert.All(result.SourceBounds, bound =>
        {
            Assert.Equal(Due, bound.LatestStartUtc);
            Assert.Equal(TimeSpan.Zero, bound.RemainingDuration);
            Assert.Empty(bound.CriticalPath);
        });
    }

    [Fact]
    public void Cyclic_route_cannot_produce_a_time_bound()
    {
        Assert.Throws<ArgumentException>(() => MaterialDeliveryTimeBoundCalculator.Calculate([
            new MaterialDeliveryTimeSource("sales:SO-cycle", Due, [Operation("cut", 1, "pack"), Operation("pack", 1, "cut")])
        ]));
    }

    [Fact]
    public void Negative_remaining_time_cannot_move_the_start_after_the_due()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MaterialDeliveryTimeBoundCalculator.Calculate([
            new MaterialDeliveryTimeSource("sales:SO-negative", Due, [Operation("pack", -1)])
        ]));
    }

    private static RemainingRoutingOperation Operation(string id, int hours, params string[] predecessors) =>
        new(id, TimeSpan.FromHours(hours), predecessors);
}
