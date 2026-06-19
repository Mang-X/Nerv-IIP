using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Domain.DomainEvents;
using Nerv.IIP.Business.Quality.Infrastructure.Repositories;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;
using QualityDbContext = Nerv.IIP.Business.Quality.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class MesQualityDefectNcrAcceptanceTests
{
    [Fact]
    public async Task Mes_defect_event_opens_quality_ncr_and_quality_disposition_updates_mes_by_payload_source_document()
    {
        await using var mesDb = CreateMesContext();
        await using var qualityDb = CreateQualityContext();
        var defect = DefectRecord.Create(
            "org-001",
            "env-dev",
            "DEF-ACCEPT-001",
            "WO-ACCEPT-001",
            "OP-10",
            "SURFACE",
            2m,
            DateTimeOffset.Parse("2026-06-15T10:00:00Z"));
        mesDb.DefectRecords.Add(defect);
        await mesDb.SaveChangesAsync();

        var defectRaisedEvent = new DefectRaisedIntegrationEventConverter()
            .Convert(new DefectRaisedDomainEvent(defect));
        var qualityHandler = new DefectRaisedIntegrationEventHandlerForOpenNcr(
            qualityDb,
            new QualityCommandExecutingSender(qualityDb),
            new InMemoryIntegrationEventDeadLetterStore());

        await qualityHandler.HandleAsync(defectRaisedEvent, CancellationToken.None);
        await qualityDb.SaveChangesAsync();

        var ncr = await qualityDb.NonconformanceReports.SingleAsync();
        Assert.Equal(defect.DefectNo, ncr.SourceDocumentId);
        Assert.Equal("in-process", ncr.SourceType);
        ncr.ClearDomainEvents();
        ncr.SubmitDisposition(
            "scrap",
            "approval-chain-001",
            [],
            [MrbReviewInput.Approve("qa-lead-001", "scrap accepted", DateTimeOffset.Parse("2026-06-15T10:30:00Z"))]);
        var dispositionEvent = new NcrDispositionDecidedIntegrationEventConverter(
                new FixedQualityIntegrationEventContextAccessor())
            .Convert(new NonconformanceReportDispositionDecidedDomainEvent(ncr));
        Assert.Equal("quality-command-001", dispositionEvent.CausationId);
        Assert.Equal(defect.DefectNo, dispositionEvent.Payload.SourceDocumentId);

        var mesHandler = new NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect(
            mesDb,
            new InMemoryIntegrationEventDeadLetterStore());
        await mesHandler.HandleAsync(dispositionEvent, CancellationToken.None);
        await mesDb.SaveChangesAsync();

        var updatedDefect = await mesDb.DefectRecords.SingleAsync();
        Assert.Equal(DefectRecord.ScrapAcceptedStatus, updatedDefect.Status);
        Assert.Equal(ncr.Id.ToString(), updatedDefect.NcrId);
        Assert.Equal(ncr.NcrCode, updatedDefect.NcrCode);
        Assert.Equal("scrap", updatedDefect.DispositionType);
    }

    private static MesDbContext CreateMesContext()
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseInMemoryDatabase($"mes-quality-acceptance-mes-{Guid.NewGuid():N}")
            .Options;
        return new MesDbContext(options, new NoopMediator());
    }

    private static QualityDbContext CreateQualityContext()
    {
        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseInMemoryDatabase($"mes-quality-acceptance-quality-{Guid.NewGuid():N}")
            .Options;
        return new QualityDbContext(options, new NoopMediator());
    }

    private sealed class QualityCommandExecutingSender(QualityDbContext dbContext) : ISender
    {
        private readonly FixedNonconformanceReportCodeGenerator codeGenerator = new();

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateNonconformanceReportCommand command)
            {
                var result = await new CreateNonconformanceReportCommandHandler(
                        new NonconformanceReportRepository(dbContext),
                        codeGenerator)
                    .Handle(command, cancellationToken);
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

    private sealed class FixedNonconformanceReportCodeGenerator : INonconformanceReportCodeGenerator
    {
        public Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("NCR-ACCEPT-001");
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
