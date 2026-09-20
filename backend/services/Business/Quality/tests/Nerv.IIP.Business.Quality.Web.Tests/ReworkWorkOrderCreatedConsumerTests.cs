using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.NonconformanceReports;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class ReworkWorkOrderCreatedConsumerTests
{
    [Fact]
    public async Task Same_scope_system_receipt_binds_and_replay_keeps_the_mes_work_order()
    {
        await using var db = CreateDbContext();
        var ncr = ReworkNcr();
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(db, deadLetters);
        var requested = await new GetNonconformanceReportQueryHandler(db).Handle(
            new GetNonconformanceReportQuery(ncr.Id, ncr.OrganizationId, ncr.EnvironmentId),
            CancellationToken.None);

        Assert.Equal("requested", requested.ReworkWorkOrderCreationStatus);
        Assert.Null(requested.ReworkWorkOrderId);

        await handler.HandleAsync(Created(ncr, "RW-0001"), CancellationToken.None);
        await handler.HandleAsync(Created(ncr, "RW-0001") with { EventId = "evt-rework-created-replay" }, CancellationToken.None);

        db.ChangeTracker.Clear();
        var reloaded = await db.NonconformanceReports.SingleAsync();
        var listed = await new ListNonconformanceReportsQueryHandler(db).Handle(
            new ListNonconformanceReportsQuery(ncr.OrganizationId, ncr.EnvironmentId, null, null, null),
            CancellationToken.None);
        Assert.Equal("RW-0001", reloaded.ReworkWorkOrderId);
        Assert.Equal("created", reloaded.ReworkWorkOrderCreationStatus);
        Assert.Equal("created", Assert.Single(listed.Items).ReworkWorkOrderCreationStatus);
        Assert.Equal("RW-0001", Assert.Single(listed.Items).ReworkWorkOrderId);
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Receipt_before_rework_disposition_is_dead_lettered_without_binding()
    {
        await using var db = CreateDbContext();
        var ncr = OpenNcr();
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(db, deadLetters);

        await handler.HandleAsync(Created(ncr, "RW-EARLY"), CancellationToken.None);

        Assert.Null(ncr.ReworkWorkOrderId);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("quality.reworkWorkOrderCreated.bindingConflict", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Cross_scope_receipt_is_dead_lettered_without_binding()
    {
        await using var db = CreateDbContext();
        var ncr = ReworkNcr();
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(db, deadLetters);

        await handler.HandleAsync(Created(ncr, "RW-CROSS") with { OrganizationId = "org-other" }, CancellationToken.None);

        Assert.Null(ncr.ReworkWorkOrderId);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("quality.reworkWorkOrderCreated.ncrNotFoundInScope", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Receipt_from_non_mes_source_is_dead_lettered_without_binding()
    {
        await using var db = CreateDbContext();
        var ncr = ReworkNcr();
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(db, deadLetters);

        await handler.HandleAsync(Created(ncr, "RW-UNTRUSTED") with { SourceService = "business-gateway" }, CancellationToken.None);

        Assert.Null(ncr.ReworkWorkOrderId);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("quality.reworkWorkOrderCreated.untrustedSource", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Receipt_with_mismatched_ncr_facts_is_dead_lettered_without_binding()
    {
        await using var db = CreateDbContext();
        var ncr = ReworkNcr();
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(db, deadLetters);
        var receipt = Created(ncr, "RW-MISMATCH");

        await handler.HandleAsync(
            receipt with { Payload = receipt.Payload with { SkuCode = "SKU-OTHER" } },
            CancellationToken.None);

        Assert.Null(ncr.ReworkWorkOrderId);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("quality.reworkWorkOrderCreated.payloadMismatch", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Different_work_order_receipt_is_dead_lettered_and_cannot_replace_the_system_binding()
    {
        await using var db = CreateDbContext();
        var ncr = ReworkNcr();
        db.NonconformanceReports.Add(ncr);
        await db.SaveChangesAsync();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = CreateHandler(db, deadLetters);
        await handler.HandleAsync(Created(ncr, "RW-0001"), CancellationToken.None);

        await handler.HandleAsync(Created(ncr, "RW-FORGED") with { EventId = "evt-rework-created-conflict" }, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal("RW-0001", (await db.NonconformanceReports.SingleAsync()).ReworkWorkOrderId);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("quality.reworkWorkOrderCreated.bindingConflict", deadLetter.FailureCode);
    }

    private static ReworkWorkOrderCreatedIntegrationEvent Created(NonconformanceReport ncr, string workOrderId) =>
        new(
            "evt-rework-created",
            MesIntegrationEventTypes.ReworkWorkOrderCreated,
            MesIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
            QualityIntegrationEventSources.BusinessMes,
            "corr-rework-created",
            "evt-rework-requested",
            ncr.OrganizationId,
            ncr.EnvironmentId,
            "system:business-mes",
            $"mes:rework-work-order-created:{ncr.OrganizationId}:{ncr.EnvironmentId}:{ncr.Id}",
            new ReworkWorkOrderCreatedPayload(
                ncr.Id.ToString(),
                ncr.NcrCode,
                workOrderId,
                "WO-SOURCE-001",
                "OP-10",
                ncr.SkuCode,
                ncr.DefectQuantity,
                ncr.BatchNo,
                ncr.SerialNo,
                DateTimeOffset.Parse("2026-08-29T12:00:00Z")));

    private static NonconformanceReport ReworkNcr()
    {
        var ncr = OpenNcr();
        ncr.SubmitDisposition(
            QualityNcrDispositionTypes.Rework,
            "approval-chain-001",
            [],
            [MrbReviewInput.Approve("qa-manager-001", "approved", DateTimeOffset.Parse("2026-08-29T10:00:00Z"))]);
        return ncr;
    }

    private static NonconformanceReport OpenNcr() =>
        NonconformanceReport.Open(
            "org-001",
            "env-dev",
            "NCR-2026-0001",
            "in-process",
            "DEF-001",
            "SKU-001",
            3m,
            "surface-defect",
            "LOT-001",
            "SN-001",
            []);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quality-rework-receipt-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static ReworkWorkOrderCreatedIntegrationEventHandlerForBindQualityNcr CreateHandler(
        ApplicationDbContext db,
        IIntegrationEventDeadLetterStore deadLetters) =>
        new(
            new ReworkWorkOrderBindingStore(db, new SaveChangesBindingWriter(db)),
            deadLetters);

    private sealed class SaveChangesBindingWriter(ApplicationDbContext db) : IReworkWorkOrderBindingWriter
    {
        public async Task<bool> TryWriteAsync(
            NonconformanceReport candidate,
            CancellationToken cancellationToken)
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
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
