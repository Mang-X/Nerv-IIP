using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderTransformationAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class WorkOrderTransformationApplicationPostgresTests
{
    [MesRealPostgresFact]
    public async Task Split_application_handler_persists_and_replays_on_postgresql()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-26T05:00:00Z");
        var command = new SplitWorkOrderCommand(
            "org-001",
            "env-dev",
            "WO-APP-PARENT",
            [
                new("WO-APP-CHILD-1", 4m),
                new("WO-APP-CHILD-2", 6m),
            ],
            "应用层拆分",
            "split-application-postgres-001",
            "user:planner-001",
            occurredAtUtc);

        await using (var setup = new ApplicationDbContext(options, new NoopMediator()))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync(CancellationToken.None);
            setup.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-dev", "WO-APP-PARENT", "SKU-001", "PV-001", 10m, 10,
                occurredAtUtc.AddHours(4), "PCS"));
            await setup.SaveChangesAsync(CancellationToken.None);

            var handler = new SplitWorkOrderCommandHandler(setup);
            var first = await handler.Handle(command, CancellationToken.None);
            await setup.SaveChangesAsync(CancellationToken.None);
            setup.ChangeTracker.Clear();

            var replay = await handler.Handle(command, CancellationToken.None);
            Assert.False(first.IsIdempotentReplay);
            Assert.True(replay.IsIdempotentReplay);
            Assert.Equal(first.TransformationId, replay.TransformationId);
            Assert.Equal(["WO-APP-CHILD-1", "WO-APP-CHILD-2"], replay.TargetWorkOrderIds);
        }

        await using var assertion = new ApplicationDbContext(options, new NoopMediator());
        var parent = await assertion.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-APP-PARENT");
        var transformation = await assertion.WorkOrderTransformations
            .Include(x => x.Lines)
            .SingleAsync(x => x.IdempotencyKey == command.IdempotencyKey);

        Assert.Equal(WorkOrder.SplitStatus, parent.Status);
        Assert.Equal(2, parent.Version);
        Assert.Equal(10m, transformation.Lines.Sum(x => x.Quantity));
        Assert.Equal(2, transformation.Lines.Count);
        Assert.All(transformation.Lines, line =>
        {
            Assert.Equal("WO-APP-PARENT", line.SourceWorkOrderId);
            Assert.Equal("PCS", line.UomCode);
            Assert.Equal(WorkOrderLineageType.Split, line.LineageType);
        });
    }
}
