using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class QualityInventoryInspectionReleaseAcceptanceTests
{
    [Fact]
    public async Task Quality_conditional_release_event_transfers_inventory_quality_stock_to_restricted()
    {
        await using var inventoryDb = CreateInventoryContext();
        SeedQualityLedger(inventoryDb, quantity: 5m);
        await inventoryDb.SaveChangesAsync();
        var qualityRecord = CreateQualityInspectionRecord(InspectionResultLineInput.ConditionalRelease(
            "appearance",
            "two minor scratches",
            "mrb-waiver",
            2m,
            []));
        var integrationEvent = new InspectionConditionalReleasedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(new InspectionConditionalReleasedDomainEvent(qualityRecord));
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            new InventoryCommandExecutingSender(inventoryDb),
            inventoryDb,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await inventoryDb.SaveChangesAsync();

        Assert.Equal(QualityIntegrationEventTypes.InspectionConditionalReleased, integrationEvent.EventType);
        Assert.Equal(QualityStockReleaseTargetStatuses.Restricted, integrationEvent.Payload.StockRelease?.TargetQualityStatus);
        Assert.Equal(2m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Quality).OnHandQuantity);
        Assert.Equal(3m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Restricted).OnHandQuantity);
        Assert.DoesNotContain(inventoryDb.StockLedgers, x => x.QualityStatus == StockQualityStatus.Blocked);
    }

    [Fact]
    public async Task Quality_reject_event_still_transfers_inventory_quality_stock_to_blocked()
    {
        await using var inventoryDb = CreateInventoryContext();
        SeedQualityLedger(inventoryDb, quantity: 5m);
        await inventoryDb.SaveChangesAsync();
        var qualityRecord = CreateQualityInspectionRecord(InspectionResultLineInput.Fail(
            "appearance",
            "surface crack",
            "crack",
            3m,
            []));
        var integrationEvent = new InspectionRejectedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(new InspectionRejectedDomainEvent(qualityRecord));
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            new InventoryCommandExecutingSender(inventoryDb),
            inventoryDb,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await inventoryDb.SaveChangesAsync();

        Assert.Equal(QualityIntegrationEventTypes.InspectionRejected, integrationEvent.EventType);
        Assert.Equal(QualityStockReleaseTargetStatuses.Blocked, integrationEvent.Payload.StockRelease?.TargetQualityStatus);
        Assert.Equal(2m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Quality).OnHandQuantity);
        Assert.Equal(3m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Blocked).OnHandQuantity);
    }

    private static InspectionRecord CreateQualityInspectionRecord(InspectionResultLineInput resultLine)
    {
        return InspectionRecord.Create(
            "org-001",
            "env-dev",
            null,
            "receiving",
            "purchase-receipt",
            "RCV-QUALITY-001",
            "SKU-FG-1000",
            3m,
            "LOT-001",
            null,
            [resultLine],
            resultLine.Result == InspectionLineResults.Passed ? null : "MRB disposition required",
            [],
            StockReleaseDimension.Create("kg", "SITE-01", "IQC-HOLD", StockQualityStatus.Quality, "company", "owner-001"));
    }

    private static void SeedQualityLedger(InventoryDbContext inventoryDb, decimal quantity)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "IQC-HOLD",
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
            "IN-QUALITY-SEED",
            "LINE-001",
            "idem-quality-seed",
            "SKU-FG-1000",
            "kg",
            "SITE-01",
            "IQC-HOLD",
            "LOT-001",
            null,
            StockQualityStatus.Quality,
            "company",
            "owner-001",
            quantity));
        inventoryDb.StockLedgers.Add(ledger);
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"quality-inventory-acceptance-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private sealed class FixedQualityIntegrationEventContextAccessor : IQualityIntegrationEventContextAccessor
    {
        public QualityIntegrationEventContext GetContext()
        {
            return new QualityIntegrationEventContext(
                "corr-quality-001",
                "quality-command-001",
                "user:qa-001");
        }
    }

    private sealed class InventoryCommandExecutingSender(InventoryDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockMovementCommand movementCommand)
            {
                var result = await new PostStockMovementCommandHandler(dbContext).Handle(movementCommand, cancellationToken);
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

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
