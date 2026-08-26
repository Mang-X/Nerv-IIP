using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Business.Mes.Web.Application.Readiness;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesEngineeringChangeReadinessTests
{
    [Fact]
    public async Task EngineeringChangeReleasedHandler_KeepsOriginalReleaseSnapshotReadyAcrossConsecutiveRebinds()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-change-chain-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            var released = WorkOrder.Create("org-001", "env-dev", "WO-CHAIN", "SKU-FG-1000", "PV-1", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            released.MarkReleased();
            released.RecordMaterialRequirementSnapshot(
                WorkOrder.MaterialRequirementSnapshotCapturedStatus,
                DateTimeOffset.Parse("2026-07-06T07:00:00Z"));
            dbContext.WorkOrders.Add(released);
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-CHAIN",
                "OP-CHAIN",
                OperationTaskLifecycleStatus.Queued,
                10,
                "WC-ASSEMBLY",
                [],
                DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
                TimeSpan.FromHours(1),
                null,
                null));
            dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001",
                "env-dev",
                "WO-CHAIN",
                null,
                "MAT-CHAIN",
                null,
                10m,
                10m,
                0m,
                "test",
                "PV-1:MAT-CHAIN",
                DateTimeOffset.Parse("2026-07-06T07:00:00Z"),
                []));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await HandleAsync(options, "evt-eco-chain-1", "ECO-CHAIN-1", "PV-1", "PV-2");
        await HandleAsync(options, "evt-eco-chain-2", "ECO-CHAIN-2", "PV-2", "PV-3");

        await using var assertionDbContext = CreateDbContext(options);
        var workOrder = await assertionDbContext.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-CHAIN");
        Assert.Equal("PV-3", workOrder.ProductionVersionId);
        Assert.Equal("PV-1", workOrder.MaterialRequirementSnapshotProductionVersionId);
        Assert.Equal(
            [("PV-1", "PV-2"), ("PV-2", "PV-3")],
            await assertionDbContext.EngineeringChangeWorkOrderImpacts
                .Where(x => x.WorkOrderId == "WO-CHAIN" && x.Status == MesEngineeringChangeImpactStatuses.AutoRebound)
                .OrderBy(x => x.ArchivedProductionVersionId)
                .Select(x => ValueTuple.Create(x.ArchivedProductionVersionId, x.SupersededByProductionVersionId!))
                .ToArrayAsync());
        var task = await assertionDbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-CHAIN");
        var readiness = await new MesOperationTaskActionReadinessEvaluator(assertionDbContext).EvaluateAsync(
            task,
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            CancellationToken.None);
        Assert.Equal(["start"], readiness.AllowedActions);
        Assert.Empty(readiness.BlockReasons);
    }

    [Theory]
    [InlineData("wrong-work-order")]
    [InlineData("wrong-archived-version")]
    [InlineData("wrong-successor-version")]
    [InlineData("broken-chain")]
    [InlineData("wrong-organization")]
    [InlineData("wrong-environment")]
    [InlineData("non-released-impact")]
    [InlineData("unreachable-cycle")]
    public async Task MaterialReadiness_RejectsInvalidAutomaticRebindChains(string invalidChain)
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-product-engineering-invalid-chain-{Guid.CreateVersion7():N}", databaseRoot)
            .Options;
        await using (var dbContext = CreateDbContext(options))
        {
            var released = WorkOrder.Create("org-001", "env-dev", "WO-CHAIN", "SKU-FG-1000", "PV-1", 10m, 10, DateTimeOffset.Parse("2026-07-06T16:00:00Z"), "PCS");
            released.MarkReleased();
            released.RecordMaterialRequirementSnapshot(
                WorkOrder.MaterialRequirementSnapshotCapturedStatus,
                DateTimeOffset.Parse("2026-07-06T07:00:00Z"));
            released.RebindProductionVersionForEngineeringChange("PV-2");
            released.RebindProductionVersionForEngineeringChange("PV-3");
            dbContext.WorkOrders.Add(released);
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001", "env-dev", "WO-CHAIN", "OP-CHAIN", OperationTaskLifecycleStatus.Queued, 10,
                "WC-ASSEMBLY", [], DateTimeOffset.Parse("2026-07-06T08:00:00Z"), TimeSpan.FromHours(1), null, null));
            dbContext.MaterialRequirements.Add(MaterialRequirement.Capture(
                "org-001", "env-dev", "WO-CHAIN", null, "MAT-CHAIN", null, 10m, 10m, 0m, "test",
                "PV-1:MAT-CHAIN", DateTimeOffset.Parse("2026-07-06T07:00:00Z"), []));

            foreach (var edge in InvalidEdges(invalidChain))
            {
                dbContext.EngineeringChangeWorkOrderImpacts.Add(MesEngineeringChangeWorkOrderImpact.AutoRebound(
                    edge.OrganizationId,
                    edge.EnvironmentId,
                    edge.WorkOrderId,
                    "SKU-FG-1000",
                    edge.WorkOrderStatusAtDetection,
                    $"ECO-{edge.ArchivedProductionVersionId}-{edge.SupersededByProductionVersionId}",
                    edge.ArchivedProductionVersionId,
                    edge.SupersededByProductionVersionId,
                    new DateOnly(2026, 7, 6),
                    DateTimeOffset.Parse("2026-07-06T08:00:00Z")));
            }
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        var task = await assertionDbContext.OperationTasks.SingleAsync(x => x.OperationTaskIdValue == "OP-CHAIN");
        var readiness = await new MesOperationTaskActionReadinessEvaluator(assertionDbContext).EvaluateAsync(
            task,
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            CancellationToken.None);
        Assert.DoesNotContain("start", readiness.AllowedActions);
        Assert.Contains(readiness.BlockReasons, x => x.StartsWith("MATERIAL_REQUIREMENT_SNAPSHOT_MISSING:", StringComparison.Ordinal));
    }

    private static AutomaticRebindEdge[] InvalidEdges(string invalidChain) => invalidChain switch
    {
        "wrong-work-order" =>
        [
            new("org-001", "env-dev", "WO-OTHER", WorkOrder.ReleasedStatus, "PV-1", "PV-2"),
            new("org-001", "env-dev", "WO-OTHER", WorkOrder.ReleasedStatus, "PV-2", "PV-3"),
        ],
        "wrong-archived-version" =>
        [
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-X", "PV-2"),
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-2", "PV-3"),
        ],
        "wrong-successor-version" =>
        [
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-1", "PV-2"),
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-2", "PV-X"),
        ],
        "broken-chain" =>
        [
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-1", "PV-2"),
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-X", "PV-3"),
        ],
        "wrong-organization" =>
        [
            new("org-other", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-1", "PV-2"),
            new("org-other", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-2", "PV-3"),
        ],
        "wrong-environment" =>
        [
            new("org-001", "env-other", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-1", "PV-2"),
            new("org-001", "env-other", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-2", "PV-3"),
        ],
        "non-released-impact" =>
        [
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.CreatedStatus, "PV-1", "PV-2"),
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.CreatedStatus, "PV-2", "PV-3"),
        ],
        "unreachable-cycle" =>
        [
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-1", "PV-2"),
            new("org-001", "env-dev", "WO-CHAIN", WorkOrder.ReleasedStatus, "PV-2", "PV-1"),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(invalidChain), invalidChain, null),
    };

    private static async Task HandleAsync(
        DbContextOptions<ApplicationDbContext> options,
        string eventId,
        string changeNumber,
        string archivedProductionVersionId,
        string supersededByProductionVersionId)
    {
        await using var dbContext = CreateDbContext(options);
        var handler = new EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            new MesEngineeringChangeOptions { NotStartedPolicy = MesEngineeringChangeNotStartedPolicy.AutoRebind });

        await handler.HandleAsync(
            CreateEngineeringChangeReleasedEvent(eventId, changeNumber, archivedProductionVersionId, supersededByProductionVersionId),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static EngineeringChangeReleasedIntegrationEvent CreateEngineeringChangeReleasedEvent(
        string eventId,
        string changeNumber,
        string archivedProductionVersionId,
        string supersededByProductionVersionId) =>
        new(
            eventId,
            ProductEngineeringIntegrationEventTypes.EngineeringChangeReleased,
            ProductEngineeringIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-06T08:00:00Z"),
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            "corr-eco-721",
            "cause-eco-721",
            "org-001",
            "env-dev",
            "product-engineering",
            $"product-engineering:engineering-change-released:org-001:env-dev:{changeNumber}",
            new EngineeringChangeReleasedPayload(
                $"change-{changeNumber}",
                changeNumber,
                [archivedProductionVersionId],
                new DateOnly(2026, 7, 6),
                [new EngineeringChangeAffectedVersionPayload("production-version", archivedProductionVersionId, supersededByProductionVersionId)]));

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private sealed record AutomaticRebindEdge(
        string OrganizationId,
        string EnvironmentId,
        string WorkOrderId,
        string WorkOrderStatusAtDetection,
        string ArchivedProductionVersionId,
        string SupersededByProductionVersionId);
}
