using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
using Nerv.IIP.Business.Inventory.Web.Application.Expiry;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Inventory.Web.Endpoints.Inventory;
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
using NetCorePal.Extensions.DistributedTransactions;
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
        if (deriveExpiryAtReceipt)
        {
            await wms.Database.MigrateAsync("20261127185605_ProjectErpReceiptInspection");
            await wms.Database.ExecuteSqlRawAsync("""
                INSERT INTO wms.outbound_orders
                    (id, organization_id, environment_id, outbound_order_no, source_document_type, source_document_id, site_code, status, created_at_utc, version)
                VALUES ('71c07d1a-be82-4bed-958c-857c809eb377', 'org-001', 'env-dev', 'OUT-ORDINARY', 'purchase-receipt-return',
                    'MANUAL-ORDINARY', 'SITE-01', 'Open', '2026-11-01T00:00:00Z', 1);
                INSERT INTO wms.outbound_order_lines
                    (id, outbound_order_id, line_no, sku_code, uom_code, requested_quantity, pick_location_code, lot_no, quality_status,
                     owner_type, issued_quantity, backorder_quantity, fulfillment_recorded)
                VALUES ('c6d2911e-8e76-4fc7-99dc-8482879e2830', '71c07d1a-be82-4bed-958c-857c809eb377', 'LINE-001', 'SKU-RETURN-PG-001',
                    'EA', 1, 'LOC-QA', 'LOT-ORDINARY', 'blocked', 'company', 0, 0, false);
                """);
        }
        await wms.Database.MigrateAsync();
        await inventory.Database.MigrateAsync();

        const string org = "org-001", env = "env-dev", sku = "SKU-RETURN-PG-001", site = "SITE-01", location = "LOC-QA";
        const string line = "LINE-001";
        var ct = CancellationToken.None;
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var production = new DateOnly(2026, 7, 1);
        var expiry = production.AddDays(3650);
        inventory.StockLocations.Add(StockLocation.CreateOrUpdate(null, org, env, location, "storage", site, null, "active"));
        await inventory.SaveChangesAsync(ct);

        if (deriveExpiryAtReceipt)
        {
            const string ordinaryLot = "LOT-ORDINARY";
            var ordinaryReceipt = InventoryMovementRequest.Create(org, env, "inbound", "IN-ORDINARY", line,
                "receive-ordinary", sku, "EA", site, location, ordinaryLot, null, "blocked", "company", null, 2m,
                ProductionDate: production, ExpiryDate: expiry, unitCost: 100m);
            await PostAndAcknowledgeAsync(ordinaryReceipt, null);
            var ordinary = await wms.OutboundOrders.Include(x => x.Lines).SingleAsync(ct);
            var legacyLine = Assert.Single(ordinary.Lines);
            Assert.Equal((null, null, 1m), (legacyLine.ProductionDate, legacyLine.ExpiryDate, legacyLine.RequestedQuantity));
            await new CreatePickingTaskCommandHandler(wms, new PostgresReservationClient(inventory)).Handle(
                new(ordinary.Id, "PICK-ORDINARY", line, location, "PACK-01", 1m), ct);
            await wms.SaveChangesAsync(ct);
            var ordinaryMovement = Assert.Single(ordinary.CompletePackReview("PACK-ORDINARY", true, "ship-ordinary", ordinary.Version));
            wms.InventoryMovementRequests.Add(ordinaryMovement);
            await wms.SaveChangesAsync(ct);
            await PostAndAcknowledgeAsync(ordinaryMovement, new ShelfLifePolicy());

            inventory.ChangeTracker.Clear();
            var ordinaryBalance = await inventory.StockLedgers.SingleAsync(x => x.LotNo == ordinaryLot, ct);
            Assert.Equal((production, expiry, 1m, 0m),
                (ordinaryBalance.ProductionDate, ordinaryBalance.ExpiryDate, ordinaryBalance.OnHandQuantity, ordinaryBalance.ReservedQuantity));
            Assert.Equal("Completed", ordinary.Status.ToString());
        }

        var inbound = InboundOrder.CreatePendingReceipt(org, env, "IN-BLOCKED", WmsSourceDocumentTypes.PurchaseOrder,
            "PO-RETURN-PG-001", site, [new(line, sku, "EA", 1m, location, "quality", "company", null)]);
        inbound.AssignWorkPool("RECEIVING", null, inbound.Version);
        inbound.StageErpReceipt(inbound.Version, [new(line, 1m)], new("submit", "submit-fingerprint"), 1m);
        await SeedUninvoicedPurchaseReceiptAsync(erp, inbound.ReceiptBatchId);
        var receipt = await erp.PurchaseReceipts.Include(x => x.Lines).SingleAsync(ct);
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

        await PostAndAcknowledgeAsync(inboundMovement, deriveExpiryAtReceipt ? new ShelfLifePolicy() : null);
        wms.ChangeTracker.Clear();
        await using (var replayTransaction = await wms.Database.BeginTransactionAsync(ct))
        {
            var complete = new CompleteInboundOrderCommandHandler(wms, null!);
            var originalCompletion = new CompleteInboundOrderCommand(
                inbound.Id, "post-blocked", [new(line, null, hasProductionDate ? production : null, null)],
                org, env, "receiver", [site], "site", site, ExpectedVersion: inbound.Version);
            var replay = await complete.Handle(originalCompletion, ct);
            Assert.Equal(inboundMovement.Id, replay.RequestId);
            Assert.NotNull(replay.InventoryMovementId);
            await Assert.ThrowsAsync<Nerv.IIP.Business.Wms.Web.Application.Errors.WmsIdempotencyConflictException>(
                () => complete.Handle(originalCompletion with { Lines = [new(line, null, production, expiry)] }, ct));
            await replayTransaction.CommitAsync(ct);
        }

        var reservationClient = new PostgresReservationClient(inventory);
        var outbound = await wms.OutboundOrders.Include(x => x.Lines).SingleAsync(x => x.OutboundOrderNo != "OUT-ORDINARY", ct);
        await new CreatePickingTaskCommandHandler(wms, reservationClient).Handle(
            new(outbound.Id, "PICK-BLOCKED", line, location, "PACK-01", 1m), ct);
        await wms.SaveChangesAsync(ct);
        var reservation = await inventory.StockReservations.SingleAsync(x => x.SourceDocumentId == outbound.OutboundOrderNo, ct);
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
            var retriedReservation = await inventory.StockReservations.SingleAsync(x => x.SourceDocumentId == outbound.OutboundOrderNo && x.Id != reservation.Id, ct);
            Assert.Equal(originalDates, (retriedReservation.ProductionDate, retriedReservation.ExpiryDate));
        }
        await PostAndAcknowledgeAsync(outboundMovement, new ShelfLifePolicy());

        if (deriveExpiryAtReceipt)
        {
            var dates = otherDates[0];
            var reserve = new ReserveStockCommandHandler(inventory);
            var request = new ReserveStockCommand(org, env, "wms", "OTHER-RESERVATION", line,
                "release-by-source", sku, "EA", site, location, null, null, "blocked", "company", null, 1m,
                dates.Production, dates.Expiry);
            var reserved = await reserve.Handle(request, ct);
            await inventory.SaveChangesAsync(ct);
            Assert.Equal(reserved.ReservationId, (await reserve.Handle(request, ct)).ReservationId);
            var release = await new ReleaseStockReservationsBySourceCommandHandler(inventory,
                NullLogger<ReleaseStockReservationsBySourceCommandHandler>.Instance)
                .Handle(new(org, env, "wms", request.SourceDocumentId, []), ct);
            await inventory.SaveChangesAsync(ct);
            Assert.Equal((1, 1m), (release.ReleasedReservationCount, release.ReleasedQuantity));

            var expiring = await reserve.Handle(request with { IdempotencyKey = "expire-reservation" }, ct);
            await inventory.SaveChangesAsync(ct);
            var expiresAt = await inventory.StockReservations.Where(x => x.Id == expiring.ReservationId)
                .Select(x => x.ExpiresAtUtc).SingleAsync(ct);
            var expired = await new ExpiredStockReservationService(inventory, Options.Create(new StockReservationExpirationOptions()))
                .ExpireOpenReservationsAsync(expiresAt, ct);
            Assert.Equal(1, expired);
        }

        inventory.ChangeTracker.Clear();
        var balances = await inventory.StockLedgers.Where(x => x.LotNo == null).Select(x => new { x.ProductionDate, x.ExpiryDate, x.OnHandQuantity, x.ReservedQuantity }).ToListAsync(ct);
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
            var request = new InventoryMovementRequestCreatedIntegrationEventConverter()
                .Convert(new InventoryMovementRequestCreatedDomainEvent(movement));
            request = JsonSerializer.Deserialize<InventoryMovementRequestedIntegrationEvent>(JsonSerializer.Serialize(request))!;
            var handler = new PostStockMovementCommandHandler(inventory, expiryPolicy);
            var inventorySender = new NoopMediator(post: handler.Handle);
            var consumer = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
                NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
                inventorySender, deadLetters, new UnexpectedPostingFailurePublisher());
            await consumer.HandleAsync(request, ct);
            await inventory.SaveChangesAsync(ct);
            await consumer.HandleAsync(request, ct);
            await inventory.SaveChangesAsync(ct);
            var posted = await inventory.StockMovements.SingleAsync(x => x.SourceDocumentId == movement.SourceDocumentId
                && x.IdempotencyKey == movement.IdempotencyKey, ct);
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

    private sealed class UnexpectedPostingFailurePublisher : IIntegrationEventPublisher
    {
        Task IIntegrationEventPublisher.PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Inventory rejected the test movement.");
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
            var wireRequest = JsonSerializer.SerializeToElement(request, JsonSerializerOptions.Web)
                .Deserialize<ReserveStockRequest>(JsonSerializerOptions.Web)!;
            var result = await new ReserveStockCommandHandler(inventory).Handle(new(
                request.OrganizationId, request.EnvironmentId, request.SourceService, request.SourceDocumentId, request.SourceDocumentLineId,
                request.IdempotencyKey, request.SkuCode, request.UomCode, request.SiteCode, request.LocationCode, request.LotNo,
                request.SerialNo, request.QualityStatus, request.OwnerType, request.OwnerId, request.Quantity,
                wireRequest.ProductionDate, wireRequest.ExpiryDate, MatchReceiptStockDates: wireRequest.MatchReceiptStockDates), ct);
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
