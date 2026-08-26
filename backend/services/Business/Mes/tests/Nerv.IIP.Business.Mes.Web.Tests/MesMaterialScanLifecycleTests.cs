using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.ProductEngineering;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed partial class MesMaterialScanPrevalidationTests
{
    [Fact]
    public async Task Convert_release_rebind_receipt_then_scan_preserves_the_released_snapshot_chain()
    {
        await using var db = CreateDbContext();
        var snapshotProvider = new StaticMaterialSnapshotProvider(
            MesMaterialRequirementSnapshotResult.Captured(
                "product-engineering-http:PV-001:MBOM-001",
                [new MesMaterialRequirementSnapshotLine(
                    null, "MAT-PRIMARY", null, 5m, "PCS", 5m, 0m,
                    "MBOM-001:MAT-PRIMARY", [])]));
        var converted = await new ConvertPlanToWorkOrderCommandHandler(
            db,
            new RuleScheduler(),
            null,
            snapshotProvider).Handle(
                new ConvertPlanToWorkOrderCommand(
                    "org-001", "env-dev", "PLAN-SCAN-001", null, Now,
                    "FG-001", "PV-001", 10m, "PCS", Now.AddDays(1), "WC-01",
                    "DemandPlanning", "PlanningSuggestion", "SUG-SCAN-001", "DEMAND-SCAN-001",
                    "convert-plan-material-scan"),
                CancellationToken.None);
        await db.SaveChangesAsync();
        var workOrderId = converted.ReferenceId;
        var operationTaskId = $"{workOrderId}-OP-10";
        var captureIdentity = Assert.Single(db.MaterialRequirements).CapturedAtUtc;

        await new ReleaseWorkOrderCommandHandler(db, snapshotProvider).Handle(
            new ReleaseWorkOrderCommand("org-001", "env-dev", workOrderId, Now.AddMinutes(1)),
            CancellationToken.None);
        var workOrder = await db.WorkOrders.SingleAsync();
        Assert.Equal(captureIdentity, workOrder.MaterialRequirementSnapshotEvaluatedAtUtc);
        await db.SaveChangesAsync();

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await new EngineeringChangeReleasedIntegrationEventHandlerForMesWip(
                db,
                deadLetters,
                new MesEngineeringChangeOptions { NotStartedPolicy = MesEngineeringChangeNotStartedPolicy.AutoRebind })
            .HandleAsync(CreateEngineeringChangeReleasedEvent(), CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Equal("PV-002", workOrder.ProductionVersionId);
        Assert.Single(await db.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName == EngineeringChangeReleasedIntegrationEventHandlerForMesWip.ConsumerName)
            .ToArrayAsync());

        var issue = MaterialIssueRequest.Create(
            "org-001", "env-dev", "MIR-LIFECYCLE-001", workOrderId, operationTaskId,
            "MAT-PRIMARY", "PCS", 5m, Now.AddMinutes(2));
        db.MaterialIssueRequests.Add(issue);
        await db.SaveChangesAsync();
        await new ConfirmLineSideMaterialReceiptCommandHandler(db, MaterialSupplyTestFixtures.Resolver).Handle(
            new ConfirmLineSideMaterialReceiptCommand(
                "org-001", "env-dev", "MIR-LIFECYCLE-001", Now.AddMinutes(3), 5m, "LOT-001"),
            CancellationToken.None);
        await db.SaveChangesAsync();
        Assert.Equal(MaterialIssueRequest.ReceiptPostingStatus, issue.Status);
        Assert.Equal(0m, issue.ReceivedQuantity);

        var postingToken = Assert.IsType<string>(issue.PendingPostingToken);
        var stockMovementConsumer = new StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted(
            db,
            deadLetters);
        await stockMovementConsumer.HandleAsync(
            CreateMaterialTransferPostedEvent(
                "evt-scan-warehouse-issue",
                issue,
                postingToken,
                MaterialTransferLeg.WarehouseIssue,
                allocationIndex: 0),
            CancellationToken.None);
        Assert.Equal(MaterialIssueRequest.ReceiptPostingStatus, issue.Status);
        Assert.Equal(0m, issue.ReceivedQuantity);
        Assert.True(issue.PendingIssueLegPosted);
        Assert.False(issue.PendingReceiptLegPosted);

        await stockMovementConsumer.HandleAsync(
            CreateMaterialTransferPostedEvent(
                "evt-scan-line-side-receipt",
                issue,
                postingToken,
                MaterialTransferLeg.LineSideReceipt),
            CancellationToken.None);
        Assert.Equal(MaterialIssueRequest.ReceivedStatus, issue.Status);
        Assert.Equal(5m, issue.ReceivedQuantity);
        Assert.Equal(2, await db.ProcessedIntegrationEvents
            .CountAsync(x => x.ConsumerName == StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted.ConsumerName));

        var response = await CreateHandler(db, new StubAvailabilityProvider(new(true, false, true))).Handle(
            new PrevalidateMaterialScanQuery(
                "org-001", "env-dev", "MIR-LIFECYCLE-001", workOrderId, operationTaskId),
            CancellationToken.None);

        Assert.Equal(MesMaterialScanDecision.Accepted, response.Decision);
        Assert.Equal("material-scan-accepted", response.ReasonCode);
        Assert.Equal("PV-001", workOrder.MaterialRequirementSnapshotProductionVersionId);
        Assert.Equal("PV-002", workOrder.ProductionVersionId);
    }

    [Theory]
    [InlineData("wrong-organization")]
    [InlineData("non-released-impact")]
    [InlineData("broken-version-chain")]
    public async Task Scan_fails_closed_when_the_rebind_is_not_a_scoped_released_chain(string mutation)
    {
        await using var db = CreateDbContext();
        SeedMesFacts(db, "MAT-PRIMARY", includeRequirement: true, completeReceipt: true);
        var workOrder = Assert.Single(db.WorkOrders.Local);
        workOrder.MarkReleased();
        workOrder.RebindProductionVersionForEngineeringChange("PV-002");
        db.EngineeringChangeWorkOrderImpacts.Add(MesEngineeringChangeWorkOrderImpact.AutoRebound(
            mutation == "wrong-organization" ? "org-other" : "org-001",
            "env-dev",
            "WO-001",
            "FG-001",
            mutation == "non-released-impact" ? WorkOrder.CreatedStatus : WorkOrder.ReleasedStatus,
            "ECO-SCAN-MUTATION",
            mutation == "broken-version-chain" ? "PV-OTHER" : "PV-001",
            "PV-002",
            new DateOnly(2026, 8, 26),
            Now.AddMinutes(2)));
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            CreateHandler(db, new StubAvailabilityProvider(new(true, false, true)))
                .Handle(Request(), CancellationToken.None));

        Assert.Equal(MaterialScanPrevalidationErrors.SourceUnavailableMessage, exception.Message);
    }

    private static EngineeringChangeReleasedIntegrationEvent CreateEngineeringChangeReleasedEvent() =>
        new(
            "evt-scan-eco-released",
            ProductEngineeringIntegrationEventTypes.EngineeringChangeReleased,
            ProductEngineeringIntegrationEventVersions.V1,
            Now.AddMinutes(2),
            ProductEngineeringIntegrationEventSources.BusinessProductEngineering,
            "corr-scan-eco",
            "cause-scan-release",
            "org-001",
            "env-dev",
            "product-engineering",
            "product-engineering:engineering-change-released:org-001:env-dev:ECO-SCAN-001",
            new EngineeringChangeReleasedPayload(
                "change-scan-001",
                "ECO-SCAN-001",
                ["PV-001"],
                new DateOnly(2026, 8, 26),
                [new EngineeringChangeAffectedVersionPayload("production-version", "PV-001", "PV-002")]));

    private static StockMovementPostedIntegrationEvent CreateMaterialTransferPostedEvent(
        string eventId,
        MaterialIssueRequest issue,
        string postingToken,
        MaterialTransferLeg leg,
        int? allocationIndex = null)
    {
        var idempotencyKey = MaterialIssueRequest.BuildLegIdempotencyKey(postingToken, leg, allocationIndex);
        return new StockMovementPostedIntegrationEvent(
            eventId,
            InventoryIntegrationEventTypes.StockMovementPosted,
            InventoryIntegrationEventVersions.V1,
            Now.AddMinutes(4),
            InventoryIntegrationEventSources.BusinessInventory,
            "corr-scan-inventory",
            "cause-scan-receipt",
            issue.OrganizationId,
            issue.EnvironmentId,
            "inventory",
            $"inventory:posted:{eventId}",
            new StockMovementPostedPayload(
                $"movement-{eventId}",
                leg == MaterialTransferLeg.WarehouseIssue ? InventoryMovementTypes.Outbound : InventoryMovementTypes.Inbound,
                InventoryIntegrationEventSources.BusinessMes,
                issue.RequestNo,
                issue.OperationTaskId,
                idempotencyKey,
                issue.MaterialId,
                issue.UomCode,
                leg == MaterialTransferLeg.WarehouseIssue ? issue.SourceSiteCode! : issue.TargetSiteCode!,
                leg == MaterialTransferLeg.WarehouseIssue ? issue.SourceLocationCode! : issue.TargetLocationCode!,
                issue.MaterialLotId,
                null,
                "Unrestricted",
                "production",
                null,
                5m,
                Now.AddMinutes(4),
                null,
                null));
    }
}
