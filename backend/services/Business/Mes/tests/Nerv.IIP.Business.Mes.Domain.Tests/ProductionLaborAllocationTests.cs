using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;

namespace Nerv.IIP.Business.Mes.Domain.Tests;

public sealed class ProductionLaborAllocationTests
{
    [Fact]
    public void Allocate_splits_final_labor_ticks_by_registered_share_without_losing_ticks()
    {
        var participants = new[]
        {
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-a", "Alice", 60m),
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-b", "Bob", 40m),
        };

        var allocations = ProductionReportLaborAllocation.Allocate(
            "org-001",
            "env-dev",
            "PR-001",
            "WO-001",
            "OP-10",
            1001L,
            participants);

        Assert.Collection(
            allocations,
            first =>
            {
                Assert.Equal("worker-a", first.WorkerId);
                Assert.Equal("Alice", first.WorkerName);
                Assert.Equal(60m, first.SharePercent);
                Assert.Equal(601L, first.AllocatedLaborTicks);
            },
            second =>
            {
                Assert.Equal("worker-b", second.WorkerId);
                Assert.Equal("Bob", second.WorkerName);
                Assert.Equal(40m, second.SharePercent);
                Assert.Equal(400L, second.AllocatedLaborTicks);
            });
        Assert.Equal(1001L, allocations.Sum(x => x.AllocatedLaborTicks));
    }

    [Fact]
    public void Allocate_rejects_duplicate_workers_and_non_balanced_shares()
    {
        var duplicateWorkers = new[]
        {
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-a", "Alice", 50m),
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "WORKER-A", "Alice", 50m),
        };
        var unbalancedShares = new[]
        {
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-a", "Alice", 60m),
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-b", "Bob", 30m),
        };

        Assert.Throws<ArgumentException>(() => ProductionReportLaborAllocation.Allocate(
            "org-001", "env-dev", "PR-001", "WO-001", "OP-10", 1000L, duplicateWorkers));
        Assert.Throws<ArgumentException>(() => ProductionReportLaborAllocation.Allocate(
            "org-001", "env-dev", "PR-001", "WO-001", "OP-10", 1000L, unbalancedShares));
    }

    [Fact]
    public void Register_rejects_share_precision_that_cannot_be_persisted_losslessly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OperationTaskParticipant.Register(
            "org-001", "env-dev", "OP-10", "worker-a", "Alice", 50.00001m));
    }

    [Fact]
    public void Allocate_small_tick_totals_never_produce_a_negative_remainder()
    {
        var participants = new[]
        {
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-a", "Alice", 25m),
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-b", "Bob", 25m),
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-c", "Carol", 25m),
            OperationTaskParticipant.Register("org-001", "env-dev", "OP-10", "worker-d", "David", 25m),
        };

        var allocations = ProductionReportLaborAllocation.Allocate(
            "org-001", "env-dev", "PR-001", "WO-001", "OP-10", 2L, participants);

        Assert.Equal([1L, 1L, 0L, 0L], allocations.Select(x => x.AllocatedLaborTicks));
        Assert.All(allocations, allocation => Assert.True(allocation.AllocatedLaborTicks >= 0));
        Assert.Equal(2L, allocations.Sum(x => x.AllocatedLaborTicks));
    }
}
