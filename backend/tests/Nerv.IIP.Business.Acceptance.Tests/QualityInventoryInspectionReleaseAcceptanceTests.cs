using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockStatusTransfers;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionRecordAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;

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

    [Fact]
    public async Task Ncr_scrap_disposition_scraps_blocked_inventory_and_closes_quality_ncr_with_movement_id()
    {
        await using var inventoryDb = CreateInventoryContext();
        await using var qualityDb = CreateQualityContext();
        SeedQualityLedger(inventoryDb, quantity: 5m);
        await inventoryDb.SaveChangesAsync();
        var inspection = CreateQualityInspectionRecord(InspectionResultLineInput.Fail(
            "appearance",
            "surface crack",
            "crack",
            3m,
            []));
        await ApplyInspectionResultAsync(inventoryDb, new InspectionRejectedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(new InspectionRejectedDomainEvent(inspection)));
        var ncr = NonconformanceReport.OpenFromInspection(
            "NCR-SCRAP-001",
            inspection,
            "crack",
            []);
        qualityDb.NonconformanceReports.Add(ncr);
        await qualityDb.SaveChangesAsync();
        ncr.ClearDomainEvents();

        ncr.SubmitDisposition(
            QualityNcrDispositionTypes.Scrap,
            "approval-chain-001",
            [],
            ApprovedMrbReview());
        var inventoryRequest = new NcrInventoryDispositionRequestedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(ncr.GetDomainEvents().OfType<NonconformanceReportInventoryDispositionRequestedDomainEvent>().Single());
        await new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
                new InventoryCommandExecutingSender(inventoryDb),
                new InMemoryIntegrationEventDeadLetterStore(),
                new RecordingIntegrationEventPublisher())
            .HandleAsync(inventoryRequest, CancellationToken.None);
        await inventoryDb.SaveChangesAsync();
        var scrapMovement = inventoryDb.StockMovements.Single(x => x.SourceDocumentId == ncr.Id.ToString() && x.MovementType == InventoryMovementTypes.Adjustment);
        var postedEvent = CreatePostedEventFromMovement(scrapMovement, inventoryRequest);

        var qualityPublisher = new RecordingIntegrationEventPublisher();
        await new StockMovementPostedIntegrationEventHandlerForCompleteQualityNcrInventoryDisposition(
                new QualityCommandExecutingSender(qualityDb, qualityPublisher),
                new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(postedEvent, CancellationToken.None);

        Assert.Equal(0m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Blocked).OnHandQuantity);
        Assert.Equal(-3m, scrapMovement.Quantity);
        Assert.Equal(StockQualityStatus.Blocked, scrapMovement.QualityStatus);
        qualityDb.ChangeTracker.Clear();
        var closedNcr = qualityDb.NonconformanceReports.Single(x => x.Id == ncr.Id);
        Assert.Equal("closed", closedNcr.Status);
        Assert.Equal(scrapMovement.Id.ToString(), closedNcr.ScrapMovementId);
        var closedEvent = Assert.IsType<NcrClosedIntegrationEvent>(Assert.Single(qualityPublisher.Published));
        Assert.Equal(QualityIntegrationEventTypes.NcrClosed, closedEvent.EventType);
        Assert.Equal(scrapMovement.Id.ToString(), closedEvent.Payload.ScrapMovementId);
    }

    [Fact]
    public async Task Ncr_rework_disposition_releases_blocked_inventory_to_restricted_without_mes_rework_closure()
    {
        await using var inventoryDb = CreateInventoryContext();
        SeedQualityLedger(inventoryDb, quantity: 5m);
        await inventoryDb.SaveChangesAsync();
        var inspection = CreateQualityInspectionRecord(InspectionResultLineInput.Fail(
            "appearance",
            "surface crack",
            "crack",
            3m,
            []));
        await ApplyInspectionResultAsync(inventoryDb, new InspectionRejectedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(new InspectionRejectedDomainEvent(inspection)));
        var ncr = NonconformanceReport.OpenFromInspection(
            "NCR-REWORK-001",
            inspection,
            "crack",
            []);
        ncr.ClearDomainEvents();

        ncr.SubmitDisposition(
            QualityNcrDispositionTypes.Rework,
            "approval-chain-001",
            [],
            ApprovedMrbReview());
        var inventoryRequest = new NcrInventoryDispositionRequestedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(ncr.GetDomainEvents().OfType<NonconformanceReportInventoryDispositionRequestedDomainEvent>().Single());
        await new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
                new InventoryCommandExecutingSender(inventoryDb),
                new InMemoryIntegrationEventDeadLetterStore(),
                new RecordingIntegrationEventPublisher())
            .HandleAsync(inventoryRequest, CancellationToken.None);
        await inventoryDb.SaveChangesAsync();

        Assert.Equal(InventoryMovementRequestTypes.StatusTransfer, inventoryRequest.Payload.MovementType);
        Assert.Equal(0m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Blocked).OnHandQuantity);
        Assert.Equal(3m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Restricted).OnHandQuantity);
        Assert.Equal("disposition-in-progress", ncr.Status);
        Assert.Null(ncr.ReworkWorkOrderId);
    }

    [Fact]
    public async Task Ncr_conditional_release_disposition_releases_blocked_inventory_to_restricted_and_closes_quality_ncr()
    {
        await using var inventoryDb = CreateInventoryContext();
        await using var qualityDb = CreateQualityContext();
        SeedQualityLedger(inventoryDb, quantity: 5m);
        await inventoryDb.SaveChangesAsync();
        var inspection = CreateQualityInspectionRecord(InspectionResultLineInput.Fail(
            "appearance",
            "surface crack",
            "crack",
            3m,
            []));
        await ApplyInspectionResultAsync(inventoryDb, new InspectionRejectedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(new InspectionRejectedDomainEvent(inspection)));
        var ncr = NonconformanceReport.OpenFromInspection(
            "NCR-CONDITIONAL-001",
            inspection,
            "crack",
            []);
        qualityDb.NonconformanceReports.Add(ncr);
        await qualityDb.SaveChangesAsync();
        ncr.ClearDomainEvents();

        ncr.SubmitDisposition(
            QualityNcrDispositionTypes.ConditionalRelease,
            "approval-chain-001",
            ["file-waiver-001"],
            ApprovedMrbReview());
        var inventoryRequest = new NcrInventoryDispositionRequestedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
            .Convert(ncr.GetDomainEvents().OfType<NonconformanceReportInventoryDispositionRequestedDomainEvent>().Single());
        await new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
                new InventoryCommandExecutingSender(inventoryDb),
                new InMemoryIntegrationEventDeadLetterStore(),
                new RecordingIntegrationEventPublisher())
            .HandleAsync(inventoryRequest, CancellationToken.None);
        await inventoryDb.SaveChangesAsync();
        var inboundMovement = inventoryDb.StockMovements.Single(x => x.SourceDocumentId == ncr.Id.ToString() && x.MovementType == InventoryMovementTypes.StatusTransferIn);
        var postedEvent = CreatePostedEventFromMovement(inboundMovement, inventoryRequest);

        var qualityPublisher = new RecordingIntegrationEventPublisher();
        await new StockMovementPostedIntegrationEventHandlerForCompleteQualityNcrInventoryDisposition(
                new QualityCommandExecutingSender(qualityDb, qualityPublisher),
                new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(postedEvent, CancellationToken.None);

        Assert.Equal(0m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Blocked).OnHandQuantity);
        Assert.Equal(3m, inventoryDb.StockLedgers.Single(x => x.QualityStatus == StockQualityStatus.Restricted).OnHandQuantity);
        Assert.Equal(StockQualityStatus.Restricted, inboundMovement.QualityStatus);
        qualityDb.ChangeTracker.Clear();
        var closedNcr = qualityDb.NonconformanceReports.Single(x => x.Id == ncr.Id);
        Assert.Equal("closed", closedNcr.Status);
        Assert.Null(closedNcr.ScrapMovementId);
        var closedEvent = Assert.IsType<NcrClosedIntegrationEvent>(Assert.Single(qualityPublisher.Published));
        Assert.Equal(QualityNcrDispositionTypes.ConditionalRelease, closedEvent.Payload.DispositionType);
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

    private static async Task ApplyInspectionResultAsync(InventoryDbContext inventoryDb, InspectionResultIntegrationEvent integrationEvent)
    {
        var handler = new QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer(
            new InventoryCommandExecutingSender(inventoryDb),
            inventoryDb,
            new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await inventoryDb.SaveChangesAsync();
    }

    private static StockMovementPostedIntegrationEvent CreatePostedEventFromMovement(
        StockMovement movement,
        InventoryMovementRequestedIntegrationEvent request)
    {
        return new StockMovementPostedIntegrationEvent(
            $"evt-{Guid.CreateVersion7():N}",
            InventoryIntegrationEventTypes.StockMovementPosted,
            InventoryIntegrationEventVersions.V1,
            DateTimeOffset.UtcNow,
            InventoryIntegrationEventSources.BusinessInventory,
            request.CorrelationId,
            request.EventId,
            movement.OrganizationId,
            movement.EnvironmentId,
            "system:business-inventory",
            $"inventory:stock-movement-posted:{movement.OrganizationId}:{movement.EnvironmentId}:{movement.SourceService}:{movement.SourceDocumentId}:{movement.IdempotencyKey}",
            new StockMovementPostedPayload(
                movement.Id.ToString(),
                movement.MovementType,
                movement.SourceService,
                movement.SourceDocumentId,
                movement.SourceDocumentLineId,
                movement.IdempotencyKey,
                movement.SkuCode,
                movement.UomCode,
                movement.SiteCode,
                movement.LocationCode,
                movement.LotNo,
                movement.SerialNo,
                movement.QualityStatus,
                movement.OwnerType,
                movement.OwnerId,
                movement.Quantity,
                movement.PostedAtUtc,
                movement.UnitCost,
                movement.MovementAmount));
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"quality-inventory-acceptance-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private static QualityDbContext CreateQualityContext()
    {
        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseInMemoryDatabase($"quality-ncr-disposition-acceptance-{Guid.NewGuid():N}")
            .Options;
        return new QualityDbContext(options, new NoopMediator());
    }

    private static IReadOnlyCollection<MrbReviewInput> ApprovedMrbReview()
    {
        return [MrbReviewInput.Approve("qa-manager-001", "MRB accepted disposition", DateTimeOffset.Parse("2026-06-16T08:00:00Z"))];
    }

    private sealed class RecordingIntegrationEventPublisher : NetCorePal.Extensions.DistributedTransactions.IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task NetCorePal.Extensions.DistributedTransactions.IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(
            TIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
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

    private sealed class QualityCommandExecutingSender(
        QualityDbContext dbContext,
        RecordingIntegrationEventPublisher publisher) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender only supports command requests without responses.");
        }

        public async Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            if (request is CompleteNonconformanceReportInventoryDispositionCommand command)
            {
                await new CompleteNonconformanceReportInventoryDispositionCommandHandler(new NonconformanceReportRepository(dbContext))
                    .Handle(command, cancellationToken);
                foreach (var domainEvent in dbContext.ChangeTracker.Entries<NonconformanceReport>()
                             .SelectMany(x => x.Entity.GetDomainEvents().OfType<NonconformanceReportClosedDomainEvent>())
                             .ToArray())
                {
                    var integrationEvent = new NcrClosedIntegrationEventConverter(new FixedQualityIntegrationEventContextAccessor())
                        .Convert(domainEvent);
                    await ((NetCorePal.Extensions.DistributedTransactions.IIntegrationEventPublisher)publisher)
                        .PublishAsync(integrationEvent, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request.GetType().FullName}");
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
