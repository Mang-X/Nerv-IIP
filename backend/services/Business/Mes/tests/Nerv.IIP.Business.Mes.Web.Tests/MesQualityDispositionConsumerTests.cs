using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class MesQualityDispositionConsumerTests
{
    [Fact]
    public async Task Ncr_disposition_consumer_updates_matching_mes_defect()
    {
        await using var dbContext = CreateDbContext(nameof(Ncr_disposition_consumer_updates_matching_mes_defect));
        dbContext.DefectRecords.Add(DefectRecord.Create(
            "org-001",
            "env-dev",
            "DEF-001",
            "WO-001",
            "OP-10",
            "SURFACE",
            1m,
            DateTimeOffset.Parse("2026-06-15T10:00:00Z")));
        await dbContext.SaveChangesAsync();

        var handler = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateDispositionEvent(sourceDocumentId: "DEF-001", dispositionType: "Rework", reworkWorkOrderId: "RW-WO-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var defect = await dbContext.DefectRecords.SingleAsync();
        Assert.Equal(DefectRecord.ReworkPendingStatus, defect.Status);
        Assert.Equal("NCR-001", defect.NcrId);
        Assert.Equal("NCR-2026-001", defect.NcrCode);
        Assert.Equal("Rework", defect.DispositionType);
        Assert.Equal("RW-WO-001", defect.DispositionReferenceId);
    }

    [Fact]
    public async Task Ncr_disposition_consumer_matches_by_payload_source_document_id_not_envelope_causation()
    {
        await using var dbContext = CreateDbContext(nameof(Ncr_disposition_consumer_matches_by_payload_source_document_id_not_envelope_causation));
        dbContext.DefectRecords.Add(DefectRecord.Create(
            "org-001",
            "env-dev",
            "DEF-REAL-001",
            "WO-001",
            "OP-10",
            "SURFACE",
            1m,
            DateTimeOffset.Parse("2026-06-15T10:00:00Z")));
        await dbContext.SaveChangesAsync();

        var handler = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateDispositionEvent(sourceDocumentId: "DEF-REAL-001", causationId: "quality-command-001", dispositionType: "Scrap", scrapMovementId: "MOV-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var defect = await dbContext.DefectRecords.SingleAsync();
        Assert.Equal(DefectRecord.ScrapAcceptedStatus, defect.Status);
        Assert.Equal("NCR-001", defect.NcrId);
        Assert.Equal("NCR-2026-001", defect.NcrCode);
        Assert.Equal("Scrap", defect.DispositionType);
        Assert.Equal("MOV-001", defect.DispositionReferenceId);
    }

    [Fact]
    public async Task Ncr_disposition_consumer_ignores_unmatched_ncr_without_dead_letter()
    {
        await using var dbContext = CreateDbContext(nameof(Ncr_disposition_consumer_ignores_unmatched_ncr_without_dead_letter));
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(dbContext, deadLetters);

        await handler.HandleAsync(CreateDispositionEvent(sourceDocumentId: "DEF-MISSING", dispositionType: "Scrap", scrapMovementId: "MOV-001"), CancellationToken.None);

        Assert.Empty(dbContext.DefectRecords);
        Assert.Empty(await deadLetters.ListAsync(
            NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    private static NcrDispositionDecidedIntegrationEvent CreateDispositionEvent(
        string sourceDocumentId,
        string dispositionType,
        string causationId = "quality-command-001",
        string? reworkWorkOrderId = null,
        string? scrapMovementId = null)
    {
        return new NcrDispositionDecidedIntegrationEvent(
            "evt-quality-disposition-001",
            QualityIntegrationEventTypes.DispositionDecided,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-15T11:00:00Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            causationId,
            "org-001",
            "env-dev",
            "quality",
            $"quality:disposition:{sourceDocumentId}",
            new NcrDispositionDecidedPayload(
                "NCR-001",
                "NCR-2026-001",
                "SKU-FG",
                1m,
                dispositionType,
                "approval-001",
                reworkWorkOrderId,
                scrapMovementId,
                null,
                DateTimeOffset.Parse("2026-06-15T11:00:00Z"))
            {
                SourceDocumentId = sourceDocumentId
            });
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
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
