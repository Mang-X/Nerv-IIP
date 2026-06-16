using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class QualityInspectionResultConsumerTests
{
    [Fact]
    public async Task Passed_quality_inspection_transfers_stock_from_inspection_to_qualified()
    {
        await using var dbContext = CreateContext();
        SeedInspectionLedger(dbContext, 10m);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatus(
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateInspectionEvent(QualityIntegrationEventTypes.InspectionPassed, "passed"), CancellationToken.None);

        var movements = dbContext.StockMovements.Where(x => x.SourceService == "quality").OrderBy(x => x.QualityStatus).ToArray();
        Assert.Equal(2, movements.Length);
        Assert.Contains(movements, x => x.QualityStatus == "inspection" && x.Quantity == -10m);
        Assert.Contains(movements, x => x.QualityStatus == "qualified" && x.Quantity == 10m);
        Assert.Equal(0m, dbContext.StockLedgers.Single(x => x.QualityStatus == "inspection").OnHandQuantity);
        Assert.Equal(10m, dbContext.StockLedgers.Single(x => x.QualityStatus == "qualified").OnHandQuantity);
    }

    [Fact]
    public async Task Rejected_quality_inspection_transfers_stock_from_inspection_to_quarantine_idempotently()
    {
        await using var dbContext = CreateContext();
        SeedInspectionLedger(dbContext, 10m);
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatus(
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore());
        var integrationEvent = CreateInspectionEvent(QualityIntegrationEventTypes.InspectionRejected, "rejected");

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Equal(2, dbContext.StockMovements.Count(x => x.SourceService == "quality"));
        Assert.Equal(0m, dbContext.StockLedgers.Single(x => x.QualityStatus == "inspection").OnHandQuantity);
        Assert.Equal(10m, dbContext.StockLedgers.Single(x => x.QualityStatus == "quarantine").OnHandQuantity);
    }

    private static InspectionResultIntegrationEvent CreateInspectionEvent(string eventType, string result)
    {
        return new InspectionResultIntegrationEvent(
            "evt-quality-001",
            eventType,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-16T08:00:00Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cmd-001",
            "org-001",
            "env-dev",
            "user:qa-001",
            $"quality:{eventType}:org-001:env-dev:RCV-001",
            new InspectionResultPayload(
                "inspection-record-001",
                "inspection-plan-001",
                "receiving",
                "purchase-receipt",
                "RCV-001",
                "SKU-RM-1000",
                10m,
                result,
                null,
                [],
                DateTimeOffset.Parse("2026-06-16T08:00:00Z"),
                new StockReleaseDimensionPayload(
                    "ea",
                    "SITE-01",
                    "IQC-HOLD",
                    "BATCH-001",
                    null,
                    "inspection",
                    "company",
                    null),
                [
                    new InspectionResultLinePayload(
                        "appearance",
                        null,
                        "ok",
                        null,
                        "passed",
                        null,
                        null),
                ]));
    }

    private static void SeedInspectionLedger(ApplicationDbContext dbContext, decimal quantity)
    {
        var ledger = StockLedger.Create(
            "org-001",
            "env-dev",
            "SKU-RM-1000",
            "ea",
            "SITE-01",
            "IQC-HOLD",
            "BATCH-001",
            null,
            "inspection",
            "company",
            null);
        var movement = StockMovement.Post(
            "org-001",
            "env-dev",
            "inbound",
            "wms",
            "RCV-001",
            "LINE-001",
            "seed-inspection-ledger",
            "SKU-RM-1000",
            "ea",
            "SITE-01",
            "IQC-HOLD",
            "BATCH-001",
            null,
            "inspection",
            "company",
            null,
            quantity);
        ledger.ApplyMovement(movement);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockMovements.Add(movement);
        dbContext.SaveChanges();
        ledger.ClearDomainEvents();
        movement.ClearDomainEvents();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"inventory-quality-result-{Guid.NewGuid():N}")
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
