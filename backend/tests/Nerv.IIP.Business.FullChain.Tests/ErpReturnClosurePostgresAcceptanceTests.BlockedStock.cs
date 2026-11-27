using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Inventory.Domain;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLocationAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Domain.DomainEvents;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Application.MasterData;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InboundOrderAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.InventoryMovementRequestAggregate;
using Nerv.IIP.Business.Wms.Domain.DomainEvents;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Wms.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Wms;
using Nerv.IIP.Messaging.CAP;
using Nerv.IIP.Testing.PostgreSql;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using WmsOutboundOrderCompletedDomainEvent = Nerv.IIP.Business.Wms.Domain.DomainEvents.OutboundOrderCompletedDomainEvent;

namespace Nerv.IIP.Business.FullChain.Tests;

public sealed partial class ErpReturnClosurePostgresAcceptanceTests
{
    private static async Task AssertBlockedReturnAsync(
        string baseConnectionString, bool hasProductionDate, bool deriveExpiryAtReceipt)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync(baseConnectionString, "nerv_blocked_return");
        await using var erp = CreateErpContext(database.ConnectionString);
        await using var wms = CreateWmsContext(database.ConnectionString);
        var inventoryOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(database.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryFacts.Schema))
            .Options;
        await using var inventory = new InventoryDbContext(inventoryOptions, new NoopMediator());
        await erp.Database.MigrateAsync();
        await wms.Database.MigrateAsync();
        await inventory.Database.MigrateAsync();

        const string org = "org-001", env = "env-dev", sku = "SKU-RETURN-PG-001", site = "SITE-01", location = "LOC-QA";
        const string line = "LINE-001";
        var ct = CancellationToken.None;
        var production = new DateOnly(2026, 7, 1);
        var expiry = production.AddDays(3650);
        inventory.StockLocations.Add(StockLocation.CreateOrUpdate(null, org, env, location, "storage", site, null, "active"));
        await inventory.SaveChangesAsync(ct);

        var inbound = InboundOrder.CreatePendingReceipt(org, env, "IN-BLOCKED", WmsSourceDocumentTypes.PurchaseOrder,
            "PO-RETURN-PG-001", site, [new(line, sku, "EA", 1m, location, "quality", "company", null)]);
        inbound.StageErpReceipt(inbound.Version, [new(line, 1m)], new("submit", "submit-fingerprint"), 1m);
        await SeedUninvoicedPurchaseReceiptAsync(erp, inbound.ReceiptBatchId);
        var receipt = await erp.PurchaseReceipts.Include(x => x.Lines).SingleAsync(ct);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var erpCoding = new ErpCodingService();
        await new PurchaseReceiptRecordedIntegrationEventHandlerForPostGrIrAccrual(erp, deadLetters, erpCoding)
            .HandleAsync(new PurchaseReceiptRecordedIntegrationEventConverter().Convert(new PurchaseReceiptRecordedDomainEvent(receipt)), ct);
        await erp.SaveChangesAsync(ct);

        inbound.BindErpPurchaseReceipt(inbound.ReceiptBatchId!.Value, receipt.PurchaseReceiptNo);
        inbound.ProjectErpReceiptInspection(line, "QI-BLOCKED", "rejected", 1m, null);
        var inboundMovement = Assert.Single(inbound.Complete("post-blocked", inbound.Version,
            [new(line, null, hasProductionDate ? production : null, null)],
            unitCostsByLine: new Dictionary<string, decimal> { [line] = 100m }));
        wms.InboundOrders.Add(inbound);
        wms.InventoryMovementRequests.Add(inboundMovement);
        await wms.SaveChangesAsync(ct);

        await PostAndAcknowledgeAsync(inboundMovement, deriveExpiryAtReceipt ? new ShelfLifePolicy() : null);
        var originalDates = (Production: hasProductionDate ? production : (DateOnly?)null,
            Expiry: deriveExpiryAtReceipt ? expiry : (DateOnly?)null);
        var otherDates = new (DateOnly? Production, DateOnly? Expiry)[]
        {
            (originalDates.Production, expiry.AddDays(1)),
            (production.AddDays(-1), originalDates.Expiry),
        };
        var postOther = new PostStockMovementCommandHandler(inventory);
        for (var index = 0; index < otherDates.Length; index++)
        {
            var dates = otherDates[index];
            await postOther.Handle(new PostStockMovementCommand(
                org, env, "inbound", "wms", $"OTHER-RECEIPT-{index}", line, $"other-receipt-{index}", sku, "EA",
                site, location, null, null, "blocked", "company", null, 1m, 100m,
                ProductionDate: dates.Production, ExpiryDate: dates.Expiry), ct);
        }
        await inventory.SaveChangesAsync(ct);
        inventory.ChangeTracker.Clear();
        wms.ChangeTracker.Clear();

        var reservationClient = new PostgresReservationClient(inventory);
        var outbound = await wms.OutboundOrders.Include(x => x.Lines).SingleAsync(ct);
        await new CreatePickingTaskCommandHandler(wms, reservationClient).Handle(
            new(outbound.Id, "PICK-BLOCKED", line, location, "PACK-01", 1m), ct);
        await wms.SaveChangesAsync(ct);
        var reservation = await inventory.StockReservations.SingleAsync(ct);
        Assert.Equal(originalDates, (reservation.ProductionDate, reservation.ExpiryDate));

        var outboundMovement = Assert.Single(outbound.CompletePackReview("PACK-BLOCKED", true, "ship-blocked", outbound.Version));
        wms.InventoryMovementRequests.Add(outboundMovement);
        await wms.SaveChangesAsync(ct);
        Assert.Equal(originalDates, (outboundMovement.ProductionDate, outboundMovement.ExpiryDate));
        if (deriveExpiryAtReceipt)
        {
            await new MarkInventoryMovementRequestFailedCommandHandler(wms, reservationClient).Handle(new(
                org, env, outboundMovement.MovementType, outboundMovement.SourceDocumentId, outboundMovement.SourceDocumentLineId,
                outboundMovement.IdempotencyKey, "posting-rejected", "Fixture rejects first outbound attempt"), ct);
            await wms.SaveChangesAsync(ct);
            var retry = await new RetryOutboundInventoryPostingCommandHandler(wms, reservationClient)
                .Handle(new(outbound.Id, "ship-blocked-retry"), ct);
            await wms.SaveChangesAsync(ct);
            outboundMovement = await wms.InventoryMovementRequests.SingleAsync(x => x.Id == retry.RequestId, ct);
            Assert.Equal(originalDates, (outboundMovement.ProductionDate, outboundMovement.ExpiryDate));
            var retriedReservation = await inventory.StockReservations.SingleAsync(x => x.Id != reservation.Id, ct);
            Assert.Equal(originalDates, (retriedReservation.ProductionDate, retriedReservation.ExpiryDate));
        }
        await PostAndAcknowledgeAsync(outboundMovement, new ShelfLifePolicy());

        inventory.ChangeTracker.Clear();
        var balances = await inventory.StockLedgers.Select(x => new { x.ProductionDate, x.ExpiryDate, x.OnHandQuantity, x.ReservedQuantity }).ToListAsync(ct);
        Assert.Equal(3, balances.Count);
        var original = Assert.Single(balances, x => (x.ProductionDate, x.ExpiryDate) == originalDates);
        Assert.Equal(0m, original.OnHandQuantity);
        Assert.Equal(0m, original.ReservedQuantity);
        foreach (var dates in otherDates)
        {
            var other = Assert.Single(balances, x => (x.ProductionDate, x.ExpiryDate) == dates);
            Assert.Equal(1m, other.OnHandQuantity);
            Assert.Equal(0m, other.ReservedQuantity);
        }

        Assert.Equal("Completed", outbound.Status.ToString());
        var shipment = new OutboundOrderCompletedIntegrationEventConverter()
            .Convert(new WmsOutboundOrderCompletedDomainEvent(outbound));
        var returnHandler = new WmsOutboundOrderCompletedIntegrationEventHandlerForRecordPurchaseReturn(erp, deadLetters, erpCoding);
        await returnHandler.HandleAsync(shipment, ct);
        await erp.SaveChangesAsync(ct);
        await returnHandler.HandleAsync(shipment, ct);
        await erp.SaveChangesAsync(ct);
        var returned = await erp.PurchaseReturns.SingleAsync(ct);
        Assert.Equal(receipt.PurchaseReceiptNo, returned.PurchaseReceiptNo);
        Assert.Equal(100m, returned.GrIrReversalAmount);
        var vouchers = await erp.JournalVouchers.Include(x => x.Lines).ToListAsync(ct);
        Assert.Contains(vouchers.Single(x => x.SourceNo == receipt.PurchaseReceiptNo).Lines,
            x => x.AccountCode == "GR-IR" && x.CreditAmount == 100m);
        Assert.Contains(vouchers.Single(x => x.SourceNo == returned.PurchaseReturnNo).Lines,
            x => x.AccountCode == "GR-IR" && x.DebitAmount == 100m);
        Assert.Empty(await deadLetters.ListAsync(null, IntegrationEventDeadLetterStatus.Pending, ct));

        async Task PostAndAcknowledgeAsync(InventoryMovementRequest movement, IInventorySkuExpiryPolicyProvider? expiryPolicy)
        {
            var payload = new InventoryMovementRequestCreatedIntegrationEventConverter()
                .Convert(new InventoryMovementRequestCreatedDomainEvent(movement)).Payload;
            var posting = await new PostStockMovementCommandHandler(inventory, expiryPolicy).Handle(new(
                org, env, payload.MovementType, payload.SourceService, payload.SourceDocumentId, payload.SourceDocumentLineId,
                payload.IdempotencyKey, payload.SkuCode, payload.UomCode, payload.SiteCode, payload.LocationCode, payload.LotNo,
                payload.SerialNo, payload.QualityStatus, payload.OwnerType, payload.OwnerId, payload.Quantity, payload.UnitCost,
                payload.InventoryReservationId is { } id ? new StockReservationId(Guid.Parse(id)) : null,
                payload.ProductionDate, payload.ExpiryDate), ct);
            await inventory.SaveChangesAsync(ct);
            var posted = await inventory.StockMovements.SingleAsync(x => x.Id == posting.MovementId, ct);
            var message = new StockMovementPostedIntegrationEventConverter(new HttpInventoryIntegrationEventContextAccessor(new HttpContextAccessor()))
                .Convert(new StockMovementPostedDomainEvent(posted));
            await using var transaction = await wms.Database.BeginTransactionAsync(ct);
            var sender = new NoopMediator((command, token) =>
                new MarkInventoryMovementRequestPostedCommandHandler(wms).Handle(command, token));
            await new StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted(sender, deadLetters).HandleAsync(message, ct);
            await wms.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
    }

    private sealed class ShelfLifePolicy : IInventorySkuExpiryPolicyProvider
    {
        public Task<InventorySkuExpiryPolicy?> GetAsync(string organizationId, string environmentId, string skuCode,
            CancellationToken cancellationToken) => Task.FromResult<InventorySkuExpiryPolicy?>(new(3650, null));
    }

    private sealed class PostgresReservationClient(InventoryDbContext inventory) : IWmsInventoryReservationClient
    {
        public async Task<WmsInventoryReservationResult> ReserveAsync(WmsInventoryReservationRequest request, CancellationToken ct)
        {
            var result = await new ReserveStockCommandHandler(inventory).Handle(new(
                request.OrganizationId, request.EnvironmentId, request.SourceService, request.SourceDocumentId, request.SourceDocumentLineId,
                request.IdempotencyKey, request.SkuCode, request.UomCode, request.SiteCode, request.LocationCode, request.LotNo,
                request.SerialNo, request.QualityStatus, request.OwnerType, request.OwnerId, request.Quantity,
                request.ProductionDate, request.ExpiryDate), ct);
            await inventory.SaveChangesAsync(ct);
            return new(result.ReservationId.ToString(), result.ReservedQuantity, result.AvailableQuantity,
                result.LotNo, result.ProductionDate, result.ExpiryDate);
        }

        public Task<WmsInventoryFefoReservationResult> ReserveFefoAsync(WmsInventoryFefoReservationRequest request, CancellationToken ct) => throw new NotSupportedException();
        public async Task<WmsInventoryReservationReleaseResult> ReleaseAsync(WmsInventoryReservationReleaseRequest request, CancellationToken ct)
        {
            var result = await new ReleaseStockReservationCommandHandler(inventory)
                .Handle(new(new StockReservationId(Guid.Parse(request.ReservationId)), request.Quantity), ct);
            await inventory.SaveChangesAsync(ct);
            return new(request.ReservationId, result.OpenQuantity, result.AvailableQuantity);
        }
        public Task<WmsInventoryReservationRenewalResult> RenewAsync(WmsInventoryReservationRenewalRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<WmsInventoryReservationPickedResult> MarkPickedAsync(WmsInventoryReservationPickedRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<WmsInventoryCountTaskResult> CreateCountTaskAsync(WmsInventoryCountTaskRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<WmsInventoryCountAdjustmentResult> ConfirmCountAdjustmentAsync(WmsInventoryCountAdjustmentRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
}
