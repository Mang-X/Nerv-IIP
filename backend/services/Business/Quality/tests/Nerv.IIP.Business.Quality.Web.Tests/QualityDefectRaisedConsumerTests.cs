using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.NonconformanceReportAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.Commands.NonconformanceReports;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;
using QualityRepository = Nerv.IIP.Business.Quality.Infrastructure.Repositories.NonconformanceReportRepository;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class QualityDefectRaisedConsumerTests
{
    [Fact]
    public async Task Defect_raised_consumer_creates_ncr_with_mes_defect_no_as_source_document()
    {
        await using var dbContext = CreateDbContext(nameof(Defect_raised_consumer_creates_ncr_with_mes_defect_no_as_source_document));
        var handler = new DefectRaisedIntegrationEventHandlerForOpenNcr(
            dbContext,
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateDefectRaisedEvent(defectNo: "DEF-001"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var ncr = await dbContext.NonconformanceReports.SingleAsync();
        Assert.Equal("in-process", ncr.SourceType);
        Assert.Equal("DEF-001", ncr.SourceDocumentId);
        Assert.Equal("MES-SKU-UNRESOLVED", ncr.SkuCode);
        Assert.Equal("SURFACE", ncr.DefectReason);
        Assert.Equal(2m, ncr.DefectQuantity);
    }

    [Fact]
    public async Task Defect_raised_consumer_is_idempotent_by_mes_defect_source_document()
    {
        await using var dbContext = CreateDbContext(nameof(Defect_raised_consumer_is_idempotent_by_mes_defect_source_document));
        var handler = new DefectRaisedIntegrationEventHandlerForOpenNcr(
            dbContext,
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateDefectRaisedEvent(defectNo: "DEF-001", eventId: "evt-defect-001"), CancellationToken.None);
        await handler.HandleAsync(CreateDefectRaisedEvent(defectNo: "DEF-001", eventId: "evt-defect-002"), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var ncr = Assert.Single(dbContext.NonconformanceReports);
        Assert.Equal("DEF-001", ncr.SourceDocumentId);
    }

    [Fact]
    public async Task Defect_raised_consumer_treats_mes_source_unique_conflict_as_concurrent_duplicate()
    {
        await using var dbContext = CreateDbContext(nameof(Defect_raised_consumer_treats_mes_source_unique_conflict_as_concurrent_duplicate));
        var handler = new DefectRaisedIntegrationEventHandlerForOpenNcr(
            dbContext,
            new UniqueConflictSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore());

        await handler.HandleAsync(CreateDefectRaisedEvent(defectNo: "DEF-001"), CancellationToken.None);

        Assert.Empty(dbContext.ChangeTracker.Entries<NonconformanceReport>());
        Assert.Empty(dbContext.NonconformanceReports.Local);
    }

    [Fact]
    public async Task Defect_raised_consumer_does_not_swallow_unrelated_database_update_errors()
    {
        await using var dbContext = CreateDbContext(nameof(Defect_raised_consumer_does_not_swallow_unrelated_database_update_errors));
        var handler = new DefectRaisedIntegrationEventHandlerForOpenNcr(
            dbContext,
            new UnrelatedDatabaseFailureSender(),
            new InMemoryIntegrationEventDeadLetterStore());

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(CreateDefectRaisedEvent(defectNo: "DEF-001"), CancellationToken.None));

        Assert.DoesNotContain("ux_ncr_mes_defect_source", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DefectRaisedIntegrationEvent CreateDefectRaisedEvent(string defectNo, string eventId = "evt-defect-001")
    {
        return new DefectRaisedIntegrationEvent(
            eventId,
            QualityIntegrationEventTypes.DefectRaised,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-15T10:00:00Z"),
            QualityIntegrationEventSources.BusinessMes,
            "corr-001",
            "mes-command-001",
            "org-001",
            "env-dev",
            "system:mes",
            $"mes:defect-raised:org-001:env-dev:{defectNo}",
            new DefectRaisedPayload(
                defectNo,
                "WO-001",
                "OP-10",
                "SURFACE",
                2m,
                DateTimeOffset.Parse("2026-06-15T10:00:00Z")));
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class FixedNonconformanceReportCodeGenerator : INonconformanceReportCodeGenerator
    {
        private int sequence;

        public Task<string> NextAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sequence++;
            return Task.FromResult($"NCR-TEST-{sequence:000}");
        }
    }

    private sealed class CommandExecutingSender(ApplicationDbContext dbContext) : ISender
    {
        private readonly FixedNonconformanceReportCodeGenerator codeGenerator = new();

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateNonconformanceReportCommand command)
            {
                var result = await new CreateNonconformanceReportCommandHandler(
                        new QualityRepository(dbContext),
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

    private sealed class UniqueConflictSender(ApplicationDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateNonconformanceReportCommand command)
            {
                dbContext.NonconformanceReports.Add(NonconformanceReport.Open(
                    command.OrganizationId,
                    command.EnvironmentId,
                    "NCR-TEST-CONFLICT",
                    command.SourceType,
                    command.SourceDocumentId,
                    command.SkuCode,
                    command.DefectQuantity,
                    command.DefectReason,
                    command.BatchNo,
                    command.SerialNo,
                    command.AttachmentFileIds));
                throw new DbUpdateException("duplicate key violates unique constraint \"ux_ncr_mes_defect_source\"");
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

    private sealed class UnrelatedDatabaseFailureSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("duplicate key violates unique constraint \"ux_quality_unrelated\"");
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
