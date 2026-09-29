using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Endpoints.Mes;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesBatchMaterialReadinessTests
{
    [Fact]
    public void Batch_rejects_more_than_500_work_orders()
    {
        var validator = new BatchMaterialReadinessRequestValidator();
        var request = new BatchMaterialReadinessRequest("org-001", "env-dev",
            Enumerable.Range(1, 501).Select(x => $"WO-{x}").ToArray());

        Assert.True(validator.Validate(request with { WorkOrderIds = request.WorkOrderIds.Take(500).ToArray() }).IsValid);
        Assert.False(validator.Validate(request).IsValid);
    }

    // Contract: DomainInvariant. Authority: #3994 shared-material allocation acceptance.
    [Fact]
    public async Task Earlier_due_order_receives_shared_material_once()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var capturedAt = DateTimeOffset.Parse("2026-09-20T08:00:00Z");
        db.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-LATE", "FG", "PV", 1m, 20, capturedAt.AddDays(2)),
            WorkOrder.Create("org-001", "env-dev", "WO-EARLY", "FG", "PV", 1m, 10, capturedAt.AddDays(1)));
        foreach (var workOrderId in new[] { "WO-LATE", "WO-EARLY" })
        {
            db.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001", "env-dev", workOrderId, "OP-10", "MAT-001", null,
                8m, 8m, 0m, "MBOM", $"{workOrderId}:MAT-001", capturedAt, [], "PCS"));
        }
        await db.SaveChangesAsync();

        var result = await new GetBatchMaterialReadinessQueryHandler(
            db, FrozenMaterialReadinessLiveCoverageProvider.Instance).Handle(
                new GetBatchMaterialReadinessQuery("org-001", "env-dev", ["WO-LATE", "WO-EARLY", "WO-EARLY"]),
                CancellationToken.None);

        Assert.Equal(["WO-EARLY", "WO-LATE"], result.Items.Select(x => x.WorkOrderId));
        Assert.Equal("Ready", result.Items[0].ReadinessStatus);
        Assert.Equal(8m, Assert.Single(result.Items[0].Items).AvailableQuantity);
        Assert.Equal("Blocked", result.Items[1].ReadinessStatus);
        Assert.Equal(8m, Assert.Single(result.Items[1].Items).ShortageQuantity);
    }

    [Fact]
    public async Task Priority_breaks_due_date_tie_and_missing_snapshot_stays_blocked()
    {
        await using var provider = MesTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var capturedAt = DateTimeOffset.Parse("2026-09-20T08:00:00Z");
        var due = capturedAt.AddDays(1);
        db.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-LOW", "FG", "PV", 1m, 10, due),
            WorkOrder.Create("org-001", "env-dev", "WO-HIGH", "FG", "PV", 1m, 20, due),
            WorkOrder.Create("org-001", "env-dev", "WO-MISSING", "FG", "PV", 1m, 5, due));
        foreach (var workOrderId in new[] { "WO-LOW", "WO-HIGH" })
        {
            db.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001", "env-dev", workOrderId, "OP-10", "MAT-001", null,
                6m, 6m, 0m, "MBOM", $"{workOrderId}:MAT-001", capturedAt, [], "PCS"));
        }
        await db.SaveChangesAsync();

        var result = await new GetBatchMaterialReadinessQueryHandler(
            db, FrozenMaterialReadinessLiveCoverageProvider.Instance).Handle(
                new GetBatchMaterialReadinessQuery("org-001", "env-dev",
                    ["WO-LOW", "WO-MISSING", "WO-HIGH"]), CancellationToken.None);

        Assert.Equal(["WO-HIGH", "WO-LOW", "WO-MISSING"], result.Items.Select(x => x.WorkOrderId));
        Assert.Equal("Ready", result.Items[0].ReadinessStatus);
        Assert.Equal(6m, Assert.Single(result.Items[1].Items).ShortageQuantity);
        Assert.Equal("Blocked", result.Items[2].ReadinessStatus);
        Assert.Equal([MaterialReadinessGuards.MissingRequirementSnapshotReason], result.Items[2].BlockingReasons);
    }
}
