using Microsoft.EntityFrameworkCore;
using MediatR;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockReservations;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.CountExecutionAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.OutboundOrderAggregate;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Inventory;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using WmsDbContext = Nerv.IIP.Business.Wms.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class WmsInventoryRpcIdempotencyAcceptanceTests
{
    [Fact]
    public async Task Picking_task_retry_after_inventory_reservation_timeout_recovers_existing_reservation()
    {
        await using var wmsDb = CreateWmsContext();
        await using var inventoryDb = CreateInventoryContext();
        await SeedInventoryAsync(inventoryDb, "SKU-FG-1000", "LOC-A-01", "LOT-001", 10m, "seed-pick-retry-001");
        var outbound = OutboundOrder.Create(
            "org-001",
            "env-dev",
            "OUT-RPC-RETRY-001",
            "sales-delivery",
            "SO-RPC-001",
            "SITE-01",
            [new OutboundOrderLineDraft("LINE-001", "SKU-FG-1000", "kg", 4m, "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001")]);
        wmsDb.OutboundOrders.Add(outbound);
        await wmsDb.SaveChangesAsync(CancellationToken.None);
        var inventoryClient = new TimeoutAfterInventoryCommitClient(inventoryDb)
        {
            ThrowOnNextReservation = true,
        };
        var command = new CreatePickingTaskCommand(outbound.Id, "TASK-RPC-RETRY-001", "LINE-001", "LOC-A-01", "PACK-01", 4m);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            new CreatePickingTaskCommandHandler(wmsDb, inventoryClient).Handle(command, CancellationToken.None));

        Assert.Empty(wmsDb.WarehouseTasks);
        var reservation = Assert.Single(inventoryDb.StockReservations);
        var recoveredTaskId = await new CreatePickingTaskCommandHandler(wmsDb, inventoryClient).Handle(command, CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        Assert.Single(inventoryDb.StockReservations);
        var task = Assert.Single(wmsDb.WarehouseTasks);
        Assert.Equal(recoveredTaskId, task.Id);
        Assert.Equal(reservation.Id.ToString(), wmsDb.OutboundOrders.Include(x => x.Lines).Single().Lines.Single().InventoryReservationId);
    }

    [Fact]
    public async Task Count_execution_retry_after_inventory_freeze_timeout_recovers_existing_count_task()
    {
        await using var wmsDb = CreateWmsContext();
        await using var inventoryDb = CreateInventoryContext();
        await SeedInventoryAsync(inventoryDb, "SKU-FG-1000", "LOC-A-01", null, 10m, "seed-count-retry-001", ownerId: null);
        var inventoryClient = new TimeoutAfterInventoryCommitClient(inventoryDb)
        {
            ThrowOnNextCountTask = true,
        };
        var command = new CreateCountExecutionCommand("org-001", "env-dev", "COUNT-RPC-RETRY-001", "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", 10m);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            new CreateCountExecutionCommandHandler(wmsDb, inventoryClient).Handle(command, CancellationToken.None));

        Assert.Empty(wmsDb.CountExecutions);
        var inventoryTask = Assert.Single(inventoryDb.StockCountTasks);
        var recoveredCountId = await new CreateCountExecutionCommandHandler(wmsDb, inventoryClient).Handle(command, CancellationToken.None);
        await wmsDb.SaveChangesAsync(CancellationToken.None);

        Assert.Single(inventoryDb.StockCountTasks);
        var count = Assert.Single(wmsDb.CountExecutions);
        Assert.Equal(recoveredCountId, count.Id);
        Assert.Equal(inventoryTask.Id.ToString(), count.InventoryCountTaskId);
        Assert.True(inventoryDb.StockLedgers.Single().IsFrozenForCount);
    }

    private static async Task SeedInventoryAsync(
        InventoryDbContext inventoryDb,
        string skuCode,
        string locationCode,
        string? lotNo,
        decimal quantity,
        string idempotencyKey,
        string? ownerId = "owner-001")
    {
        await new PostStockMovementCommandHandler(inventoryDb).Handle(
            new PostStockMovementCommand(
                "org-001",
                "env-dev",
                "inbound",
                "wms",
                "SEED",
                idempotencyKey,
                idempotencyKey,
                skuCode,
                "kg",
                "SITE-01",
                locationCode,
                lotNo,
                null,
                "qualified",
                "company",
                ownerId,
                quantity),
            CancellationToken.None);
        await inventoryDb.SaveChangesAsync(CancellationToken.None);
    }

    private static WmsDbContext CreateWmsContext()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase($"wms-rpc-idempotency-{Guid.NewGuid():N}")
            .Options;
        return new WmsDbContext(options, new NoopMediator());
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"inventory-rpc-idempotency-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private sealed class TimeoutAfterInventoryCommitClient(InventoryDbContext inventoryDb) : IWmsInventoryReservationClient
    {
        public bool ThrowOnNextReservation { get; set; }
        public bool ThrowOnNextCountTask { get; set; }

        public async Task<WmsInventoryReservationResult> ReserveAsync(
            WmsInventoryReservationRequest request,
            CancellationToken cancellationToken)
        {
            var result = await new ReserveStockCommandHandler(inventoryDb).Handle(
                new ReserveStockCommand(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.SourceService,
                    request.SourceDocumentId,
                    request.SourceDocumentLineId,
                    request.IdempotencyKey,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId,
                    request.Quantity,
                    request.ProductionDate,
                    request.ExpiryDate),
                cancellationToken);
            await inventoryDb.SaveChangesAsync(cancellationToken);
            if (ThrowOnNextReservation)
            {
                ThrowOnNextReservation = false;
                throw new TimeoutException("Simulated timeout after Inventory committed the reservation.");
            }

            return new WmsInventoryReservationResult(
                result.ReservationId.ToString(),
                result.ReservedQuantity,
                result.AvailableQuantity,
                result.LotNo,
                result.ProductionDate,
                result.ExpiryDate);
        }

        public Task<WmsInventoryFefoReservationResult> ReserveFefoAsync(
            WmsInventoryFefoReservationRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test uses explicit lot reservations.");
        }

        public Task<WmsInventoryReservationReleaseResult> ReleaseAsync(
            WmsInventoryReservationReleaseRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test does not release reservations.");
        }

        public async Task<WmsInventoryCountTaskResult> CreateCountTaskAsync(
            WmsInventoryCountTaskRequest request,
            CancellationToken cancellationToken)
        {
            var result = await new CreateStockCountTaskCommandHandler(inventoryDb).Handle(
                new CreateStockCountTaskCommand(
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.CountTaskCode,
                    request.SkuCode,
                    request.UomCode,
                    request.SiteCode,
                    request.LocationCode,
                    request.LotNo,
                    request.SerialNo,
                    request.QualityStatus,
                    request.OwnerType,
                    request.OwnerId,
                    request.IdempotencyKey),
                cancellationToken);
            await inventoryDb.SaveChangesAsync(cancellationToken);
            if (ThrowOnNextCountTask)
            {
                ThrowOnNextCountTask = false;
                throw new TimeoutException("Simulated timeout after Inventory committed the count freeze.");
            }

            return new WmsInventoryCountTaskResult(result.CountTaskId.ToString(), result.ExpectedLedgerVersion);
        }

        public Task<WmsInventoryCountAdjustmentResult> ConfirmCountAdjustmentAsync(
            WmsInventoryCountAdjustmentRequest request,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException("This test does not confirm count adjustments.");
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
