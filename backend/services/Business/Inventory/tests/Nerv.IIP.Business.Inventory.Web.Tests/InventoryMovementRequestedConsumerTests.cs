using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockReservationAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class InventoryMovementRequestedConsumerTests
{
    [Fact]
    public async Task Movement_requested_consumer_executes_post_stock_movement_command()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());

        await handler.HandleAsync(CreateRequestedEvent("evt-001"), CancellationToken.None);

        var movement = Assert.Single(dbContext.StockMovements);
        Assert.Equal("wms", movement.SourceService);
        Assert.Equal("IN-001", movement.SourceDocumentId);
        Assert.Equal("idem-in-001", movement.IdempotencyKey);
        Assert.Equal(5m, movement.Quantity);
    }

    [Fact]
    public async Task Movement_requested_consumer_applies_payload_unit_cost_to_inbound_valuation()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());

        await handler.HandleAsync(CreateRequestedEvent("evt-unit-cost") with
        {
            Payload = CreateRequestedEvent("evt-unit-cost").Payload with
            {
                IdempotencyKey = "idem-in-unit-cost-001",
                UnitCost = 12.34m,
            },
        }, CancellationToken.None);

        var movement = Assert.Single(dbContext.StockMovements);
        var ledger = Assert.Single(dbContext.StockLedgers);
        Assert.Equal(12.34m, movement.UnitCost);
        Assert.Equal(61.70m, movement.MovementAmount);
        Assert.Equal(12.34m, ledger.MovingAverageUnitCost);
        Assert.Equal(61.70m, ledger.InventoryValue);
    }

    [Fact]
    public async Task Movement_requested_consumer_keeps_weighted_moving_average_for_multiple_unit_cost_receipts()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());

        await handler.HandleAsync(CreateRequestedEvent("evt-weighted-001") with
        {
            Payload = CreateRequestedEvent("evt-weighted-001").Payload with
            {
                SourceDocumentId = "IN-WEIGHTED-001",
                IdempotencyKey = "idem-in-weighted-001",
                UnitCost = 12.34m,
            },
        }, CancellationToken.None);
        await handler.HandleAsync(CreateRequestedEvent("evt-weighted-002") with
        {
            Payload = CreateRequestedEvent("evt-weighted-002").Payload with
            {
                SourceDocumentId = "IN-WEIGHTED-002",
                IdempotencyKey = "idem-in-weighted-002",
                UnitCost = 20m,
            },
        }, CancellationToken.None);

        var ledger = Assert.Single(dbContext.StockLedgers);
        Assert.Equal(10m, ledger.OnHandQuantity);
        Assert.Equal(161.70m, ledger.InventoryValue);
        Assert.Equal(16.17m, ledger.MovingAverageUnitCost);
        Assert.Contains(dbContext.StockMovements, x => x.SourceDocumentId == "IN-WEIGHTED-001" && x.MovementAmount == 61.70m);
        Assert.Contains(dbContext.StockMovements, x => x.SourceDocumentId == "IN-WEIGHTED-002" && x.MovementAmount == 100m);
    }

    [Fact]
    public async Task Movement_requested_consumer_does_not_dilute_existing_average_when_inbound_unit_cost_is_missing()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());

        await handler.HandleAsync(CreateRequestedEvent("evt-costed-001") with
        {
            Payload = CreateRequestedEvent("evt-costed-001").Payload with
            {
                SourceDocumentId = "IN-COSTED-001",
                IdempotencyKey = "idem-in-costed-001",
                UnitCost = 12.34m,
            },
        }, CancellationToken.None);
        await handler.HandleAsync(CreateRequestedEvent("evt-legacy-null-cost") with
        {
            Payload = CreateRequestedEvent("evt-legacy-null-cost").Payload with
            {
                SourceDocumentId = "IN-LEGACY-NULL-COST",
                IdempotencyKey = "idem-in-legacy-null-cost",
                UnitCost = null,
            },
        }, CancellationToken.None);

        var ledger = Assert.Single(dbContext.StockLedgers);
        var legacyMovement = dbContext.StockMovements.Single(x => x.SourceDocumentId == "IN-LEGACY-NULL-COST");
        Assert.Null(legacyMovement.UnitCost);
        Assert.Equal(61.70m, legacyMovement.MovementAmount);
        Assert.Equal(10m, ledger.OnHandQuantity);
        Assert.Equal(123.40m, ledger.InventoryValue);
        Assert.Equal(12.34m, ledger.MovingAverageUnitCost);
    }

    [Fact]
    public async Task Duplicate_movement_requested_event_uses_inventory_command_idempotency()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());
        var integrationEvent = CreateRequestedEvent("evt-duplicate");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Single(dbContext.StockMovements);
        Assert.Single(dbContext.StockLedgers);
        Assert.Equal(5m, dbContext.StockLedgers.Single().OnHandQuantity);
    }

    [Fact]
    public async Task Movement_requested_consumer_allocates_inventory_reservation_when_supplied()
    {
        await using var dbContext = CreateContext();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-SEED",
            "LINE-001",
            "idem-seed",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            5m));
        var reservation = StockReservation.Reserve(ledger, "wms", "OUT-001", "LINE-001", "idem-res-001", 4m);
        ledger.Reserve(reservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.Add(reservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sender = new CommandExecutingSender(dbContext);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());

        await handler.HandleAsync(CreateRequestedEvent("evt-reserved-outbound") with
        {
            Payload = CreateRequestedEvent("evt-reserved-outbound").Payload with
            {
                MovementType = "outbound",
                SourceDocumentId = "OUT-001",
                IdempotencyKey = "idem-out-001",
                Quantity = -4m,
                InventoryReservationId = reservation.Id.ToString(),
            },
        }, CancellationToken.None);

        Assert.Equal(1m, ledger.OnHandQuantity);
        Assert.Equal(0m, ledger.ReservedQuantity);
        Assert.Equal(0m, reservation.OpenQuantity);
        Assert.Equal("allocated", reservation.Status);
    }

    [Fact]
    public async Task Movement_requested_consumer_executes_status_transfer_request_from_blocked_to_restricted()
    {
        await using var dbContext = CreateContext();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            StockQualityStatus.Blocked,
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "quality",
            "NCR-SEED",
            "LINE-001",
            "idem-blocked-seed",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            StockQualityStatus.Blocked,
            "company",
            "owner-001",
            5m));
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());

        await handler.HandleAsync(CreateRequestedEvent("evt-status-transfer") with
        {
            SourceService = InventoryIntegrationEventSources.BusinessQuality,
            Payload = CreateRequestedEvent("evt-status-transfer").Payload with
            {
                MovementType = InventoryMovementRequestTypes.StatusTransfer,
                SourceService = InventoryMovementSourceServices.Quality,
                SourceDocumentId = "NCR-001",
                SourceDocumentLineId = "NCR-CODE-001",
                IdempotencyKey = "quality:ncr-inventory-disposition:org-001:env-dev:NCR-CODE-001:rework",
                QualityStatus = StockQualityStatus.Blocked,
                Quantity = 3m,
                TargetQualityStatus = StockQualityStatus.Restricted,
            },
        }, CancellationToken.None);

        Assert.Equal(2m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Blocked).OnHandQuantity);
        Assert.Equal(3m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Restricted).OnHandQuantity);
        Assert.Equal(2, dbContext.StockMovements.Count(x => x.MovementType.StartsWith(InventoryMovementRequestTypes.StatusTransfer, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Movement_requested_consumer_publishes_posting_failed_when_unreserved_outbound_would_pierce_reserved_stock()
    {
        await using var dbContext = CreateContext();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-SEED",
            "LINE-001",
            "idem-seed",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            10m));
        var reservation = StockReservation.Reserve(ledger, "mes", "WO-001", "LINE-001", "idem-res-001", 8m);
        ledger.Reserve(reservation);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockReservations.Add(reservation);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sender = new CommandExecutingSender(dbContext);
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);

        await handler.HandleAsync(CreateRequestedEvent("evt-reserved-pierce") with
        {
            Payload = CreateRequestedEvent("evt-reserved-pierce").Payload with
            {
                MovementType = "outbound",
                SourceDocumentId = "OUT-RESERVED-001",
                IdempotencyKey = "idem-out-reserved-001",
                Quantity = -3m,
                InventoryReservationId = null,
            },
        }, CancellationToken.None);

        var failedEvent = Assert.IsType<StockMovementPostingFailedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(InventoryIntegrationEventTypes.StockMovementPostingFailed, failedEvent.EventType);
        Assert.Equal(InventoryPostingFailureCodes.ReservationAllocationRejected, failedEvent.Payload.FailureCode);
        Assert.DoesNotContain("reserved", failedEvent.Payload.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10m, ledger.OnHandQuantity);
        Assert.Equal(8m, ledger.ReservedQuantity);
        Assert.Equal(2m, ledger.AvailableQuantity);
        Assert.DoesNotContain(dbContext.StockMovements, x => x.SourceDocumentId == "OUT-RESERVED-001");
    }

    [Theory]
    [InlineData(QualityIntegrationEventTypes.InspectionPassed, StockQualityStatus.Unrestricted)]
    [InlineData(QualityIntegrationEventTypes.InspectionRejected, StockQualityStatus.Blocked)]
    [InlineData(QualityIntegrationEventTypes.InspectionConditionalReleased, StockQualityStatus.Restricted)]
    public async Task Quality_inspection_result_consumer_transfers_quality_stock(string eventType, string targetStatus)
    {
        await using var dbContext = CreateContext();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            StockQualityStatus.Quality,
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-001",
            "LINE-001",
            "idem-quality-in-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            StockQualityStatus.Quality,
            "company",
            "owner-001",
            5m,
            2m));
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sender = new CommandExecutingSender(dbContext);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            sender,
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateInspectionEvent(eventType), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Contains(dbContext.StockLedgers, x => x.QualityStatus == targetStatus && x.OnHandQuantity == 3m);
        Assert.Contains(dbContext.StockLedgers, x => x.QualityStatus == StockQualityStatus.Quality && x.OnHandQuantity == 2m);
        Assert.Equal(2, dbContext.StockMovements.Count(x => x.MovementType.StartsWith("status-transfer")));
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_uses_stock_release_dimensions_when_supplied()
    {
        await using var dbContext = CreateContext();
        var firstLedger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "quality",
            "company",
            "owner-001");
        firstLedger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-001",
            "LINE-001",
            "idem-quality-in-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "quality",
            "company",
            "owner-001",
            5m,
            2m));
        var selectedLedger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-B-02",
            "LOT-002",
            null,
            "quality",
            "company",
            "owner-001");
        selectedLedger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-002",
            "LINE-001",
            "idem-quality-in-002",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-B-02",
            "LOT-002",
            null,
            "quality",
            "company",
            "owner-001",
            5m,
            2m));
        dbContext.StockLedgers.AddRange(firstLedger, selectedLedger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sender = new CommandExecutingSender(dbContext);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            sender,
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(
            CreateInspectionEvent(
                QualityIntegrationEventTypes.InspectionRejected,
                new StockReleaseDimensionPayload(
                    "kg",
                    "SITE-01",
                    "LOC-B-02",
                    "LOT-002",
                    null,
                    StockQualityStatus.Quality,
                    "company",
                    "owner-001",
                    QualityStockReleaseTargetStatuses.Blocked)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(5m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Quality && x.LocationCode == "LOC-A-01").OnHandQuantity);
        Assert.Equal(2m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Quality && x.LocationCode == "LOC-B-02").OnHandQuantity);
        Assert.Equal(3m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Blocked && x.LocationCode == "LOC-B-02").OnHandQuantity);
        Assert.Equal(2, dbContext.StockMovements.Count(x => x.MovementType.StartsWith("status-transfer")));
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_normalizes_optional_stock_release_locator_blanks()
    {
        await using var dbContext = CreateContext();
        var targetLedger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-B-02",
            null,
            null,
            StockQualityStatus.Quality,
            "company",
            null);
        targetLedger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-NO-LOT",
            "LINE-001",
            "idem-quality-in-no-lot",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-B-02",
            null,
            null,
            StockQualityStatus.Quality,
            "company",
            null,
            5m,
            2m));
        dbContext.StockLedgers.Add(targetLedger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            new CommandExecutingSender(dbContext),
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(
            CreateInspectionEvent(
                QualityIntegrationEventTypes.InspectionPassed,
                new StockReleaseDimensionPayload(
                    "kg",
                    "SITE-01",
                    "LOC-B-02",
                    "   ",
                    "",
                    StockQualityStatus.Quality,
                    "company",
                    " ")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(2m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Quality).OnHandQuantity);
        var unrestrictedLedger = dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Unrestricted);
        Assert.Null(unrestrictedLedger.LotNo);
        Assert.Null(unrestrictedLedger.SerialNo);
        Assert.Null(unrestrictedLedger.OwnerId);
        Assert.Equal(3m, unrestrictedLedger.OnHandQuantity);
        Assert.Equal(2, dbContext.StockMovements.Count(x => x.MovementType.StartsWith("status-transfer")));
    }

    [Theory]
    [InlineData(QualityIntegrationEventTypes.InspectionPassed, StockQualityStatus.Unrestricted)]
    [InlineData(QualityIntegrationEventTypes.InspectionRejected, StockQualityStatus.Blocked)]
    public async Task Quality_inspection_result_consumer_uses_payload_stock_locator_dimensions_for_multi_lot_release(
        string eventType,
        string targetStatus)
    {
        await using var dbContext = CreateContext();
        var untouchedLedger = CreateQualityLedger("LOC-A-01", "LOT-001", 5m);
        var targetLedger = CreateQualityLedger("LOC-B-02", "LOT-002", 5m);
        dbContext.StockLedgers.AddRange(untouchedLedger, targetLedger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            new CommandExecutingSender(dbContext),
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateInspectionEventWithPayloadStockLocator(eventType), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(5m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Quality && x.LocationCode == "LOC-A-01").OnHandQuantity);
        Assert.Equal(2m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Quality && x.LocationCode == "LOC-B-02").OnHandQuantity);
        Assert.Equal(3m, dbContext.StockLedgers.Single(x => x.QualityStatus == targetStatus && x.LocationCode == "LOC-B-02").OnHandQuantity);
        Assert.Equal(2, dbContext.StockMovements.Count(x => x.MovementType.StartsWith("status-transfer")));
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_rejects_ambiguous_legacy_event_without_stock_locator()
    {
        await using var dbContext = CreateContext();
        dbContext.StockLedgers.AddRange(
            CreateQualityLedger("LOC-A-01", "LOT-001", 5m),
            CreateQualityLedger("LOC-B-02", "LOT-002", 5m));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            new CommandExecutingSender(dbContext),
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.HandleAsync(
            CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed),
            CancellationToken.None));

        Assert.Contains("exactly one matching quality stock ledger", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.StockMovements);
        Assert.Equal(5m, dbContext.StockLedgers.Single(x => x.LocationCode == "LOC-A-01").OnHandQuantity);
        Assert.Equal(5m, dbContext.StockLedgers.Single(x => x.LocationCode == "LOC-B-02").OnHandQuantity);
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_accepts_matching_stock_release_target_status()
    {
        await using var dbContext = CreateContext();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-B-02",
            "LOT-002",
            null,
            StockQualityStatus.Quality,
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-002",
            "LINE-001",
            "idem-quality-in-002",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-B-02",
            "LOT-002",
            null,
            StockQualityStatus.Quality,
            "company",
            "owner-001",
            5m,
            2m));
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            new CommandExecutingSender(dbContext),
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(
            CreateInspectionEvent(
                QualityIntegrationEventTypes.InspectionConditionalReleased,
                new StockReleaseDimensionPayload(
                    "kg",
                    "SITE-01",
                    "LOC-B-02",
                    "LOT-002",
                    null,
                    StockQualityStatus.Quality,
                    "company",
                    "owner-001",
                    QualityStockReleaseTargetStatuses.Restricted)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(2m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Quality).OnHandQuantity);
        Assert.Equal(3m, dbContext.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Restricted).OnHandQuantity);
        Assert.Equal(2, dbContext.StockMovements.Count(x => x.MovementType.StartsWith("status-transfer")));
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_rejects_stock_release_target_mismatch()
    {
        await using var dbContext = CreateContext();
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            new CommandExecutingSender(dbContext),
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.HandleAsync(
            CreateInspectionEvent(
                QualityIntegrationEventTypes.InspectionPassed,
                new StockReleaseDimensionPayload(
                    "kg",
                    "SITE-01",
                    "LOC-B-02",
                    "LOT-002",
                    null,
                    StockQualityStatus.Quality,
                    "company",
                    "owner-001",
                    QualityStockReleaseTargetStatuses.Blocked)),
            CancellationToken.None));

        Assert.Contains("target status", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.StockMovements);
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_rejects_stock_release_from_non_quality_status()
    {
        await using var dbContext = CreateContext();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "unrestricted",
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-001",
            "LINE-001",
            "idem-unrestricted-in-001",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "unrestricted",
            "company",
            "owner-001",
            5m,
            2m));
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sender = new CommandExecutingSender(dbContext);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            sender,
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        var exception = await Assert.ThrowsAsync<KnownException>(() => handler.HandleAsync(
            CreateInspectionEvent(
                QualityIntegrationEventTypes.InspectionPassed,
                new StockReleaseDimensionPayload(
                    "kg",
                    "SITE-01",
                    "LOC-A-01",
                    "LOT-001",
                    null,
                    "unrestricted",
                    "company",
                    "owner-001")),
            CancellationToken.None));

        Assert.Contains("quality status", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.StockMovements);
        Assert.Single(dbContext.StockLedgers);
        Assert.Equal(5m, ledger.OnHandQuantity);
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_rejects_unsupported_event_type_to_dead_letter_store()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            sender,
            dbContext,
            deadLetterStore);

        await handler.HandleAsync(CreateInspectionEvent("quality.UnknownInspectionResult"), CancellationToken.None);

        Assert.Empty(dbContext.StockMovements);
        var deadLetters = await deadLetterStore.ListAsync(
            QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None);
        var deadLetter = Assert.Single(deadLetters);
        Assert.Equal("unexpected-event-type", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Quality_inspection_result_consumer_returns_before_candidate_lookup_when_status_transfer_was_already_processed()
    {
        await using var dbContext = CreateContext();
        dbContext.StockMovements.Add(StockMovement.Post(
            "org-001",
            "env-dev",
            "status-transfer-out",
            "quality",
            "QI-001",
            "QI-001",
            "quality:inspection-result:org-001:env-dev:QI-001:quality.InspectionPassed:out",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "quality",
            "company",
            "owner-001",
            -3m));
        dbContext.StockMovements.Add(StockMovement.Post(
            "org-001",
            "env-dev",
            "status-transfer-in",
            "quality",
            "QI-001",
            "QI-001",
            "quality:inspection-result:org-001:env-dev:QI-001:quality.InspectionPassed:in",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "unrestricted",
            "company",
            "owner-001",
            3m));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var sender = new CommandExecutingSender(dbContext);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            sender,
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed), CancellationToken.None);

        Assert.Equal(2, dbContext.StockMovements.Count());
    }

    [Fact]
    public async Task Movement_requested_consumer_publishes_posting_failed_event_for_business_rejection()
    {
        await using var dbContext = CreateContext();
        var sender = new CommandExecutingSender(dbContext);
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            sender,
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);

        await handler.HandleAsync(CreateRequestedEvent("evt-negative") with
        {
            Payload = CreateRequestedEvent("evt-negative").Payload with
            {
                MovementType = "outbound",
                SourceDocumentId = "OUT-001",
                IdempotencyKey = "idem-out-001",
                Quantity = -5m,
            },
        }, CancellationToken.None);

        var failedEvent = Assert.IsType<StockMovementPostingFailedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(InventoryIntegrationEventTypes.StockMovementPostingFailed, failedEvent.EventType);
        Assert.Equal("NEGATIVE_ON_HAND", failedEvent.Payload.FailureCode);
        Assert.Equal("OUT-001", failedEvent.Payload.SourceDocumentId);
        Assert.Equal("idem-out-001", failedEvent.Payload.IdempotencyKey);
        Assert.Empty(dbContext.StockMovements);
    }

    [Theory]
    [InlineData("count-adjustment", -2.5)]
    [InlineData("status-transfer-out", -1)]
    [InlineData("status-transfer-in", 1)]
    public async Task Movement_requested_consumer_rejects_internal_movement_types_from_external_events(
        string movementType,
        decimal quantity)
    {
        await using var dbContext = CreateContext();
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "IN-SEED",
            "LINE-001",
            "idem-seed",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "LOC-A-01",
            "LOT-001",
            null,
            "qualified",
            "company",
            "owner-001",
            10m,
            8m));
        ledger.FreezeForCount("COUNT-001");
        dbContext.StockLedgers.Add(ledger);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);

        await handler.HandleAsync(CreateRequestedEvent($"evt-internal-{movementType}") with
        {
            Payload = CreateRequestedEvent($"evt-internal-{movementType}").Payload with
            {
                MovementType = movementType,
                SourceDocumentId = $"INTERNAL-{movementType}",
                IdempotencyKey = $"idem-internal-{movementType}",
                Quantity = quantity,
                UnitCost = 8m,
            },
        }, CancellationToken.None);

        var failedEvent = Assert.IsType<StockMovementPostingFailedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(InventoryPostingFailureCodes.PostingRejected, failedEvent.Payload.FailureCode);
        Assert.Equal(movementType, failedEvent.Payload.MovementType);
        Assert.True(ledger.IsFrozenForCount);
        Assert.Equal("COUNT-001", ledger.FrozenCountTaskCode);
        Assert.Equal(10m, ledger.OnHandQuantity);
        Assert.Equal(80m, ledger.InventoryValue);
        Assert.DoesNotContain(dbContext.StockMovements, x => x.SourceDocumentId.StartsWith("INTERNAL-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Movement_requested_consumer_rejects_unknown_owner_type_from_external_events()
    {
        await using var dbContext = CreateContext();
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);

        await handler.HandleAsync(CreateRequestedEvent("evt-unknown-owner") with
        {
            Payload = CreateRequestedEvent("evt-unknown-owner").Payload with
            {
                OwnerType = "shadow-owner",
                SourceDocumentId = "IN-UNKNOWN-OWNER",
                IdempotencyKey = "idem-unknown-owner",
            },
        }, CancellationToken.None);

        var failedEvent = Assert.IsType<StockMovementPostingFailedIntegrationEvent>(Assert.Single(publisher.Published));
        Assert.Equal(InventoryPostingFailureCodes.PostingRejected, failedEvent.Payload.FailureCode);
        Assert.Equal("shadow-owner", failedEvent.Payload.OwnerType);
        Assert.Empty(dbContext.StockMovements);
        Assert.Empty(dbContext.StockLedgers);
    }

    [Fact]
    public async Task Movement_requested_consumer_normalizes_owner_type_aliases_before_posting()
    {
        await using var dbContext = CreateContext();
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore(),
            new RecordingIntegrationEventPublisher());

        await handler.HandleAsync(CreateRequestedEvent("evt-owner-alias") with
        {
            Payload = CreateRequestedEvent("evt-owner-alias").Payload with
            {
                OwnerType = "internal",
                SourceDocumentId = "IN-OWNER-ALIAS",
                IdempotencyKey = "idem-owner-alias",
            },
        }, CancellationToken.None);

        var ledger = Assert.Single(dbContext.StockLedgers);
        var movement = Assert.Single(dbContext.StockMovements);
        Assert.Equal("company", ledger.OwnerType);
        Assert.Equal("company", movement.OwnerType);
    }

    [Fact]
    public async Task Movement_requested_consumer_leaves_unexpected_invalid_operation_for_retry()
    {
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new FailingSender(new InvalidOperationException("Transient infrastructure failure.")),
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(CreateRequestedEvent("evt-transient"), CancellationToken.None));

        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Movement_requested_consumer_leaves_unexpected_argument_exception_for_retry()
    {
        var publisher = new RecordingIntegrationEventPublisher();
        var handler = new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new FailingSender(new ArgumentNullException("request")),
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(CreateRequestedEvent("evt-argument-bug"), CancellationToken.None));

        Assert.Empty(publisher.Published);
    }

    private static InventoryMovementRequestedIntegrationEvent CreateRequestedEvent(string eventId)
    {
        return new InventoryMovementRequestedIntegrationEvent(
            eventId,
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessWms,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:wms",
            "wms:inventory-movement-requested:org-001:env-dev:IN-001:idem-in-001",
            new InventoryMovementRequestedPayload(
                "inbound",
                "wms",
                "IN-001",
                "LINE-001",
                "idem-in-001",
                "SKU-FG-1000",
                "kg",
                "SITE-01",
                "LOC-A-01",
                "LOT-001",
                null,
                "qualified",
                "company",
                "owner-001",
                5m,
                DateTimeOffset.UtcNow));
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(
        string eventType,
        StockReleaseDimensionPayload? stockRelease = null)
    {
        return new InspectionResultIntegrationEvent(
            "quality-event-001",
            eventType,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:quality",
            $"quality:inspection-result:org-001:env-dev:QI-001:{eventType}",
            new InspectionResultPayload(
                "QI-001",
                "PLAN-001",
                "receiving",
                "quality",
                "QI-001",
                "SKU-FG-1000",
                3m,
                eventType == QualityIntegrationEventTypes.InspectionPassed
                    ? "passed"
                    : eventType == QualityIntegrationEventTypes.InspectionConditionalReleased
                        ? "conditional-release"
                        : "rejected",
                null,
                [],
                DateTimeOffset.UtcNow,
                stockRelease));
    }

    private static InspectionResultIntegrationEvent CreateInspectionEventWithPayloadStockLocator(string eventType)
    {
        var integrationEvent = CreateInspectionEvent(eventType);
        return integrationEvent with
        {
            Payload = integrationEvent.Payload with
            {
                LotNo = "LOT-002",
                SerialNo = null,
                SiteCode = "SITE-01",
                LocationCode = "LOC-B-02",
                OwnerType = "company",
                OwnerId = "owner-001",
                UomCode = "kg",
            },
        };
    }

    private static StockLedger CreateQualityLedger(string locationCode, string lotNo, decimal quantity)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            locationCode,
            lotNo,
            null,
            StockQualityStatus.Quality,
            "company",
            "owner-001");
        ledger.ApplyMovement(StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            $"IN-{lotNo}",
            "LINE-001",
            $"idem-quality-in-{lotNo}",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            locationCode,
            lotNo,
            null,
            StockQualityStatus.Quality,
            "company",
            "owner-001",
            quantity,
            2m));
        return ledger;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-movement-requested-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class CommandExecutingSender(ApplicationDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockMovementCommand command)
            {
                var result = await new PostStockMovementCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)result;
            }

            if (request is PostStockStatusTransferCommand statusTransferCommand)
            {
                var result = await new PostStockStatusTransferCommandHandler(dbContext).Handle(statusTransferCommand, cancellationToken);
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

    private sealed class FailingSender(Exception exception) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            return Task.FromException<TResponse>(exception);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            return Task.FromException(exception);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            return Task.FromException<object?>(exception);
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
