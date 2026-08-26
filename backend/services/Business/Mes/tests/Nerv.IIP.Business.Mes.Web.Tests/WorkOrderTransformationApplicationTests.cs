using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class WorkOrderTransformationApplicationTests
{
    [Fact]
    public async Task Split_persists_lineage_and_replays_the_same_idempotency_key()
    {
        await using var db = CreateContext();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-26T02:00:00Z");
        db.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-SPLIT-PARENT", "SKU-001", "PV-001", 10m, 10,
            occurredAtUtc.AddHours(4), "PCS"));
        await db.SaveChangesAsync();

        var command = new SplitWorkOrderCommand(
            "org-001",
            "env-dev",
            "WO-SPLIT-PARENT",
            [
                new("WO-SPLIT-CHILD-1", 4m),
                new("WO-SPLIT-CHILD-2", 6m),
            ],
            "按客户批次拆分",
            "split-application-001",
            "user:planner-001",
            occurredAtUtc);
        var handler = new SplitWorkOrderCommandHandler(db);

        var first = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var replay = await handler.Handle(command, CancellationToken.None);
        var parent = await db.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-SPLIT-PARENT");
        var children = await db.WorkOrders
            .Where(x => x.WorkOrderIdValue.StartsWith("WO-SPLIT-CHILD-"))
            .OrderBy(x => x.WorkOrderIdValue)
            .ToArrayAsync();
        var readback = await new GetWorkOrderTransformationQueryHandler(db).Handle(
            new("org-001", "env-dev", first.TransformationId),
            CancellationToken.None);

        Assert.False(first.IsIdempotentReplay);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal(first.TransformationId, replay.TransformationId);
        Assert.Equal(["WO-SPLIT-CHILD-1", "WO-SPLIT-CHILD-2"], first.TargetWorkOrderIds);
        Assert.Equal(WorkOrder.SplitStatus, parent.Status);
        Assert.Equal(2, parent.Version);
        Assert.Equal([4m, 6m], children.Select(x => x.Quantity));
        Assert.Equal(2, readback.Lines.Count);
        Assert.Equal(10m, readback.Lines.Sum(x => x.Quantity));
        Assert.All(readback.Lines, line => Assert.Equal("WO-SPLIT-PARENT", line.SourceWorkOrderId));
    }

    [Fact]
    public async Task Merge_rejects_a_different_payload_reusing_the_same_idempotency_key()
    {
        await using var db = CreateContext();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-26T03:00:00Z");
        db.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-MERGE-SOURCE-1", "SKU-001", "PV-001", 3m, 10, occurredAtUtc.AddHours(4), "PCS"),
            WorkOrder.Create("org-001", "env-dev", "WO-MERGE-SOURCE-2", "SKU-001", "PV-001", 7m, 10, occurredAtUtc.AddHours(4), "PCS"));
        await db.SaveChangesAsync();

        var handler = new MergeWorkOrdersCommandHandler(db);
        var first = new MergeWorkOrdersCommand(
            "org-001",
            "env-dev",
            ["WO-MERGE-SOURCE-1", "WO-MERGE-SOURCE-2"],
            "WO-MERGE-TARGET",
            "合并同 SKU 小单",
            "merge-application-001",
            "user:planner-001",
            occurredAtUtc);
        await handler.Handle(first, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var conflicting = first with { TargetWorkOrderId = "WO-MERGE-TARGET-OTHER" };
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() =>
            handler.Handle(conflicting, CancellationToken.None));

        Assert.Equal("idempotency-conflict", MesIdempotencyConflictException.SafeCode);
        Assert.Equal(1, await db.WorkOrderTransformations.CountAsync());
        Assert.Equal(3, await db.WorkOrders.CountAsync());
    }

    [Fact]
    public async Task Split_maps_non_transformable_source_to_a_lifecycle_conflict()
    {
        await using var db = CreateContext();
        var source = WorkOrder.Create(
            "org-001", "env-dev", "WO-SPLIT-STARTED", "SKU-001", "PV-001", 10m, 10,
            DateTimeOffset.Parse("2026-08-26T04:00:00Z"), "PCS");
        source.MarkReleased();
        source.MarkSplit();
        db.WorkOrders.Add(source);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new SplitWorkOrderCommandHandler(db).Handle(
                new SplitWorkOrderCommand(
                    "org-001", "env-dev", "WO-SPLIT-STARTED",
                    [new("WO-SPLIT-CHILD-1", 4m), new("WO-SPLIT-CHILD-2", 6m)],
                    "重复拆分", "split-application-conflict", "user:planner-001",
                    DateTimeOffset.Parse("2026-08-26T04:00:00Z")),
                CancellationToken.None));

        Assert.Equal("work-order-transformation", exception.Action);
        Assert.Equal("invalid-split", exception.CurrentStatus);
        Assert.Equal(0, await db.WorkOrderTransformations.CountAsync());
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-work-order-transformation-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
}
