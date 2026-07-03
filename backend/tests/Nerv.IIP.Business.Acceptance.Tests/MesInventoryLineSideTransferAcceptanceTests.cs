using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Web.Application.Queries.Production;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class MesInventoryLineSideTransferAcceptanceTests
{
    [Fact]
    public async Task Mes_work_order_cancel_releases_inventory_reservation_and_returns_received_line_side_material_idempotently()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb, "WO-695");
        await mesDb.SaveChangesAsync();
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryMovementHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var inventoryReservationHandler = CreateInventoryReservationReleaseHandler(inventoryDb);
        var issuedAtUtc = DateTimeOffset.Parse("2026-07-03T08:00:00Z");
        var receivedAtUtc = issuedAtUtc.AddMinutes(20);
        var cancelledAtUtc = issuedAtUtc.AddHours(1);
        await inventoryMovementHandler.HandleAsync(CreateWarehouseSeedEvent(issuedAtUtc.AddMinutes(-10), "WO-695", 10m), CancellationToken.None);

        var issueResult = await new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001",
                "env-dev",
                "WO-695",
                "OP-10",
                "MAT-OIL",
                "L",
                6m,
                issuedAtUtc,
                "issue-695"),
            CancellationToken.None);
        var issueRequest = mesDb.MaterialIssueRequests.Local.Single(x => x.RequestNo == issueResult.ReferenceId);
        await new ReserveStockCommandHandler(inventoryDb).Handle(
            new ReserveStockCommand(
                "org-001",
                "env-dev",
                InventoryIntegrationEventSources.BusinessMes,
                "WO-695",
                issueRequest.RequestNo,
                "reserve:wo-695:mir",
                "MAT-OIL",
                "L",
                "warehouse",
                "line-side",
                "LOT-OIL-A",
                null,
                "Unrestricted",
                "production",
                null,
                6m),
            CancellationToken.None);
        await inventoryDb.SaveChangesAsync();
        await mesDb.SaveChangesAsync();

        await new ConfirmLineSideMaterialReceiptCommandHandler(mesDb).Handle(
            new ConfirmLineSideMaterialReceiptCommand(
                "org-001",
                "env-dev",
                issueResult.ReferenceId,
                receivedAtUtc,
                2m,
                "LOT-OIL-A"),
            CancellationToken.None);
        var transferEvents = issueRequest.GetDomainEvents().ToArray();
        var issueEvent = new MaterialIssueRequestedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialIssueRequestedDomainEvent>(transferEvents[0]));
        var receiptEvent = new MaterialLineSideReceiptConfirmedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialLineSideReceiptConfirmedDomainEvent>(transferEvents[1]));
        issueRequest.ClearDomainEvents();
        await mesDb.SaveChangesAsync();
        await inventoryMovementHandler.HandleAsync(issueEvent, CancellationToken.None);
        await inventoryMovementHandler.HandleAsync(receiptEvent, CancellationToken.None);
        Assert.Equal(2m, inventoryDb.StockLedgers.Single(x => x.SiteCode == "warehouse" && x.LocationCode == "line-side").AvailableQuantity);

        var cancelResponse = await new CancelWorkOrderCommandHandler(mesDb).Handle(
            new CancelWorkOrderCommand("org-001", "env-dev", "WO-695", "plan cancelled", cancelledAtUtc),
            CancellationToken.None);
        var cancellationEvent = new WorkOrderCancelledIntegrationEventConverter().Convert(
            Assert.IsType<WorkOrderCancelledDomainEvent>(mesDb.WorkOrders.Local.Single(x => x.WorkOrderId == "WO-695").GetDomainEvents().Last()));
        var cancellationEvents = issueRequest.GetDomainEvents().ToArray();
        var returnOutEvent = new MaterialLineSideReturnRequestedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialLineSideReturnRequestedDomainEvent>(cancellationEvents[0]));
        var returnInEvent = new MaterialReturnedToWarehouseIntegrationEventConverter().Convert(
            Assert.IsType<MaterialReturnedToWarehouseDomainEvent>(cancellationEvents[1]));
        await mesDb.SaveChangesAsync();

        await inventoryReservationHandler.HandleAsync(cancellationEvent, CancellationToken.None);
        await inventoryReservationHandler.HandleAsync(cancellationEvent, CancellationToken.None);
        await inventoryMovementHandler.HandleAsync(returnOutEvent, CancellationToken.None);
        await inventoryMovementHandler.HandleAsync(returnInEvent, CancellationToken.None);

        Assert.Equal("Accepted", cancelResponse.Status);
        Assert.Equal(MaterialIssueRequest.ReturnRequestedStatus, issueRequest.Status);
        Assert.Equal(0m, inventoryDb.StockReservations.Single().OpenQuantity);
        Assert.Equal("released", inventoryDb.StockReservations.Single().Status);
        var warehouseLedger = inventoryDb.StockLedgers.Single(x => x.SiteCode == "warehouse" && x.LocationCode == "line-side");
        var lineSideLedger = inventoryDb.StockLedgers.Single(x => x.SiteCode == "production" && x.LocationCode == "line-side");
        Assert.Equal(10m, warehouseLedger.OnHandQuantity);
        Assert.Equal(0m, warehouseLedger.ReservedQuantity);
        Assert.Equal(10m, warehouseLedger.AvailableQuantity);
        Assert.Equal(0m, lineSideLedger.OnHandQuantity);
    }

    [Fact]
    public async Task Mes_work_order_cancel_returns_only_unconsumed_line_side_material()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb, "WO-695-CONSUME");
        await mesDb.SaveChangesAsync();
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryMovementHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var inventoryReservationHandler = CreateInventoryReservationReleaseHandler(inventoryDb);
        var issuedAtUtc = DateTimeOffset.Parse("2026-07-03T08:00:00Z");
        var receivedAtUtc = issuedAtUtc.AddMinutes(20);
        var reportedAtUtc = issuedAtUtc.AddMinutes(40);
        var cancelledAtUtc = issuedAtUtc.AddHours(1);
        await inventoryMovementHandler.HandleAsync(CreateWarehouseSeedEvent(issuedAtUtc.AddMinutes(-10), "WO-695-CONSUME", 10m), CancellationToken.None);

        var issueResult = await new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001",
                "env-dev",
                "WO-695-CONSUME",
                "OP-10",
                "MAT-OIL",
                "L",
                6m,
                issuedAtUtc,
                "issue-695-consume"),
            CancellationToken.None);
        var issueRequest = mesDb.MaterialIssueRequests.Local.Single(x => x.RequestNo == issueResult.ReferenceId);
        await new ReserveStockCommandHandler(inventoryDb).Handle(
            new ReserveStockCommand(
                "org-001",
                "env-dev",
                InventoryIntegrationEventSources.BusinessMes,
                "WO-695-CONSUME",
                issueRequest.RequestNo,
                "reserve:wo-695-consume:mir",
                "MAT-OIL",
                "L",
                "warehouse",
                "line-side",
                "LOT-OIL-A",
                null,
                "Unrestricted",
                "production",
                null,
                6m),
            CancellationToken.None);
        await inventoryDb.SaveChangesAsync();
        await mesDb.SaveChangesAsync();

        await new ConfirmLineSideMaterialReceiptCommandHandler(mesDb).Handle(
            new ConfirmLineSideMaterialReceiptCommand(
                "org-001",
                "env-dev",
                issueResult.ReferenceId,
                receivedAtUtc,
                6m,
                "LOT-OIL-A"),
            CancellationToken.None);
        var transferEvents = issueRequest.GetDomainEvents().ToArray();
        var issueEvent = new MaterialIssueRequestedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialIssueRequestedDomainEvent>(transferEvents[0]));
        var receiptEvent = new MaterialLineSideReceiptConfirmedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialLineSideReceiptConfirmedDomainEvent>(transferEvents[1]));
        issueRequest.ClearDomainEvents();
        await mesDb.SaveChangesAsync();

        var reportResult = await new RecordProductionReportCommandHandler(mesDb).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-695-CONSUME",
                "OP-10",
                1m,
                0m,
                false,
                reportedAtUtc,
                "report-695-consume",
                [
                    new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 4m, issueResult.ReferenceId),
                ]),
            CancellationToken.None);
        var consumption = mesDb.ProductionReportMaterialConsumptions.Local.Single(x => x.ReportNo == reportResult.ReportNo);
        var consumptionEvent = new ProductionMaterialConsumedIntegrationEventConverter().Convert(
            Assert.IsType<ProductionMaterialConsumedDomainEvent>(consumption.GetDomainEvents().Single()));
        await mesDb.SaveChangesAsync();
        foreach (var movementEvent in new[] { issueEvent, receiptEvent, consumptionEvent })
        {
            await inventoryMovementHandler.HandleAsync(movementEvent, CancellationToken.None);
        }

        await new CancelWorkOrderCommandHandler(mesDb).Handle(
            new CancelWorkOrderCommand("org-001", "env-dev", "WO-695-CONSUME", "plan cancelled", cancelledAtUtc),
            CancellationToken.None);
        var cancellationEvent = new WorkOrderCancelledIntegrationEventConverter().Convert(
            Assert.IsType<WorkOrderCancelledDomainEvent>(mesDb.WorkOrders.Local.Single(x => x.WorkOrderId == "WO-695-CONSUME").GetDomainEvents().Last()));
        var cancellationEvents = issueRequest.GetDomainEvents().ToArray();
        var returnOutEvent = new MaterialLineSideReturnRequestedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialLineSideReturnRequestedDomainEvent>(cancellationEvents[0]));
        var returnInEvent = new MaterialReturnedToWarehouseIntegrationEventConverter().Convert(
            Assert.IsType<MaterialReturnedToWarehouseDomainEvent>(cancellationEvents[1]));

        Assert.Equal(-2m, returnOutEvent.Payload.Quantity);
        Assert.Equal(2m, returnInEvent.Payload.Quantity);
        await inventoryReservationHandler.HandleAsync(cancellationEvent, CancellationToken.None);
        await inventoryMovementHandler.HandleAsync(returnOutEvent, CancellationToken.None);
        await inventoryMovementHandler.HandleAsync(returnInEvent, CancellationToken.None);

        Assert.Equal(MaterialIssueRequest.ReturnRequestedStatus, issueRequest.Status);
        Assert.Equal(4m, issueRequest.ReceivedQuantity);
        Assert.Equal(0m, inventoryDb.StockReservations.Single().OpenQuantity);
        var warehouseLedger = inventoryDb.StockLedgers.Single(x => x.SiteCode == "warehouse" && x.LocationCode == "line-side");
        var lineSideLedger = inventoryDb.StockLedgers.Single(x => x.SiteCode == "production" && x.LocationCode == "line-side");
        Assert.Equal(0m, warehouseLedger.ReservedQuantity);
        Assert.Equal(warehouseLedger.OnHandQuantity, warehouseLedger.AvailableQuantity);
        Assert.Equal(0m, lineSideLedger.OnHandQuantity);
    }

    [Fact]
    public async Task Mes_finished_goods_receipt_unit_cost_flows_to_inventory_moving_average_valuation()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb, "WO-483", "SKU-FG-483");
        await mesDb.SaveChangesAsync();
        await RecordMesOutputLotAsync(mesDb, "WO-483", "LOT-FG-483", 8m, DateTimeOffset.Parse("2026-06-23T07:45:00Z"));
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var requestedAtUtc = DateTimeOffset.Parse("2026-06-23T08:00:00Z");

        var receiptResult = await new CreateFinishedGoodsReceiptRequestCommandHandler(mesDb).Handle(
            new CreateFinishedGoodsReceiptRequestCommand(
                "org-001",
                "env-dev",
                "WO-483",
                "SKU-FG-483",
                8m,
                "PCS",
                requestedAtUtc,
                UnitCost: 12.34m,
                IdempotencyKey: "receipt-483",
                ProducedLotNo: "LOT-FG-483"),
            CancellationToken.None);
        var receiptRequest = mesDb.FinishedGoodsReceiptRequests.Local.Single(x => x.RequestNo == receiptResult.RequestNo);
        var receiptEvent = new FinishedGoodsReceiptRequestedIntegrationEventConverter()
            .Convert(Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(receiptRequest.GetDomainEvents().Single()));

        await inventoryHandler.HandleAsync(receiptEvent, CancellationToken.None);

        Assert.Empty(inventoryPublisher.Published);
        Assert.Equal(12.34m, receiptEvent.Payload.UnitCost);
        var movement = Assert.Single(inventoryDb.StockMovements);
        var ledger = Assert.Single(inventoryDb.StockLedgers);
        Assert.Equal("FGR", receiptRequest.RequestNo[..3]);
        Assert.Equal("SKU-FG-483", movement.SkuCode);
        Assert.Equal(8m, movement.Quantity);
        Assert.Equal(12.34m, movement.UnitCost);
        Assert.Equal(98.72m, movement.MovementAmount);
        Assert.Equal(8m, ledger.OnHandQuantity);
        Assert.Equal(12.34m, ledger.MovingAverageUnitCost);
        Assert.Equal(98.72m, ledger.InventoryValue);
    }

    [Fact]
    public async Task Mes_issue_receipt_and_consumption_posts_continuous_inventory_line_side_account()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb);
        await mesDb.SaveChangesAsync();
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var issuedAtUtc = DateTimeOffset.Parse("2026-06-18T08:00:00Z");
        var receivedAtUtc = issuedAtUtc.AddMinutes(20);
        var reportedAtUtc = issuedAtUtc.AddHours(1);
        await inventoryHandler.HandleAsync(CreateWarehouseSeedEvent(issuedAtUtc.AddMinutes(-10)), CancellationToken.None);

        var issueResult = await new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001",
                "env-dev",
                "WO-446",
                "OP-10",
                "MAT-OIL",
                "L",
                5m,
                issuedAtUtc,
                "issue-446"),
            CancellationToken.None);
        var issueRequest = mesDb.MaterialIssueRequests.Local.Single(x => x.RequestNo == issueResult.ReferenceId);
        await mesDb.SaveChangesAsync();

        await new ConfirmLineSideMaterialReceiptCommandHandler(mesDb).Handle(
            new ConfirmLineSideMaterialReceiptCommand(
                "org-001",
                "env-dev",
                issueResult.ReferenceId,
                receivedAtUtc,
                5m,
                "LOT-OIL-A"),
            CancellationToken.None);
        var transferEvents = issueRequest.GetDomainEvents().ToArray();
        var issueEvent = new MaterialIssueRequestedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialIssueRequestedDomainEvent>(transferEvents[0]));
        var receiptEvent = new MaterialLineSideReceiptConfirmedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialLineSideReceiptConfirmedDomainEvent>(transferEvents[1]));
        issueRequest.ClearDomainEvents();
        await mesDb.SaveChangesAsync();

        var reportResult = await new RecordProductionReportCommandHandler(mesDb).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-446",
                "OP-10",
                1m,
                0m,
                false,
                reportedAtUtc,
                "report-446",
                [
                    new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 2m, issueResult.ReferenceId),
                ]),
            CancellationToken.None);
        var consumption = mesDb.ProductionReportMaterialConsumptions.Local.Single(x => x.ReportNo == reportResult.ReportNo);
        var consumptionEvent = new ProductionMaterialConsumedIntegrationEventConverter().Convert(
            Assert.IsType<ProductionMaterialConsumedDomainEvent>(consumption.GetDomainEvents().Single()));

        foreach (var movementEvent in new[] { issueEvent, receiptEvent, consumptionEvent })
        {
            await inventoryHandler.HandleAsync(movementEvent, CancellationToken.None);
        }

        Assert.Empty(inventoryPublisher.Published);
        Assert.Equal(0m, inventoryDb.StockLedgers.Single(x =>
            x.SiteCode == "warehouse" &&
            x.LocationCode == "line-side" &&
            x.SkuCode == "MAT-OIL" &&
            x.LotNo == "LOT-OIL-A").OnHandQuantity);
        Assert.Equal(3m, inventoryDb.StockLedgers.Single(x =>
            x.SiteCode == "production" &&
            x.LocationCode == "line-side" &&
            x.SkuCode == "MAT-OIL" &&
            x.LotNo == "LOT-OIL-A").OnHandQuantity);
        Assert.Contains(inventoryDb.StockMovements, x =>
            x.SiteCode == "warehouse" &&
            x.LocationCode == "line-side" &&
            x.Quantity == -5m);
        Assert.Equal(4, inventoryDb.StockMovements.Count());
    }

    [Fact]
    public async Task Inventory_rejecting_mes_line_side_receipt_rolls_back_material_issue_status()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb);
        await mesDb.SaveChangesAsync();
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var failedConsumer = new StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore());

        var issueResult = await new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001",
                "env-dev",
                "WO-446",
                "OP-10",
                "MAT-OIL",
                "L",
                5m,
                DateTimeOffset.Parse("2026-06-18T08:00:00Z"),
                "issue-541"),
            CancellationToken.None);
        var issueRequest = mesDb.MaterialIssueRequests.Local.Single(x => x.RequestNo == issueResult.ReferenceId);
        await mesDb.SaveChangesAsync();

        await new ConfirmLineSideMaterialReceiptCommandHandler(mesDb).Handle(
            new ConfirmLineSideMaterialReceiptCommand(
                "org-001",
                "env-dev",
                issueResult.ReferenceId,
                DateTimeOffset.Parse("2026-06-18T08:20:00Z"),
                5m,
                "LOT-OIL-A"),
            CancellationToken.None);
        var issueEvent = new MaterialIssueRequestedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialIssueRequestedDomainEvent>(issueRequest.GetDomainEvents().First()));
        issueRequest.ClearDomainEvents();
        await mesDb.SaveChangesAsync();

        await inventoryHandler.HandleAsync(issueEvent, CancellationToken.None);
        var failedEvent = Assert.IsType<StockMovementPostingFailedIntegrationEvent>(Assert.Single(inventoryPublisher.Published));

        await failedConsumer.HandleAsync(failedEvent, CancellationToken.None);
        await mesDb.SaveChangesAsync();

        var persistedIssue = await mesDb.MaterialIssueRequests.SingleAsync();
        Assert.Equal(MaterialIssueRequest.RequestedStatus, persistedIssue.Status);
        Assert.Equal(0m, persistedIssue.ReceivedQuantity);
        Assert.Null(persistedIssue.ReceivedAtUtc);
        Assert.Equal("NEGATIVE_ON_HAND", persistedIssue.InventoryPostingFailureCode);
        Assert.Contains("negative", persistedIssue.InventoryPostingFailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(persistedIssue.InventoryPostingFailedAtUtc);
    }

    [Fact]
    public async Task Inventory_rejecting_both_mes_transfer_legs_rolls_back_material_issue_once()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb);
        await mesDb.SaveChangesAsync();
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var failedConsumer = new StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore());

        var issueResult = await new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001",
                "env-dev",
                "WO-446",
                "OP-10",
                "MAT-OIL",
                "L",
                10m,
                DateTimeOffset.Parse("2026-06-18T08:00:00Z"),
                "issue-541-double"),
            CancellationToken.None);
        var issueRequest = mesDb.MaterialIssueRequests.Local.Single(x => x.RequestNo == issueResult.ReferenceId);
        issueRequest.ConfirmLineSideReceipt(DateTimeOffset.Parse("2026-06-18T08:10:00Z"), 3m, "LOT-OIL-A");
        issueRequest.ClearDomainEvents();
        issueRequest.ConfirmLineSideReceipt(DateTimeOffset.Parse("2026-06-18T08:20:00Z"), 5m, "LOT-OIL-A");
        var transferEvents = issueRequest.GetDomainEvents().ToArray();
        var issueEvent = new MaterialIssueRequestedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialIssueRequestedDomainEvent>(transferEvents[0]));
        var receiptEvent = new MaterialLineSideReceiptConfirmedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialLineSideReceiptConfirmedDomainEvent>(transferEvents[1]));
        issueRequest.ClearDomainEvents();
        await mesDb.SaveChangesAsync();

        var failedEvents = new[]
        {
            CreateFailedEvent(issueEvent, "evt-failed-material-issue-leg"),
            CreateFailedEvent(receiptEvent, "evt-failed-line-side-receipt-leg"),
        };

        foreach (var failedEvent in failedEvents)
        {
            await failedConsumer.HandleAsync(failedEvent, CancellationToken.None);
            await mesDb.SaveChangesAsync();
        }

        var persistedIssue = await mesDb.MaterialIssueRequests.SingleAsync();
        Assert.Equal(3m, persistedIssue.ReceivedQuantity);
        Assert.Equal(MaterialIssueRequest.PartiallyReceivedStatus, persistedIssue.Status);
        Assert.Equal("NEGATIVE_ON_HAND", persistedIssue.InventoryPostingFailureCode);
    }

    [Fact]
    public async Task Inventory_rejecting_mes_production_consumption_marks_consumption_failed_without_polluting_material_issue()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb);
        await mesDb.SaveChangesAsync();
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var failedConsumer = new StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore());

        var issueResult = await new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001",
                "env-dev",
                "WO-446",
                "OP-10",
                "MAT-OIL",
                "L",
                5m,
                DateTimeOffset.Parse("2026-06-18T08:00:00Z"),
                "issue-541-consumption"),
            CancellationToken.None);
        var issueRequest = mesDb.MaterialIssueRequests.Local.Single(x => x.RequestNo == issueResult.ReferenceId);
        issueRequest.ConfirmLineSideReceipt(DateTimeOffset.Parse("2026-06-18T08:20:00Z"), 5m, "LOT-OIL-A");
        issueRequest.ClearDomainEvents();
        await mesDb.SaveChangesAsync();

        var reportResult = await new RecordProductionReportCommandHandler(mesDb).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-446",
                "OP-10",
                1m,
                0m,
                false,
                DateTimeOffset.Parse("2026-06-18T09:00:00Z"),
                "report-541-consumption",
                [
                    new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 2m, issueResult.ReferenceId),
                ]),
            CancellationToken.None);
        var consumption = mesDb.ProductionReportMaterialConsumptions.Local.Single(x => x.ReportNo == reportResult.ReportNo);
        var consumptionEvent = new ProductionMaterialConsumedIntegrationEventConverter().Convert(
            Assert.IsType<ProductionMaterialConsumedDomainEvent>(consumption.GetDomainEvents().Single()));
        await mesDb.SaveChangesAsync();

        await inventoryHandler.HandleAsync(consumptionEvent, CancellationToken.None);
        var failedEvent = Assert.IsType<StockMovementPostingFailedIntegrationEvent>(Assert.Single(inventoryPublisher.Published));

        await failedConsumer.HandleAsync(failedEvent, CancellationToken.None);
        await mesDb.SaveChangesAsync();

        var persistedConsumption = await mesDb.ProductionReportMaterialConsumptions.SingleAsync();
        Assert.Equal("NEGATIVE_ON_HAND", persistedConsumption.InventoryPostingFailureCode);
        Assert.Contains("negative", persistedConsumption.InventoryPostingFailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(persistedConsumption.InventoryPostingFailedAtUtc);
        var reports = await new ListProductionReportsQueryHandler(mesDb).Handle(
            new ListProductionReportsQuery("org-001", "env-dev", "WO-446"),
            CancellationToken.None);
        var visibleReport = Assert.Single(reports.Items);
        Assert.Equal("NEGATIVE_ON_HAND", visibleReport.InventoryPostingFailureCode);
        Assert.Contains("negative", visibleReport.InventoryPostingFailureMessage, StringComparison.OrdinalIgnoreCase);
        var persistedIssue = await mesDb.MaterialIssueRequests.SingleAsync();
        Assert.Null(persistedIssue.InventoryPostingFailureCode);
        Assert.Equal(MaterialIssueRequest.ReceivedStatus, persistedIssue.Status);
        Assert.Equal(5m, persistedIssue.ReceivedQuantity);
    }

    [Fact]
    public async Task Inventory_rejecting_mes_finished_goods_receipt_marks_receipt_failed()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb, "WO-541", "SKU-FG-541");
        await mesDb.SaveChangesAsync();
        await RecordMesOutputLotAsync(mesDb, "WO-541", "LOT-FG-541", 8m, DateTimeOffset.Parse("2026-06-18T08:45:00Z"));
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var failedConsumer = new StockMovementPostingFailedIntegrationEventHandlerForMarkMesRequestFailed(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore());

        var receiptResult = await new CreateFinishedGoodsReceiptRequestCommandHandler(mesDb).Handle(
            new CreateFinishedGoodsReceiptRequestCommand(
                "org-001",
                "env-dev",
                "WO-541",
                "SKU-FG-541",
                8m,
                "PCS",
                DateTimeOffset.Parse("2026-06-18T09:00:00Z"),
                UnitCost: 12.34m,
                IdempotencyKey: "receipt-541",
                ProducedLotNo: "LOT-FG-541"),
            CancellationToken.None);
        var receiptRequest = mesDb.FinishedGoodsReceiptRequests.Local.Single(x => x.RequestNo == receiptResult.RequestNo);
        var receiptEvent = new FinishedGoodsReceiptRequestedIntegrationEventConverter()
            .Convert(Assert.IsType<FinishedGoodsReceiptRequestedDomainEvent>(receiptRequest.GetDomainEvents().Single()));
        await mesDb.SaveChangesAsync();

        await inventoryHandler.HandleAsync(receiptEvent, CancellationToken.None);
        await inventoryHandler.HandleAsync(receiptEvent with
        {
            EventId = "evt-fgr-541-conflict",
            Payload = receiptEvent.Payload with { Quantity = 9m },
        }, CancellationToken.None);
        var failedEvent = Assert.IsType<StockMovementPostingFailedIntegrationEvent>(Assert.Single(inventoryPublisher.Published));
        failedEvent = failedEvent with
        {
            Payload = failedEvent.Payload with { FailureMessage = new string('x', 650) },
        };

        await failedConsumer.HandleAsync(failedEvent, CancellationToken.None);
        await mesDb.SaveChangesAsync();

        var persistedReceipt = await mesDb.FinishedGoodsReceiptRequests.SingleAsync();
        Assert.Equal(FinishedGoodsReceiptRequest.InventoryPostingFailedStatus, persistedReceipt.Status);
        Assert.Null(persistedReceipt.PostedInventoryMovementId);
        Assert.Equal("IDEMPOTENCY_CONFLICT", persistedReceipt.InventoryPostingFailureCode);
        Assert.Equal(500, persistedReceipt.InventoryPostingFailureMessage?.Length);
        Assert.NotNull(persistedReceipt.InventoryPostingFailedAtUtc);
    }

    private static InventoryMovementRequestedIntegrationEvent CreateWarehouseSeedEvent(
        DateTimeOffset occurredAtUtc,
        string sourceDocumentId = "SEED-446",
        decimal quantity = 5m)
    {
        return new InventoryMovementRequestedIntegrationEvent(
            $"evt-mes-{sourceDocumentId}-warehouse-seed",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            occurredAtUtc,
            InventoryIntegrationEventSources.BusinessWms,
            $"seed-{sourceDocumentId}",
            $"seed-{sourceDocumentId}",
            "org-001",
            "env-dev",
            "system:test",
            $"seed:mes:{sourceDocumentId}:warehouse-line-side",
            new InventoryMovementRequestedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessWms,
                sourceDocumentId,
                $"{sourceDocumentId}-LINE",
                $"seed:mes:{sourceDocumentId}:warehouse-line-side",
                "MAT-OIL",
                "L",
                "warehouse",
                "line-side",
                "LOT-OIL-A",
                null,
                "Unrestricted",
                "production",
                null,
                quantity,
                occurredAtUtc));
    }

    private static StockMovementPostingFailedIntegrationEvent CreateFailedEvent(
        InventoryMovementRequestedIntegrationEvent requestedEvent,
        string eventId)
    {
        var payload = requestedEvent.Payload;
        return new StockMovementPostingFailedIntegrationEvent(
            eventId,
            InventoryIntegrationEventTypes.StockMovementPostingFailed,
            InventoryIntegrationEventVersions.V1,
            requestedEvent.OccurredAtUtc.AddSeconds(1),
            InventoryIntegrationEventSources.BusinessInventory,
            requestedEvent.CorrelationId,
            requestedEvent.EventId,
            requestedEvent.OrganizationId,
            requestedEvent.EnvironmentId,
            "system:business-inventory",
            $"inventory:stock-movement-posting-failed:{requestedEvent.OrganizationId}:{requestedEvent.EnvironmentId}:{payload.SourceService}:{payload.SourceDocumentId}:{payload.IdempotencyKey}",
            new StockMovementPostingFailedPayload(
                payload.MovementType,
                payload.SourceService,
                payload.SourceDocumentId,
                payload.SourceDocumentLineId,
                payload.IdempotencyKey,
                payload.SkuCode,
                payload.UomCode,
                payload.SiteCode,
                payload.LocationCode,
                payload.LotNo,
                payload.SerialNo,
                payload.QualityStatus,
                payload.OwnerType,
                payload.OwnerId,
                payload.Quantity,
                "NEGATIVE_ON_HAND",
                "Stock movement would make on-hand quantity negative.",
                requestedEvent.OccurredAtUtc.AddSeconds(1)));
    }

    private static InventoryMovementRequestedIntegrationEventHandlerForPostingMovement CreateInventoryHandler(
        InventoryDbContext inventoryDb,
        RecordingIntegrationEventPublisher publisher)
    {
        return new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new InventoryCommandExecutingSender(inventoryDb),
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);
    }

    private static InventoryReservationReleaseRequestedIntegrationEventHandlerForReleaseReservations CreateInventoryReservationReleaseHandler(
        InventoryDbContext inventoryDb)
    {
        return new InventoryReservationReleaseRequestedIntegrationEventHandlerForReleaseReservations(
            NullLogger<InventoryReservationReleaseRequestedIntegrationEventHandlerForReleaseReservations>.Instance,
            new InventoryCommandExecutingSender(inventoryDb),
            new InMemoryIntegrationEventDeadLetterStore());
    }

    private static void SeedMesWorkOrder(MesDbContext mesDb, string workOrderId = "WO-446", string skuId = "SKU-FG")
    {
        var now = DateTimeOffset.Parse("2026-06-18T07:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", workOrderId, skuId, "PV-001", 10m, 10, now.AddHours(8));
        workOrder.MarkReleased();
        workOrder.Start(now);
        mesDb.WorkOrders.Add(workOrder);
        mesDb.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            workOrderId,
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            now,
            TimeSpan.FromMinutes(30),
            null,
            null));
    }

    private static async Task RecordMesOutputLotAsync(
        MesDbContext mesDb,
        string workOrderId,
        string producedLotNo,
        decimal quantity,
        DateTimeOffset reportedAtUtc)
    {
        await new RecordProductionReportCommandHandler(mesDb).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                workOrderId,
                "OP-10",
                quantity,
                0m,
                false,
                reportedAtUtc,
                ProducedLotNo: producedLotNo),
            CancellationToken.None);
        await mesDb.SaveChangesAsync();
    }

    private static MesDbContext CreateMesContext()
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseInMemoryDatabase($"mes-inventory-line-side-{Guid.NewGuid():N}")
            .Options;
        return new MesDbContext(options, new NoopMediator());
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"mes-inventory-line-side-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private sealed class InventoryCommandExecutingSender(InventoryDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockMovementCommand command)
            {
                var result = await new PostStockMovementCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)result;
            }

            if (request is ReleaseStockReservationsBySourceCommand releaseCommand)
            {
                var result = await new ReleaseStockReservationsBySourceCommandHandler(
                    dbContext,
                    NullLogger<ReleaseStockReservationsBySourceCommandHandler>.Instance).Handle(releaseCommand, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)result;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request.GetType().FullName}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("This test sender only supports command requests with responses.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender only supports typed command requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot stream requests.");
        }
    }
}
